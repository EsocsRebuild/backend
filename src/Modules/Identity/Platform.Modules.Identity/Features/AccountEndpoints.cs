using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Application.Abstractions;
using Platform.Application.Security;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Identity.Features;

public sealed record ProfileResponse(string Name, string Email, string? Phone, string? AvatarUrl);

public sealed record UpdateProfileRequest(string Name, string? Phone);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record MfaCodeRequest(string Code);

public sealed record ActiveSessionResponse(Guid Id, string Browser, string Os, string? Ip, string? Location, DateTimeOffset LastActiveAt,
    DateTimeOffset CreatedAt, bool Current);

public sealed record SecurityOverviewResponse(bool MfaEnabled, int RecoveryCodesRemaining, DateTimeOffset? PasswordChangedAt,
    IReadOnlyList<ActiveSessionResponse> Sessions);

public sealed record NotificationPreferencesDto(bool AccessRequests, bool FormResponses, bool CampaignReports, bool WeeklySummary);

internal sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Please enter your full name.").MinimumLength(3).MaximumLength(120);
        RuleFor(x => x.Phone).Phone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Enter your current password.");
        RuleFor(x => x.NewPassword).NewPassword().NotEqual(x => x.CurrentPassword).WithMessage("Choose a password you haven’t used here.");
    }
}

/// <summary>The signed-in administrator's own account (API contract §5).</summary>
public static class AccountEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("me").WithTags("My account").RequireAuthorization();
        me.MapGet("/profile", GetProfile).WithSummary("My profile");
        me.MapPatch("/profile", UpdateProfile).WithValidation<UpdateProfileRequest>().WithSummary("Update my name and phone");
        me.MapPost("/password", ChangePassword).WithValidation<ChangePasswordRequest>().RequireRateLimiting("auth")
            .WithSummary("Change my password (signs out my other devices)");
        me.MapGet("/security", Security).WithSummary("Two-step status, recovery codes left and signed-in devices");
        me.MapPost("/mfa/setup", SetupMfa).RequireSudo().WithSummary("Start authenticator-app setup");
        me.MapPost("/mfa/enable", EnableMfa).RequireRateLimiting("auth").WithSummary("Confirm a code, turn on two-step and get recovery codes");
        me.MapDelete("/mfa", DisableMfa).RequireSudo().WithSummary("Turn off two-step verification");
        me.MapPost("/mfa/recovery-codes", RegenerateCodes).RequireSudo().WithSummary("Replace my recovery codes");
        me.MapDelete("/sessions/{id:guid}", RevokeSession).WithSummary("Sign out one device");
        me.MapPost("/sessions/revoke-others", RevokeOthers).RequireSudo().WithSummary("Sign out all my other devices");
        me.MapGet("/notification-preferences", GetPreferences).WithSummary("What I want to be told about");
        me.MapPut("/notification-preferences", SavePreferences).WithSummary("Save notification preferences");
    }

    private static Task<User?> Me(IdentityDbContext db, ICurrentUser user, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == user.UserId, ct);

    private static async Task<IResult> GetProfile(IdentityDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        await Me(db, currentUser, ct) is { } user
            ? Results.Ok(new ProfileResponse(user.Name, user.Email, user.Phone, user.AvatarUrl))
            : AuthErrors.SessionEnded.ToError();

    private static async Task<IResult> UpdateProfile(UpdateProfileRequest r, IdentityDbContext db, ICurrentUser currentUser, IAuditLog audit, CancellationToken ct)
    {
        var user = await Me(db, currentUser, ct);
        if (user is null)
        {
            return AuthErrors.SessionEnded.ToError();
        }

        var changes = AuditChanges.Diff(("name", user.Name, r.Name.Trim()), ("phone", user.Phone, string.IsNullOrWhiteSpace(r.Phone) ? null : r.Phone.Trim()));
        user.UpdateProfile(r.Name, string.IsNullOrWhiteSpace(r.Phone) ? null : r.Phone.Trim());
        audit.Record("account.updated", "Updated their profile", changes: changes);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest r, IdentityDbContext db, ICurrentUser currentUser, IPasswordHasher<User> hasher,
        PasswordPolicy policy, SessionService sessions, IEmailSender email, IAuditLog audit, IOptions<AuthOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var user = await Me(db, currentUser, ct);
        if (user is null)
        {
            return AuthErrors.SessionEnded.ToError();
        }

        var now = clock.GetUtcNow();
        if (user.PasswordHash is null || hasher.VerifyHashedPassword(user, user.PasswordHash, r.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedLogin(now, options.Value.MaxFailedAccessAttempts, options.Value.LockoutDuration);
            await db.SaveChangesAsync(ct);
            return Error.Validation("auth.wrong_password", "Your current password isn’t right.",
                new Dictionary<string, string[]> { ["current"] = ["Your current password isn’t right."] }).ToError();
        }

        if (await policy.ValidateAsync(r.NewPassword, user.Email, ct) is { } weak)
        {
            return AuthErrors.WeakPassword(weak, "next").ToError();
        }

        user.SetPassword(hasher.HashPassword(user, r.NewPassword), now);
        await sessions.RevokeAllAsync(user.Id, "password_changed", currentUser.SessionId, ct);
        audit.Record("account.password_changed", "Changed their password", AuditSeverity.Warning);
        await db.SaveChangesAsync(ct);
        await email.SendAsync(EmailTemplates.PasswordChanged(user.Email, user.Name), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Security(IdentityDbContext db, ICurrentUser currentUser, SessionService sessions, TimeProvider clock, CancellationToken ct)
    {
        var user = await Me(db, currentUser, ct);
        if (user is null)
        {
            return AuthErrors.SessionEnded.ToError();
        }

        var now = clock.GetUtcNow();
        var active = (await db.Sessions.AsNoTracking().Where(s => s.UserId == user.Id && s.RevokedAt == null && s.ExpiresAt > now).ToListAsync(ct))
            .Where(s => s.IsActive(now, sessions.IdleTimeout(s)))
            .OrderByDescending(s => s.Id == currentUser.SessionId).ThenByDescending(s => s.LastUsedAt)
            .Select(s =>
            {
                var (browser, os) = UserAgentParser.Parse(s.UserAgent);
                return new ActiveSessionResponse(s.Id, browser, os, s.IpAddress, null, s.LastUsedAt, s.CreatedAt, s.Id == currentUser.SessionId);
            })
            .ToList();

        return Results.Ok(new SecurityOverviewResponse(user.TwoFactorEnabled, user.TwoFactorEnabled ? user.RecoveryCodeHashes.Count : 0,
            user.PasswordChangedAt, active));
    }

    private static async Task<IResult> SetupMfa(IdentityDbContext db, ICurrentUser currentUser, TotpService totp, IOptions<AuthOptions> options, CancellationToken ct)
    {
        var user = await Me(db, currentUser, ct);
        if (user is null)
        {
            return AuthErrors.SessionEnded.ToError();
        }

        if (user.TwoFactorEnabled)
        {
            return Error.Conflict("mfa.already_enabled", "Two-step verification is already on.").ToError();
        }

        var (protectedSecret, secret) = totp.GenerateSecret();
        user.BeginTwoFactorSetup(protectedSecret);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { secret, otpauthUrl = totp.BuildOtpAuthUri(options.Value.TotpIssuer, user.Email, secret) });
    }

    private static async Task<IResult> EnableMfa(MfaCodeRequest r, IdentityDbContext db, ICurrentUser currentUser, TotpService totp, IAuditLog audit, CancellationToken ct)
    {
        var user = await Me(db, currentUser, ct);
        if (user?.TwoFactorSecret is null)
        {
            return Error.Conflict("mfa.not_started", "Start two-step setup first.").ToError();
        }

        if (totp.Verify(user.TwoFactorSecret, r.Code ?? string.Empty) is not { } step || !user.ConsumeTotpStep(step))
        {
            return AuthErrors.WrongCode.ToError();
        }

        var codes = TotpService.GenerateRecoveryCodes();
        user.EnableTwoFactor(codes.Select(SecretHasher.Hash));
        audit.Record("account.mfa_enabled", "Turned on two-step verification");
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { recoveryCodes = codes });
    }

    private static async Task<IResult> DisableMfa(IdentityDbContext db, ICurrentUser currentUser, IAuditLog audit, CancellationToken ct)
    {
        var user = await Me(db, currentUser, ct);
        if (user is null)
        {
            return AuthErrors.SessionEnded.ToError();
        }

        user.DisableTwoFactor();
        audit.Record("account.mfa_disabled", "Turned off two-step verification", AuditSeverity.Critical);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RegenerateCodes(IdentityDbContext db, ICurrentUser currentUser, IAuditLog audit, CancellationToken ct)
    {
        var user = await Me(db, currentUser, ct);
        if (user is null || !user.TwoFactorEnabled)
        {
            return Error.Conflict("mfa.not_enabled", "Turn on two-step verification first.").ToError();
        }

        var codes = TotpService.GenerateRecoveryCodes();
        user.ReplaceRecoveryCodes(codes.Select(SecretHasher.Hash));
        audit.Record("account.recovery_codes_regenerated", "Replaced their recovery codes", AuditSeverity.Warning);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { recoveryCodes = codes });
    }

    private static async Task<IResult> RevokeSession(Guid id, IdentityDbContext db, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == currentUser.UserId, ct);
        if (session is null)
        {
            return Error.NotFound("session.not_found", "That device isn’t signed in any more.").ToError();
        }

        session.Revoke(clock.GetUtcNow(), "revoked_by_user");
        audit.Record("account.session_revoked", $"Signed out {string.Join(" on ", UserAgentParser.Parse(session.UserAgent).Browser, UserAgentParser.Parse(session.UserAgent).Os)}");
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeOthers(IdentityDbContext db, ICurrentUser currentUser, SessionService sessions, IAuditLog audit, CancellationToken ct)
    {
        var revoked = await sessions.RevokeAllAsync(currentUser.RequiredUserId, "revoked_by_user", currentUser.SessionId, ct);
        audit.Record("account.sessions_revoked", $"Signed out {revoked} other device(s)", AuditSeverity.Warning);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { revoked });
    }

    private static async Task<IResult> GetPreferences(IdentityDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var membership = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.Id == currentUser.MembershipId, ct);
        var p = membership?.NotificationPreferences ?? new NotificationPreferences();
        return Results.Ok(new NotificationPreferencesDto(p.AccessRequests, p.FormResponses, p.CampaignReports, p.WeeklySummary));
    }

    private static async Task<IResult> SavePreferences(NotificationPreferencesDto r, IdentityDbContext db, ICurrentUser currentUser, IAuditLog audit, CancellationToken ct)
    {
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.Id == currentUser.MembershipId, ct);
        if (membership is null)
        {
            return AuthErrors.SessionEnded.ToError();
        }

        membership.SetNotificationPreferences(new NotificationPreferences
        {
            AccessRequests = r.AccessRequests,
            FormResponses = r.FormResponses,
            CampaignReports = r.CampaignReports,
            WeeklySummary = r.WeeklySummary,
        });
        audit.Record("account.notifications_updated", "Changed notification preferences");
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

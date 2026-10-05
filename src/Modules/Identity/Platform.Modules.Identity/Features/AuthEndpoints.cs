using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Application.Abstractions;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Contracts;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;
using Platform.Modules.Tenancy.Contracts;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;

namespace Platform.Modules.Identity.Features;

public sealed record LoginRequest(string Email, string Password, bool Remember = false);

public sealed record MfaVerifyRequest(string ChallengeToken, string Method, string Code, bool RememberDevice = false);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record ReauthenticateRequest(string Password);

public sealed record SignupRequest(string Name, string Email, string? Phone, Guid ParishId, Guid RequestedRoleId, string Password);

public sealed record VerifyEmailRequest(string Email, string Code);

public sealed record EmailOnlyRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string Password);

public sealed record AcceptInvitationRequest(string Name, string Password);

public sealed record SessionUserResponse(
    Guid Id, string Name, string Email, string? AvatarUrl, RoleRef Role, IReadOnlyList<string> Permissions, bool MfaEnabled, Guid? ParishId,
    Guid? ScopeUnitId = null, bool IsPlatformAdmin = false);

public sealed record RoleRef(Guid Id, string Name);

public sealed record PublicRoleResponse(Guid Id, string Name, string Description, string Icon);

public sealed record InvitationResponse(string Email, string? Name, string RoleName, string InvitedBy, DateTimeOffset ExpiresAt);

internal static class Rules
{
    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Enter your email address.")
            .EmailAddress().WithMessage("That email doesn’t look right. Check for typos.")
            .MaximumLength(256);

    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Choose a password.")
            .MinimumLength(PasswordPolicy.MinLength).WithMessage($"Use at least {PasswordPolicy.MinLength} characters. A short phrase works well.")
            .MaximumLength(PasswordPolicy.MaxLength).WithMessage($"Use {PasswordPolicy.MaxLength} characters or fewer.");

    public static IRuleBuilderOptions<T, string?> Phone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Matches(@"^[+\d][\d\s()-]{6,24}$").WithMessage("Enter a valid phone number, e.g. +234 800 000 0000.");

    public static IRuleBuilderOptions<T, string> SixDigitCode<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Matches(@"^\s*\d{6}\s*$").WithMessage("Enter the 6-digit code.");
}

internal sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter your password.").MaximumLength(PasswordPolicy.MaxLength);
    }
}

internal sealed class MfaVerifyValidator : AbstractValidator<MfaVerifyRequest>
{
    public MfaVerifyValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty().MaximumLength(4096);
        RuleFor(x => x.Method).Must(m => m is "totp" or "recovery").WithMessage("Choose how to verify.");
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
    }
}

internal sealed class SignupValidator : AbstractValidator<SignupRequest>
{
    public SignupValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MinimumLength(3).MaximumLength(120)
            .Must(n => n.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2).WithMessage("Please enter your first and last name.");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Phone).Phone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.ParishId).NotEmpty().WithMessage("Choose the parish you serve in.");
        RuleFor(x => x.RequestedRoleId).NotEmpty().WithMessage("Choose what you’ll mostly do.");
        RuleFor(x => x.Password).NewPassword();
    }
}

internal sealed class VerifyEmailValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Code).SixDigitCode();
    }
}

internal sealed class EmailOnlyValidator : AbstractValidator<EmailOnlyRequest>
{
    public EmailOnlyValidator() => RuleFor(x => x.Email).ValidEmail();
}

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MinimumLength(16).MaximumLength(1024);
        RuleFor(x => x.Password).NewPassword();
    }
}

internal sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Please enter your full name.").MinimumLength(3).MaximumLength(120);
        RuleFor(x => x.Password).NewPassword();
    }
}

/// <summary>Administrator authentication (API contract §4).</summary>
public static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var anonymous = endpoints.MapGroup("auth").WithTags("Authentication").AllowAnonymous().RequireRateLimiting("auth");
        anonymous.MapPost("/login", Login).WithValidation<LoginRequest>().WithSummary("Sign in with email and password");
        anonymous.MapPost("/mfa/verify", VerifyMfa).WithValidation<MfaVerifyRequest>().WithSummary("Complete sign-in with an authenticator or recovery code");
        anonymous.MapPost("/refresh", Refresh).WithSummary("Rotate the refresh token");
        anonymous.MapPost("/logout", Logout).WithSummary("End the session");
        anonymous.MapPost("/signup", Signup).WithValidation<SignupRequest>().WithSummary("Request administrator access (creates an access request)");
        anonymous.MapPost("/verify-email", VerifyEmail).WithValidation<VerifyEmailRequest>().WithSummary("Verify the email on an access request");
        anonymous.MapPost("/verify-email/resend", ResendCode).WithValidation<EmailOnlyRequest>().WithSummary("Send a new verification code");
        anonymous.MapPost("/password/forgot", ForgotPassword).WithValidation<EmailOnlyRequest>().WithSummary("Email a password reset link");
        anonymous.MapPost("/password/reset", ResetPassword).WithValidation<ResetPasswordRequest>().WithSummary("Choose a new password from a reset link");

        var session = endpoints.MapGroup("auth").WithTags("Authentication").RequireAuthorization();
        session.MapGet("/me", Me).WithSummary("The signed-in administrator, role and permissions");
        session.MapPost("/session/touch", Touch).WithSummary("Keep the session alive while the user is active");
        session.MapPost("/reauthenticate", Reauthenticate).RequireRateLimiting("auth").WithSummary("Confirm the password to get a 5-minute sudo token");

        var pub = endpoints.MapPublicGroup("", "Public");
        pub.MapGet("/requestable-roles", RequestableRoles).WithSummary("Roles offered on the sign-up page");
        pub.MapGet("/invitations/{token}", GetInvitation).WithSummary("Details of an invitation link");
        pub.MapPost("/invitations/{token}/accept", AcceptInvitation).WithValidation<AcceptInvitationRequest>()
            .RequireRateLimiting("auth").WithSummary("Accept an invitation and set a password");
    }

    // ---- Sign-in ---------------------------------------------------------------------------------

    private static async Task<IResult> Login(
        LoginRequest r, CredentialService credentials, SessionService sessions, TokenService tokens, IdentityDbContext db,
        IPasswordHasher<User> hasher, ITenantContext tenant, IAuditLog audit, IOptions<AuthOptions> options, CancellationToken ct)
    {
        var verified = await credentials.VerifyAsync(r.Email, r.Password, ct);
        if (verified.IsFailure)
        {
            // Someone who asked for access but isn't approved yet gets pointed to the right next step —
            // only when they know the password they chose, so nothing is revealed to strangers.
            if (verified.Error == AuthErrors.InvalidCredentials && tenant.TenantId is not null)
            {
                var email = r.Email.Trim().ToLowerInvariant();
                var request = await db.AccessRequests.AsNoTracking()
                    .Where(a => a.Email == email && a.Status == AccessRequestStatus.Pending)
                    .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
                if (request is not null && hasher.VerifyHashedPassword(null!, request.PasswordHash, r.Password) != PasswordVerificationResult.Failed)
                {
                    return request.EmailVerifiedAt is null
                        ? Results.Ok(new { status = "email_unverified", email = request.Email })
                        : AuthErrors.AwaitingApproval.ToError();
                }
            }

            return verified.Error.ToError();
        }

        var user = verified.Value;
        var membership = await FindStaffMembershipAsync(db, user.Id, tenant.TenantId, ct);
        if (membership is null && !user.IsPlatformAdmin)
        {
            audit.Record("auth.login_denied", $"{user.Email} has no administrator access", AuditSeverity.Warning,
                CredentialService.Target(user), actor: CredentialService.Actor(user));
            await db.SaveChangesAsync(ct);
            return AuthErrors.InvalidCredentials.ToError();
        }

        if (user.TwoFactorEnabled)
        {
            await db.SaveChangesAsync(ct);
            return Results.Ok(new
            {
                status = "mfa_required",
                challengeToken = tokens.CreateMfaChallenge(user, membership?.TenantId, r.Remember),
                expiresIn = (int)options.Value.MfaChallengeLifetime.TotalSeconds,
            });
        }

        audit.Record("auth.login", $"{user.Name} signed in", target: CredentialService.Target(user),
            actor: new AuditActor(membership?.Id, user.Name, user.Email));
        var issued = await sessions.StartAsync(user, membership, ClientType.Admin, r.Remember, ct);
        return Results.Ok(new { status = "authenticated", tokens = issued });
    }

    private static async Task<IResult> VerifyMfa(
        MfaVerifyRequest r, TokenService tokens, TotpService totp, SessionService sessions, IdentityDbContext db, IAuditLog audit,
        IOptions<AuthOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var challenge = await tokens.ReadMfaChallengeAsync(r.ChallengeToken);
        var user = challenge is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == challenge.UserId, ct);
        if (user is null || user.SecurityStamp != challenge!.Stamp || !user.TwoFactorEnabled || user.TwoFactorSecret is null)
        {
            return AuthErrors.ChallengeExpired.ToError();
        }

        var now = clock.GetUtcNow();
        if (user.IsLockedOut(now))
        {
            return AuthErrors.LockedOut.ToError();
        }

        var ok = r.Method == "recovery"
            ? user.RedeemRecoveryCode(SecretHasher.Hash(TotpService.NormaliseRecoveryCode(r.Code)))
            : totp.Verify(user.TwoFactorSecret, r.Code) is { } step && user.ConsumeTotpStep(step);

        if (!ok)
        {
            user.RegisterFailedLogin(now, options.Value.MaxFailedAccessAttempts, options.Value.LockoutDuration);
            audit.Record("auth.mfa_failed", $"Wrong two-step code for {user.Email}", AuditSeverity.Warning,
                CredentialService.Target(user), actor: CredentialService.Actor(user));
            await db.SaveChangesAsync(ct);
            return (user.IsLockedOut(now) ? AuthErrors.LockedOut : AuthErrors.WrongCode).ToError();
        }

        var membership = await FindStaffMembershipAsync(db, user.Id, challenge.TenantId, ct);
        if (r.Method == "recovery")
        {
            audit.Record("auth.recovery_code_used", $"{user.Name} signed in with a recovery code ({user.RecoveryCodeHashes.Count} left)",
                AuditSeverity.Warning, CredentialService.Target(user), actor: new AuditActor(membership?.Id, user.Name, user.Email));
        }

        audit.Record("auth.login", $"{user.Name} signed in with two-step verification", target: CredentialService.Target(user),
            actor: new AuditActor(membership?.Id, user.Name, user.Email));
        return Results.Ok(await sessions.StartAsync(user, membership, ClientType.Admin, challenge.Remember, ct));
    }

    private static async Task<IResult> Refresh(RefreshRequest? r, SessionService sessions, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(r?.RefreshToken)
            ? AuthErrors.SessionEnded.ToError()
            : (await sessions.RefreshAsync(r.RefreshToken, ct)).ToHttp();

    private static async Task<IResult> Logout(RefreshRequest? r, ICurrentUser currentUser, IdentityDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        UserSession? session = null;
        if (r?.RefreshToken is { } token && RefreshTokenFormat.TryParse(token, out var sessionId, out var hash))
        {
            session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
            if (session is not null && !SecretHasher.FixedTimeEquals(session.TokenHash, hash))
            {
                session = null;
            }
        }
        else if (currentUser.SessionId is { } sid)
        {
            session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sid, ct);
        }

        if (session is not null)
        {
            session.Revoke(clock.GetUtcNow(), "logout");
            audit.Record("auth.logout", "Signed out");
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> Me(ICurrentUser currentUser, ICurrentAccess access, IdentityDbContext db, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct);
        var profile = await access.GetAsync(ct);
        if (user is null || profile is null || !profile.IsStaff)
        {
            return AuthErrors.SessionEnded.ToError();
        }

        var roleId = await db.MembershipRoles.Where(r => r.MembershipId == profile.MembershipId).Select(r => (Guid?)r.RoleId).FirstOrDefaultAsync(ct);
        var role = roleId is null ? null : await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, ct);

        return Results.Ok(new SessionUserResponse(
            profile.MembershipId, user.Name, user.Email, user.AvatarUrl,
            new RoleRef(role?.Id ?? Guid.Empty, role?.Name ?? "No role"),
            Permissions.Ordered.Where(profile.Permissions.Contains).ToList(), user.TwoFactorEnabled,
            profile.ScopeUnitId, profile.ScopeUnitId, user.IsPlatformAdmin));
    }

    private static async Task<IResult> Touch(ICurrentUser currentUser, IdentityDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (currentUser.SessionId is { } sid)
        {
            await db.Sessions.Where(s => s.Id == sid && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, now), ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> Reauthenticate(
        ReauthenticateRequest r, ICurrentUser currentUser, IdentityDbContext db, IPasswordHasher<User> hasher, IAuditLog audit,
        IOptions<AuthOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct);
        var session = currentUser.SessionId is { } sid ? await db.Sessions.FirstOrDefaultAsync(s => s.Id == sid, ct) : null;
        if (user is null || session is null)
        {
            return AuthErrors.SessionEnded.ToError();
        }

        var now = clock.GetUtcNow();
        if (user.PasswordHash is null || hasher.VerifyHashedPassword(user, user.PasswordHash, r.Password ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedLogin(now, options.Value.MaxFailedAccessAttempts, options.Value.LockoutDuration);
            audit.Record("auth.reauth_failed", "Wrong password when confirming a sensitive action", AuditSeverity.Warning);
            await db.SaveChangesAsync(ct);
            return (user.IsLockedOut(now) ? AuthErrors.LockedOut : AuthErrors.WrongPassword).ToError();
        }

        var token = SecretHasher.NewSecret();
        session.GrantSudo(SecretHasher.Hash(token), now.Add(options.Value.SudoLifetime));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { sudoToken = token, expiresIn = (int)options.Value.SudoLifetime.TotalSeconds });
    }

    // ---- Access requests (sign-up) --------------------------------------------------------------

    private static async Task<IResult> Signup(
        SignupRequest r, ITenantContext tenant, IdentityDbContext db, IUnitDirectory units, PasswordPolicy policy, Argon2PasswordHasher hasher,
        OneTimeCodeService codes, IEmailSender email, ITenantDirectory tenants, IOptions<AuthOptions> options, IAuditLog audit, CancellationToken ct)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return Error.Validation("tenant.required", "Sign-up isn’t available here.").ToError();
        }

        var normalized = r.Email.Trim().ToLowerInvariant();
        if (await policy.ValidateAsync(r.Password, normalized, ct) is { } weak)
        {
            return AuthErrors.WeakPassword(weak).ToError();
        }

        var parishes = await units.GetParishesAsync(null, ct);
        if (parishes.All(p => p.Id != r.ParishId))
        {
            return Error.Validation("signup.invalid_parish", "Choose the parish you serve in.",
                new Dictionary<string, string[]> { ["parishId"] = ["Choose the parish you serve in."] }).ToError();
        }

        if (!await db.Roles.AnyAsync(x => x.Id == r.RequestedRoleId && x.IsRequestable, ct))
        {
            return Error.Validation("signup.invalid_role", "Choose what you’ll mostly do.",
                new Dictionary<string, string[]> { ["requestedRoleId"] = ["Choose what you’ll mostly do."] }).ToError();
        }

        var organisation = (await tenants.GetAsync(tenantId, ct))?.Name ?? "the organisation";
        var existingStaff = await (from m in db.Memberships
                                   join u in db.Users on m.UserId equals u.Id
                                   where u.Email == normalized && m.Kind == MembershipKind.Staff
                                   select m.Id).AnyAsync(ct);
        if (existingStaff)
        {
            // Same response as a new request: never reveal which emails have accounts.
            await email.SendAsync(EmailTemplates.AlreadyRegistered(normalized, organisation, $"{options.Value.AppBaseUrl}/login"), ct);
            return TypedResults.Created();
        }

        var pending = await db.AccessRequests.Where(a => a.Email == normalized && a.Status == AccessRequestStatus.Pending).ToListAsync(ct);
        db.AccessRequests.RemoveRange(pending);

        var request = AccessRequest.Submit(r.Name, normalized, r.Phone, r.ParishId, r.RequestedRoleId, hasher.HashPassword(r.Password));
        db.AccessRequests.Add(request);

        var code = await codes.IssueAsync(tenantId, CodePurpose.AccessRequestEmail, normalized, ct);
        if (code.IsSuccess)
        {
            await email.SendAsync(EmailTemplates.VerificationCode(normalized, r.Name, code.Value), ct);
        }

        audit.Record("access.requested", $"{request.Name} asked for access", target: new AuditTarget("access_request", request.Id.ToString(), request.Name),
            actor: new AuditActor(null, request.Name, request.Email));
        await db.SaveChangesAsync(ct);
        return TypedResults.Created();
    }

    private static async Task<IResult> VerifyEmail(VerifyEmailRequest r, ITenantContext tenant, IdentityDbContext db, OneTimeCodeService codes,
        IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var normalized = r.Email.Trim().ToLowerInvariant();
        var request = await db.AccessRequests.Where(a => a.Email == normalized && a.Status == AccessRequestStatus.Pending)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        var ok = await codes.VerifyAsync(tenant.TenantId, CodePurpose.AccessRequestEmail, normalized, r.Code, ct);
        if (!ok || request is null)
        {
            await db.SaveChangesAsync(ct);
            return AuthErrors.WrongCode.ToError();
        }

        request.MarkEmailVerified(clock.GetUtcNow());
        request.RaiseSubmitted();
        audit.Record("access.email_verified", $"{request.Name} verified their email", target: new AuditTarget("access_request", request.Id.ToString(), request.Name),
            actor: new AuditActor(null, request.Name, request.Email));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ResendCode(EmailOnlyRequest r, ITenantContext tenant, IdentityDbContext db, OneTimeCodeService codes,
        IEmailSender email, CancellationToken ct)
    {
        var normalized = r.Email.Trim().ToLowerInvariant();
        var request = await db.AccessRequests.AsNoTracking()
            .Where(a => a.Email == normalized && a.Status == AccessRequestStatus.Pending && a.EmailVerifiedAt == null)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        if (request is null)
        {
            return Results.NoContent();
        }

        var code = await codes.IssueAsync(tenant.TenantId, CodePurpose.AccessRequestEmail, normalized, ct);
        if (code.IsFailure)
        {
            return code.Error.ToError();
        }

        await db.SaveChangesAsync(ct);
        await email.SendAsync(EmailTemplates.VerificationCode(normalized, request.Name, code.Value), ct);
        return Results.NoContent();
    }

    // ---- Passwords -------------------------------------------------------------------------------

    private static async Task<IResult> ForgotPassword(EmailOnlyRequest r, IdentityDbContext db, UserTokenService resetTokens, IEmailSender email,
        IAuditLog audit, IOptions<AuthOptions> options, CancellationToken ct)
    {
        var normalized = r.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized && u.Status == UserStatus.Active, ct);
        if (user is not null)
        {
            var link = $"{options.Value.AppBaseUrl}/reset-password?token={Uri.EscapeDataString(resetTokens.CreatePasswordReset(user))}";
            await email.SendAsync(EmailTemplates.PasswordReset(user.Email, user.Name, link), ct);
            audit.Record("auth.password_reset_requested", $"Password reset requested for {user.Email}", target: CredentialService.Target(user),
                actor: CredentialService.Actor(user));
            await audit.FlushAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ResetPassword(ResetPasswordRequest r, IdentityDbContext db, UserTokenService resetTokens, PasswordPolicy policy,
        IPasswordHasher<User> hasher, SessionService sessions, IEmailSender email, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var payload = resetTokens.ReadPasswordReset(r.Token);
        var user = payload is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == payload.Value.UserId, ct);
        if (user is null || user.SecurityStamp != payload!.Value.Stamp)
        {
            return AuthErrors.InvalidLink.ToError();
        }

        if (await policy.ValidateAsync(r.Password, user.Email, ct) is { } weak)
        {
            return AuthErrors.WeakPassword(weak).ToError();
        }

        user.SetPassword(hasher.HashPassword(user, r.Password), clock.GetUtcNow());
        user.ConfirmEmail();
        user.RegisterSuccessfulLogin(clock.GetUtcNow());
        await sessions.RevokeAllAsync(user.Id, "password_reset", exceptSessionId: null, ct);
        audit.Record("auth.password_reset", $"{user.Name} reset their password", AuditSeverity.Warning, CredentialService.Target(user),
            actor: CredentialService.Actor(user));
        await db.SaveChangesAsync(ct);
        await email.SendAsync(EmailTemplates.PasswordChanged(user.Email, user.Name), ct);
        return Results.NoContent();
    }

    // ---- Public lookups & invitations -----------------------------------------------------------

    private static async Task<IResult> RequestableRoles(ITenantContext tenant, IdentityDbContext db, CancellationToken ct) =>
        tenant.TenantId is null
            ? Results.Ok(Array.Empty<PublicRoleResponse>())
            : Results.Ok(await db.Roles.AsNoTracking().Where(r => r.IsRequestable).OrderBy(r => r.Name)
                .Select(r => new PublicRoleResponse(r.Id, r.Name, r.Description ?? string.Empty, r.Icon)).ToListAsync(ct));

    private static async Task<StaffInvitation?> FindInvitationAsync(IdentityDbContext db, string token, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512)
        {
            return null;
        }

        var hash = SecretHasher.Hash(token);
        var invitation = await db.Invitations.IgnoreQueryFilters([QueryFilters.Tenant]).FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        return invitation is not null && invitation.IsUsable(now) ? invitation : null;
    }

    private static async Task<IResult> GetInvitation(string token, IdentityDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var invitation = await FindInvitationAsync(db, token, clock.GetUtcNow(), ct);
        if (invitation is null)
        {
            return Error.NotFound("invitation.invalid", "This invitation is invalid or has expired. Ask for a new one.").ToError();
        }

        var details = await (from m in db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
                             join u in db.Users on m.UserId equals u.Id
                             where m.Id == invitation.MembershipId
                             select new { u.Name, RoleId = m.Roles.Select(r => r.RoleId).FirstOrDefault() }).FirstAsync(ct);
        var roleName = await db.Roles.IgnoreQueryFilters([QueryFilters.Tenant]).Where(r => r.Id == details.RoleId).Select(r => r.Name).FirstOrDefaultAsync(ct);

        return Results.Ok(new InvitationResponse(invitation.Email, string.IsNullOrWhiteSpace(details.Name) ? null : details.Name,
            roleName ?? "Administrator", invitation.InvitedByName ?? "An administrator", invitation.ExpiresAt));
    }

    private static async Task<IResult> AcceptInvitation(
        string token, AcceptInvitationRequest r, IdentityDbContext db, ITenantContextSetter tenantSetter, PasswordPolicy policy,
        IPasswordHasher<User> hasher, IPermissionService permissions, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var invitation = await FindInvitationAsync(db, token, now, ct);
        if (invitation is null)
        {
            return Error.NotFound("invitation.invalid", "This invitation is invalid or has expired. Ask for a new one.").ToError();
        }

        tenantSetter.SetTenant(invitation.TenantId);
        var membership = await db.Memberships.FirstAsync(m => m.Id == invitation.MembershipId, ct);
        var user = await db.Users.FirstAsync(u => u.Id == membership.UserId, ct);

        if (await policy.ValidateAsync(r.Password, user.Email, ct) is { } weak)
        {
            return AuthErrors.WeakPassword(weak).ToError();
        }

        user.UpdateProfile(r.Name, user.Phone);
        user.SetPassword(hasher.HashPassword(user, r.Password), now);
        user.ConfirmEmail();
        membership.Activate(now);
        invitation.Accept(now);
        audit.Record("user.invitation_accepted", $"{user.Name} accepted their invitation", target: new AuditTarget("user", membership.Id.ToString(), user.Name),
            actor: new AuditActor(membership.Id, user.Name, user.Email));
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(invitation.TenantId, ct);
        return Results.NoContent();
    }

    internal static Task<TenantMembership?> FindStaffMembershipAsync(IdentityDbContext db, Guid userId, Guid? tenantId, CancellationToken ct)
    {
        var query = db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.UserId == userId && m.Kind == MembershipKind.Staff && m.Status == MembershipStatus.Active);
        if (tenantId is { } tid)
        {
            query = query.Where(m => m.TenantId == tid);
        }

        return query.OrderBy(m => m.CreatedAt).FirstOrDefaultAsync(ct);
    }
}

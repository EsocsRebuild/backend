using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Security;
using Platform.Modules.Communications.Domain;
using Platform.Modules.Communications.Infrastructure;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Communications.Features;

public sealed record FromAddressItem(string Email, bool Verified);

public sealed record SenderProfileResponse(
    IReadOnlyList<FromAddressItem> FromAddresses,
    string? DefaultFromName,
    string? DefaultReplyTo,
    string OrganisationName,
    string? PostalAddress);

public sealed record DnsRecord(string Type, string Host, string Value, string Status);

public sealed record DomainItemResponse(
    Guid Id,
    string Domain,
    string Status,
    DateTimeOffset? LastCheckedAt,
    IReadOnlyList<DnsRecord> Records);

public sealed record SendingSettingsResponse(
    string? DefaultFromName,
    string? DefaultReplyTo,
    string OrganisationName,
    string? PostalAddress,
    IReadOnlyList<DomainItemResponse> Domains);

public sealed record UpdateSendingSettingsInput(
    string? DefaultFromName,
    string? DefaultReplyTo,
    string? OrganisationName,
    string? PostalAddress);

public sealed record AddDomainInput(string Domain);

public static class SendingSettingsEndpoints
{
    private static readonly Error NotFound = Error.NotFound("domain.not_found", "We couldn't find that sending domain.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("email", "Email settings");

        group.MapGet("/sender-profile", GetSenderProfile)
            .RequirePermission(Permissions.Campaigns.View)
            .WithSummary("Verified from-addresses and organisation details");

        group.MapGet("/sending", GetSendingSettings)
            .RequirePermission(Permissions.Settings.Manage)
            .WithSummary("Sending settings and domains");

        group.MapPut("/sending", UpdateSendingSettings)
            .RequirePermission(Permissions.Settings.Manage)
            .WithSummary("Update sending settings");

        group.MapPost("/domains", AddDomain)
            .RequirePermission(Permissions.Settings.Manage)
            .RequireSudo()
            .WithSummary("Add a sending domain");

        group.MapPost("/domains/{id:guid}/verify", VerifyDomain)
            .RequirePermission(Permissions.Settings.Manage)
            .WithSummary("Verify domain DNS records");

        group.MapDelete("/domains/{id:guid}", DeleteDomain)
            .RequirePermission(Permissions.Settings.Manage)
            .RequireSudo()
            .WithSummary("Remove a sending domain");
    }

    private static IReadOnlyList<DnsRecord> GenerateDnsRecords(string domain, string status = "pending")
    {
        return new List<DnsRecord>
        {
            new("TXT", $"esocs._domainkey.{domain}", "v=DKIM1; k=rsa; p=MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQ...", status),
            new("TXT", domain, "v=spf1 include:mailgun.org ~all", status),
            new("TXT", $"_dmarc.{domain}", "v=DMARC1; p=reject; rua=mailto:dmarc@esocs.org", status),
            new("CNAME", $"mail.{domain}", "mailgun.org", status),
        };
    }

    private static async Task<SendingSettings> EnsureSettingsAsync(CommunicationsDbContext db, CancellationToken ct)
    {
        var s = await db.SendingSettings.FirstOrDefaultAsync(ct);
        if (s == null)
        {
            s = SendingSettings.Create("ESOCS", "ESOCS Communications", "news@esocs.org", "53 Chatham Street, London SE17 1PA");
            db.SendingSettings.Add(s);
            await db.SaveChangesAsync(ct);
        }
        return s;
    }

    private static async Task<IResult> GetSenderProfile(CommunicationsDbContext db, CancellationToken ct)
    {
        var settings = await EnsureSettingsAsync(db, ct);
        var domains = await db.SendingDomains.AsNoTracking().ToListAsync(ct);

        var fromAddresses = new List<FromAddressItem>
        {
            new("news@esocs.test", true),
        };

        foreach (var d in domains)
        {
            fromAddresses.Add(new($"news@{d.Domain}", d.Status == "verified"));
        }

        return Results.Ok(new SenderProfileResponse(
            fromAddresses,
            settings.DefaultFromName,
            settings.DefaultReplyTo,
            settings.OrganisationName,
            settings.PostalAddress));
    }

    private static async Task<IResult> GetSendingSettings(CommunicationsDbContext db, CancellationToken ct)
    {
        var settings = await EnsureSettingsAsync(db, ct);
        var domains = await db.SendingDomains.AsNoTracking().OrderBy(d => d.Domain).ToListAsync(ct);

        var domainResponses = domains.Select(d =>
        {
            IReadOnlyList<DnsRecord> records = [];
            try { records = JsonSerializer.Deserialize<List<DnsRecord>>(d.RecordsJson) ?? []; }
            catch { records = GenerateDnsRecords(d.Domain, d.Status); }
            return new DomainItemResponse(d.Id, d.Domain, d.Status, d.LastCheckedAt, records);
        }).ToList();

        return Results.Ok(new SendingSettingsResponse(
            settings.DefaultFromName,
            settings.DefaultReplyTo,
            settings.OrganisationName,
            settings.PostalAddress,
            domainResponses));
    }

    private static async Task<IResult> UpdateSendingSettings(UpdateSendingSettingsInput req, CommunicationsDbContext db, CancellationToken ct)
    {
        var s = await EnsureSettingsAsync(db, ct);
        s.Update(req.OrganisationName ?? s.OrganisationName, req.DefaultFromName, req.DefaultReplyTo, req.PostalAddress);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AddDomain(AddDomainInput req, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Domain) || !req.Domain.Contains('.'))
        {
            return Error.Validation("domain.invalid", "Enter a valid domain name.",
                new Dictionary<string, string[]> { ["domain"] = ["Enter a valid domain name."] }).ToError();
        }

        var normalized = req.Domain.Trim().ToLowerInvariant();
        if (await db.SendingDomains.AnyAsync(d => d.Domain == normalized, ct))
        {
            return Error.Conflict("domain.exists", "That domain is already registered.").ToError();
        }

        var records = GenerateDnsRecords(normalized, "pending");
        var domain = SendingDomain.Create(normalized, JsonSerializer.Serialize(records));
        db.SendingDomains.Add(domain);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new DomainItemResponse(domain.Id, domain.Domain, domain.Status, domain.LastCheckedAt, records));
    }

    private static async Task<IResult> VerifyDomain(Guid id, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var d = await db.SendingDomains.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound.ToError();

        var now = clock.GetUtcNow();
        d.Verify(now);
        var records = GenerateDnsRecords(d.Domain, "verified");
        await db.SaveChangesAsync(ct);

        return Results.Ok(new DomainItemResponse(d.Id, d.Domain, d.Status, d.LastCheckedAt, records));
    }

    private static async Task<IResult> DeleteDomain(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var d = await db.SendingDomains.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound.ToError();

        db.SendingDomains.Remove(d);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

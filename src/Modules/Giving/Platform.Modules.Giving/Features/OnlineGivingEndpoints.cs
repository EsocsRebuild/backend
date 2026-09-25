using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Giving.Domain;
using Platform.Modules.Giving.Infrastructure;
using Platform.Modules.Giving.Payments;
using Platform.Modules.People.Contracts;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;

namespace Platform.Modules.Giving.Features;

public sealed record CheckoutRequestDto(
    IReadOnlyList<AllocationDto> Allocations, string Email, string? FullName, string? Currency, Guid? CampaignId, string ReturnUrl,
    GivingChannel Channel = GivingChannel.Website);

public sealed record CheckoutResponse(Guid DonationId, string ReceiptNumber, string Provider, string Reference, string CheckoutUrl);

internal sealed class CheckoutValidator : AbstractValidator<CheckoutRequestDto>
{
    public CheckoutValidator()
    {
        RuleFor(x => x.Allocations).NotEmpty();
        RuleForEach(x => x.Allocations).ChildRules(a => a.RuleFor(x => x.Amount).GreaterThan(0).LessThan(100_000_000m));
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FullName).MaximumLength(200);
        RuleFor(x => x.ReturnUrl).NotEmpty().Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            .WithMessage("Must be an absolute http(s) URL.");
        RuleFor(x => x.Channel).Must(c => c is GivingChannel.Website or GivingChannel.MobileApp);
    }
}

/// <summary>
/// Online giving: the website/app starts a checkout (a Pending donation + provider session) and the
/// provider's signed webhook completes it. Webhooks are idempotent and validate amount and currency.
/// </summary>
public static partial class OnlineGivingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var pub = endpoints.MapPublicGroup("giving", "Public");
        pub.MapPost("/checkout", Checkout).WithValidation<CheckoutRequestDto>().WithSummary("Start an online gift; redirect the giver to checkoutUrl");
        pub.MapPost("/webhooks/{provider}", Webhook).WithSummary("Payment provider webhook (signature verified)").ExcludeFromDescription();

        var me = endpoints.MapGroup($"{EndpointExtensions.ApiPrefix}/me/giving").WithTags("My account").RequireAuthorization();
        me.MapGet("/", MyGiving).WithSummary("My giving history and annual statement");
    }

    private static async Task<IResult> Checkout(
        CheckoutRequestDto r, ICurrentUser user, IPeopleDirectory people, DonationRecorder recorder, GivingDbContext db,
        IServiceProvider services, IOptions<PaymentOptions> options, TimeProvider clock, ITenantContext tenant, CancellationToken ct)
    {
        if (tenant.TenantId is null)
        {
            return Error.Validation("tenant.required", "Send the X-Tenant header.").ToProblem();
        }

        Guid? personId = user.UserId is { } userId ? await people.FindPersonIdByUserAsync(userId, ct) : null;
        var request = new RecordDonationRequest(personId, r.FullName, r.Email, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            PaymentMethod.Online, r.Allocations, r.Currency, r.Channel, CampaignId: r.CampaignId);

        var publicFunds = r.Allocations.Select(a => a.FundId).ToList();
        if (await db.Funds.CountAsync(f => publicFunds.Contains(f.Id) && f.IsPublic && f.IsActive, ct) != publicFunds.Distinct().Count())
        {
            return Error.Validation("donation.invalid_fund", "One or more funds are not available for online giving.").ToProblem();
        }

        var recorded = await recorder.RecordAsync(request, DonationStatus.Pending, ct);
        if (recorded.IsFailure)
        {
            return recorded.Error.ToProblem();
        }

        var donation = recorded.Value;
        var gateway = services.GetRequiredKeyedService<IPaymentGateway>(options.Value.DefaultProvider);
        var session = await gateway.CreateCheckoutAsync(new CheckoutRequest(
            donation.Id.ToString("N"), donation.Total.Amount, donation.Total.Currency, r.Email, r.FullName, r.ReturnUrl), ct);

        donation.AttachProvider(session.Provider, session.ProviderReference);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new CheckoutResponse(donation.Id, donation.ReceiptNumber, session.Provider, session.ProviderReference, session.CheckoutUrl));
    }

    private static async Task<IResult> Webhook(
        string provider, HttpRequest request, IServiceProvider services, GivingDbContext db, ITenantContextSetter tenantSetter,
        TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        var logger = loggers.CreateLogger("Giving.Webhooks");
        var gateway = services.GetKeyedService<IPaymentGateway>(provider);
        if (gateway is null)
        {
            return Results.NotFound();
        }

        var notification = await gateway.ParseWebhookAsync(request, ct);
        if (notification is null)
        {
            LogRejected(logger, provider);
            return Results.Unauthorized();
        }

        // Webhooks carry no tenant: find the donation globally, then bind the tenant for the write.
        var donation = await db.Donations.IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(d => d.Provider == gateway.Name && d.ProviderReference == notification.ProviderReference, ct);
        if (donation is null)
        {
            LogUnknownReference(logger, provider, notification.ProviderReference);
            return Results.Ok(); // Acknowledge so the provider stops retrying; nothing to do.
        }

        tenantSetter.SetTenant(donation.TenantId);

        if (donation.Status is DonationStatus.Completed or DonationStatus.Refunded)
        {
            return Results.Ok(); // Idempotent re-delivery.
        }

        var matches = notification.Amount == donation.Total.Amount &&
                      string.Equals(notification.Currency, donation.Total.Currency, StringComparison.OrdinalIgnoreCase);

        if (notification.Succeeded && matches)
        {
            donation.Complete(clock.GetUtcNow(), notification.ProviderReference);
        }
        else
        {
            donation.Fail(matches ? notification.FailureReason ?? "Payment failed." : "Amount or currency mismatch.");
            if (!matches)
            {
                LogMismatch(logger, donation.Id, notification.Amount, notification.Currency);
            }
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> MyGiving(int? year, ICurrentUser user, IPeopleDirectory people, GivingDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await people.FindPersonIdByUserAsync(user.RequiredUserId, ct) is not { } personId)
        {
            return Error.NotFound("person.no_profile", "No member profile is linked to your account yet.").ToProblem();
        }

        return Results.Ok(await GivingManagementEndpoints.BuildStatementAsync(db, personId, null, year ?? clock.GetUtcNow().Year, ct));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected {Provider} webhook: invalid signature")]
    private static partial void LogRejected(ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} webhook for unknown reference {Reference}")]
    private static partial void LogUnknownReference(ILogger logger, string provider, string reference);

    [LoggerMessage(Level = LogLevel.Error, Message = "Donation {DonationId} payment mismatch: got {Amount} {Currency}")]
    private static partial void LogMismatch(ILogger logger, Guid donationId, decimal amount, string currency);
}

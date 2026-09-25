using Platform.SharedKernel.Domain;

namespace Platform.Modules.Giving.Contracts;

/// <summary>A gift was completed (recorded in person or confirmed by the payment provider). Drives thank-you messages.</summary>
public sealed record DonationCompletedIntegrationEvent(
    Guid TenantId, Guid DonationId, string ReceiptNumber, Guid? PersonId, string? DonorEmail, string? DonorName,
    decimal Amount, string Currency, DateOnly ReceivedOn, string Channel) : IntegrationEvent(TenantId);

public sealed record DonationRefundedIntegrationEvent(Guid TenantId, Guid DonationId, decimal Amount, string Currency, string Reason)
    : IntegrationEvent(TenantId);

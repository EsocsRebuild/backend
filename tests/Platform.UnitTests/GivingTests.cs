using Platform.Modules.Giving.Contracts;
using Platform.Modules.Giving.Domain;
using Platform.SharedKernel.Domain;

namespace Platform.UnitTests;

public class GivingTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Tithe = Guid.CreateVersion7();
    private static readonly Guid Building = Guid.CreateVersion7();

    [Fact]
    public void Total_is_the_exact_sum_of_allocations()
    {
        var donation = Record([(Tithe, 10_000m), (Building, 4_500.505m)]);

        Assert.Equal(14_500.51m, donation.Total.Amount); // each allocation rounds half away from zero to 2dp
        Assert.Equal("NGN", donation.Total.Currency);
        Assert.Equal(donation.Total.Amount, donation.Allocations.Sum(a => a.Amount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Allocations_must_be_positive(decimal amount) =>
        Assert.Throws<DomainException>(() => Record([(Tithe, amount)]));

    [Fact]
    public void A_fund_may_appear_only_once() =>
        Assert.Throws<DomainException>(() => Record([(Tithe, 1m), (Tithe, 2m)]));

    [Fact]
    public void Completing_raises_an_integration_event_once()
    {
        var donation = Record([(Tithe, 100m)], DonationStatus.Pending);
        donation.Complete(DateTimeOffset.UtcNow, "ref");
        donation.Complete(DateTimeOffset.UtcNow, "ref");

        var evt = Assert.Single(donation.DomainEvents.OfType<DonationCompletedIntegrationEvent>());
        Assert.Equal(Tenant, evt.TenantId);
        Assert.Equal(100m, evt.Amount);
    }

    [Fact]
    public void Closed_batches_lock_their_donations()
    {
        var batch = DonationBatch.Open("Sunday", new DateOnly(2026, 9, 27), "NGN", null, null, 100m);
        var donation = Record([(Tithe, 100m)]);
        batch.Close([donation], DateTimeOffset.UtcNow, null);

        Assert.Throws<DomainException>(() => donation.Void("mistake"));
        batch.Reopen([donation]);
        donation.Void("mistake");
        Assert.Equal(DonationStatus.Voided, donation.Status);
    }

    [Fact]
    public void Only_completed_gifts_can_be_refunded()
    {
        var pending = Record([(Tithe, 50m)], DonationStatus.Pending);
        Assert.Throws<DomainException>(() => pending.Refund(DateTimeOffset.UtcNow, "x"));
    }

    [Fact]
    public void Money_rejects_mixed_currencies_and_bad_codes()
    {
        Assert.Throws<DomainException>(() => Money.Of(1, "NGN").Add(Money.Of(1, "USD")));
        Assert.Throws<DomainException>(() => Money.Of(1, "NAIRA"));
        Assert.Throws<DomainException>(() => Money.Of(-1, "NGN"));
        Assert.Equal(3.5m, Money.Of(1.25m, "ngn").Add(Money.Of(2.25m, "NGN")).Amount);
    }

    private static Donation Record(IReadOnlyCollection<(Guid, decimal)> allocations, DonationStatus status = DonationStatus.Completed) =>
        Donation.Record(Tenant, "R2026-000001", new DonorInfo(null, "Ada", "ada@example.com"),
            new GiftDetails(new DateOnly(2026, 9, 27), PaymentMethod.Cash, GivingChannel.InPerson, "NGN", null, null, null, null, null),
            allocations, status, DateTimeOffset.UtcNow);
}

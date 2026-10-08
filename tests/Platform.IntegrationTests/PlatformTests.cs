using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Platform.IntegrationTests;

[Collection(PlatformCollection.Name)]
public class TenantIsolationTests(PlatformFactory factory)
{
    [Fact]
    public async Task One_church_can_never_see_or_change_another_churchs_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var grace = await factory.LoginAsync(PlatformFactory.OwnerEmail, PlatformFactory.OwnerPassword);

        // Get a parish under Grace for member creation.
        var parishesRes = await grace.GetFromJsonAsync<JsonElement>("/api/v1/lookups/parishes", ct);
        var parishId = parishesRes.GetProperty("data").EnumerateArray().First().GetProperty("id").GetGuid();

        // Grace records a member.
        var created = await grace.PostAsJsonAsync("/api/v1/members",
            new { firstName = "Secret", lastName = "Member", parishId, status = "active", email = "secret@grace.test", emailConsent = false }, ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var personId = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").GetProperty("id").GetGuid();

        // The platform operator (seeded owner is a platform admin) onboards a second church.
        var onboard = await grace.PostAsJsonAsync("/api/v1/platform/tenants",
            new { slug = "hope", name = "Hope Chapel", ownerEmail = "owner@hope.test", ownerFirstName = "Hope", ownerLastName = "Owner", currency = "NGN" }, ct);
        Assert.Equal(HttpStatusCode.Created, onboard.StatusCode);

        // Wait for Identity to provision the owner via the outbox, then accept the invitation.
        await PlatformFactory.EventuallyAsync(async () =>
        {
            try { await factory.SetPasswordAsync("owner@hope.test", "HopeChapel!2026"); return (bool?)true; }
            catch (InvalidOperationException) { return null; }
        });
        var hope = await factory.LoginAsync("owner@hope.test", "HopeChapel!2026");

        var list = await hope.GetFromJsonAsync<JsonElement>("/api/v1/members?q=Secret", ct);
        Assert.Equal(0, list.GetProperty("data").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await hope.GetAsync($"/api/v1/members/{personId}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hope.DeleteAsync($"/api/v1/members/{personId}", ct)).StatusCode);

        // Hope's own default funds exist (Giving reacted to TenantCreated) and are not Grace's.
        var hopeFunds = await PlatformFactory.EventuallyAsync(async () =>
        {
            var funds = await hope.GetFromJsonAsync<JsonElement>("/api/v1/giving/funds", ct);
            var data = funds.GetProperty("data");
            return data.GetArrayLength() > 0 ? (JsonElement?)data : null;
        });
        var graceFunds = (await grace.GetFromJsonAsync<JsonElement>("/api/v1/giving/funds", ct)).GetProperty("data");
        var graceIds = graceFunds.EnumerateArray().Select(f => f.GetProperty("id").GetGuid()).ToHashSet();
        Assert.DoesNotContain(hopeFunds.EnumerateArray(), f => graceIds.Contains(f.GetProperty("id").GetGuid()));

        // Grace's owner is not a member of Hope: switching tenant is refused.
        var sw = await grace.PostAsJsonAsync("/api/v1/auth/switch-tenant", new { tenantId = Guid.NewGuid() }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, sw.StatusCode);
    }
}

[Collection(PlatformCollection.Name)]
public class AuthenticationTests(PlatformFactory factory)
{
    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = PlatformFactory.OwnerEmail, password = PlatformFactory.OwnerPassword, remember = true }, ct);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>(ct);
        var first = body.GetProperty("data").GetProperty("tokens").GetProperty("refreshToken").GetString();

        var rotated = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first }, ct);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = (await rotated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").GetProperty("refreshToken").GetString();

        // An attacker replays the stolen first token…
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first }, ct)).StatusCode);

        // …so the legitimate, newer token is dead too: the device must sign in again.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = second }, ct)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected_and_errors_are_problem_details()
    {
        var ct = TestContext.Current.CancellationToken;
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/members", ct)).StatusCode);

        var bad = await anon.PostAsJsonAsync("/api/v1/auth/login", new { email = "not-an-email", password = "" }, ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        var problem = await bad.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("VALIDATION", problem.GetProperty("error").GetProperty("code").GetString());
    }
}

[Collection(PlatformCollection.Name)]
public class EventCapacityTests(PlatformFactory factory)
{
    [Fact]
    public async Task Concurrent_registrations_never_oversell_capacity()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await factory.LoginAsync(PlatformFactory.OwnerEmail, PlatformFactory.OwnerPassword);
        var start = DateTimeOffset.UtcNow.AddDays(3);
        var created = await admin.PostAsJsonAsync("/api/v1/events", new
        {
            title = "Couples Dinner",
            type = "Social",
            visibility = "Public",
            startsAt = start,
            endsAt = start.AddHours(3),
            timeZone = "Africa/Lagos",
            registrationEnabled = true,
            capacity = 5,
        }, ct);
        var eventId = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").GetProperty("id").GetGuid();
        await admin.PostAsync($"/api/v1/events/{eventId}/publish", null, ct);
        var occurrences = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/events/{eventId}/occurrences", ct)).GetProperty("data");
        var occurrenceId = occurrences[0].GetProperty("id").GetGuid();

        var anon = factory.CreateClient();
        anon.DefaultRequestHeaders.Add("X-Tenant", "grace");
        var attempts = Enumerable.Range(0, 20).Select(i => anon.PostAsJsonAsync(
            $"/api/v1/public/events/occurrences/{occurrenceId}/register", new { fullName = $"Guest {i}", email = $"guest{i}@test.dev" }, ct));
        var responses = await Task.WhenAll(attempts);
        var statuses = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").GetProperty("status").GetString()));

        Assert.Equal(5, statuses.Count(s => s == "Confirmed"));
        Assert.Equal(15, statuses.Count(s => s == "Waitlisted"));
    }
}

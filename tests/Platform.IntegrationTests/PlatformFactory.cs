using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;

namespace Platform.IntegrationTests;

/// <summary>
/// Boots the real API against a fresh, uniquely named PostgreSQL database (dropped afterwards).
/// Set TEST_DB_CONNECTION to point at your server; the database name is generated.
/// </summary>
public sealed class PlatformFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string OwnerEmail = "owner@grace.test";
    public const string OwnerPassword = "Integration!2026";

    private readonly string _server = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
        ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    private readonly string _database = $"platform_test_{Guid.NewGuid():N}";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string ConnectionString => new NpgsqlConnectionStringBuilder(_server) { Database = _database }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", ConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", "");
        builder.UseSetting("Auth:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Auth:RefreshReuseGracePeriod", "00:00:00");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Outbox:PollingInterval", "00:00:00.200");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Seed:TenantSlug", "grace");
        builder.UseSetting("Seed:TenantName", "Grace Test Church");
        builder.UseSetting("Seed:OwnerEmail", OwnerEmail);
        builder.UseSetting("Seed:OwnerPassword", OwnerPassword);
        builder.UseSetting("Seed:Currency", "NGN");
        builder.UseSetting("Seed:TimeZone", "Africa/Lagos");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }

    public async ValueTask InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_server);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
        await cmd.ExecuteNonQueryAsync();
        _ = Server; // start the host: migrations + seed
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_server);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<HttpClient> LoginAsync(string email, string password, string? tenant = null, string clientType = "Admin")
    {
        var client = CreateClient();
        if (tenant is not null)
        {
            client.DefaultRequestHeaders.Add("X-Tenant", tenant);
        }

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, clientType });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("tokens").GetProperty("accessToken").GetString();

        var authed = CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return authed;
    }

    /// <summary>Sets a password for an invited owner (stands in for clicking the emailed invitation link).</summary>
    public async Task SetPasswordAsync(string email, string password)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = await db.Users.FirstAsync(u => u.Email == email);
        user.SetPassword(hasher.HashPassword(user, password), DateTimeOffset.UtcNow);
        var invited = await db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == user.Id && m.Status == MembershipStatus.Invited).ToListAsync();
        invited.ForEach(m => m.Activate(DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    public static async Task<T> EventuallyAsync<T>(Func<Task<T?>> probe, TimeSpan? timeout = null) where T : struct
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time (outbox not delivered?).");
            }

            await Task.Delay(200);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PlatformCollection : ICollectionFixture<PlatformFactory>
{
    public const string Name = "platform";
}

using Microsoft.AspNetCore.Http;
using Platform.Modules.People.Features;
using Platform.Modules.Tenancy.Features;
using Platform.Web.Middleware;

namespace Platform.UnitTests;

public class WebMiddlewareAndLookupTests
{
    [Theory]
    [InlineData("/api/v1/api/auth/login", "/api/v1/auth/login")]
    [InlineData("/api/v1/api/ranks", "/api/v1/ranks")]
    [InlineData("/api/v1/api/hierarchy", "/api/v1/hierarchy")]
    [InlineData("/api/ranks", "/api/v1/ranks")]
    [InlineData("/api/hierarchy", "/api/v1/hierarchy")]
    [InlineData("/api/auth/login", "/api/v1/auth/login")]
    [InlineData("/api/v1/members", "/api/v1/members")]
    [InlineData("/api", "/api")]
    [InlineData("/api/v1", "/api/v1")]
    [InlineData("/docs", "/docs")]
    public async Task Route_normalization_rewrites_double_and_unversioned_paths(string inputPath, string expectedPath)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = inputPath;

        var middleware = new RouteNormalizationMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.Equal(expectedPath, context.Request.Path.Value);
    }

    [Theory]
    [InlineData("Bearer undefined")]
    [InlineData("Bearer null")]
    [InlineData("Bearer ")]
    [InlineData("Bearer       ")]
    [InlineData("Bearer invalid-token-without-dots")]
    public async Task Malformed_token_sanitizer_removes_invalid_bearer_tokens(string headerValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = headerValue;

        var middleware = new MalformedTokenSanitizerMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey("Authorization"));
    }

    [Theory]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature")]
    [InlineData("Bearer abc.def.ghi")]
    public async Task Malformed_token_sanitizer_preserves_valid_jwt_format(string headerValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = headerValue;

        var middleware = new MalformedTokenSanitizerMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.True(context.Request.Headers.ContainsKey("Authorization"));
        Assert.Equal(headerValue, context.Request.Headers.Authorization.ToString());
    }

    [Fact]
    public void Canonical_ranks_catalogue_contains_expected_holy_orders()
    {
        // 12 Male + 10 Female orders = 22 total
        var maleRanks = typeof(RanksEndpoints)
            .GetField("DefaultRanks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as IReadOnlyList<EcclesiasticalRankResponse>;

        Assert.NotNull(maleRanks);
        Assert.Equal(22, maleRanks.Count);
        Assert.Equal(12, maleRanks.Count(r => r.GenderEligibility == "male"));
        Assert.Equal(10, maleRanks.Count(r => r.GenderEligibility == "female"));

        Assert.Contains(maleRanks, r => r.Name == "Senior Apostle (Yellow)" && r.ShortCode == "SAP");
        Assert.Contains(maleRanks, r => r.Name == "Mother in Israel" && r.ShortCode == "MII");
        Assert.Contains(maleRanks, r => r.Name == "Apostle General (Green)" && r.ShortCode == "APG");
    }

    [Fact]
    public void Canonical_hierarchy_contains_key_ecclesiastical_provinces()
    {
        var provinces = typeof(HierarchyEndpoints)
            .GetField("DefaultHierarchy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as IReadOnlyList<ProvinceHierarchyResponse>;

        Assert.NotNull(provinces);
        Assert.True(provinces.Count >= 6);
        Assert.Contains(provinces, p => p.Name.Contains("Lagos Central") && p.ShortCode == "LCP");
        Assert.Contains(provinces, p => p.Name.Contains("Rivers") && p.ShortCode == "RVP");
        Assert.Contains(provinces, p => p.Name.Contains("United Kingdom") && p.ShortCode == "UKE");
    }
}


using Platform.Application.Security;
using Platform.Modules.Communications.Delivery;
using Platform.Modules.Identity.Domain;
using Platform.Modules.People.Domain;
using MembershipStatus = Platform.Modules.People.Domain.MembershipStatus;
using Platform.SharedKernel.Domain;

namespace Platform.UnitTests;

public class IdentityAndSharedTests
{
    [Fact]
    public void Every_system_role_uses_only_known_permissions()
    {
        foreach (var (name, (_, permissions)) in SystemRoles.Defaults)
        {
            Assert.All(permissions, p => Assert.True(Permissions.IsKnown(p), $"{name}: {p}"));
        }

        Assert.Equal(Permissions.All.Count, SystemRoles.Defaults[SystemRoles.Owner].Permissions.Count);
    }

    [Fact]
    public void Permission_keys_follow_module_resource_action_convention() =>
        Assert.All(Permissions.All, p => Assert.Matches("^[a-z]+\\.[a-z]+\\.[a-z-]+$", p));

    [Fact]
    public void Roles_reject_unknown_permissions() =>
        Assert.Throws<DomainException>(() => Role.Create("Custom", null, ["people.people.read", "root.everything"]));

    [Fact]
    public void Lockout_after_max_failed_attempts_then_reset_on_success()
    {
        var user = User.Create("a@b.c", "A", "B");
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(now, maxAttempts: 5, TimeSpan.FromMinutes(15));
        }

        Assert.True(user.IsLockedOut(now));
        Assert.False(user.IsLockedOut(now.AddMinutes(16)));
        user.RegisterSuccessfulLogin(now.AddMinutes(16));
        Assert.Null(user.LockoutEndsAt);
    }

    [Fact]
    public void Changing_password_rotates_the_security_stamp()
    {
        var user = User.Create("a@b.c", "A", "B");
        var stamp = user.SecurityStamp;
        user.SetPassword("hash", DateTimeOffset.UtcNow);

        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Theory]
    [InlineData("Youth & Teens Ministry", "youth-teens-ministry")]
    [InlineData("  Église Saint-Étienne  ", "eglise-saint-etienne")]
    [InlineData("2026 — Harvest!!", "2026-harvest")]
    public void Slugs_are_url_safe(string input, string expected)
    {
        Assert.Equal(expected, Slug.From(input));
        Assert.True(Slug.IsValid(expected));
    }

    [Fact]
    public void Membership_status_changes_are_recorded_in_history()
    {
        var person = Person.Create(Guid.CreateVersion7(), "M-000001", "Ada", "Okafor", MembershipStatus.Visitor, new DateOnly(2026, 1, 1));
        person.ChangeMembershipStatus(MembershipStatus.Member, new DateOnly(2026, 6, 1), "Completed membership class");

        var change = Assert.Single(person.StatusHistory);
        Assert.Equal(MembershipStatus.Visitor, change.From);
        Assert.Equal(new DateOnly(2026, 6, 1), person.MembershipDate);
    }

    [Fact]
    public void Entities_cannot_move_between_tenants()
    {
        var person = Person.Create(Guid.CreateVersion7(), "M-1", "A", "B", MembershipStatus.Visitor, new DateOnly(2026, 1, 1));
        Assert.Throws<DomainException>(() => person.AssignTenant(Guid.CreateVersion7()));
    }

    [Fact]
    public void Template_rendering_html_encodes_values_for_email()
    {
        var html = TemplateRenderer.Render("<p>Hi {{firstName}}</p>", new Dictionary<string, string> { ["firstName"] = "<script>x</script>" }, htmlEncode: true);
        Assert.Equal("<p>Hi &lt;script&gt;x&lt;/script&gt;</p>", html);
    }
}

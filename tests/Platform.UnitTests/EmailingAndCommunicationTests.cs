using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Platform.Application.Abstractions;
using Platform.Application.Emails;
using Platform.Infrastructure.Email;

namespace Platform.UnitTests;

public class EmailingAndCommunicationTests
{
    [Fact]
    public void Layout_renderer_produces_accessible_responsive_church_email_markup()
    {
        var model = new ChurchEmailLayoutModel(
            Subject: "Welcome to Mount Zion",
            Preheader: "Your portal credentials have been provisioned.",
            ChurchName: "ESOCS Mount Zion",
            HeaderBadge: "Official Notice",
            RecipientName: "Sister Deborah Adebayo",
            Title: "Welcome to the Digital Platform",
            HtmlContent: "<p>We are delighted to welcome you into active ministry administration.</p>",
            PrimaryButton: new EmailButton("Access Dashboard", "https://admin.esocs.org/dashboard"),
            Details: [new EmailKeyValue("Role Assigned", "Youth Director"), new EmailKeyValue("Province", "London")],
            VerificationCode: "849201",
            UnsubscribeUrl: "https://esocs.org/unsubscribe/test-token");

        var html = ChurchEmailLayoutRenderer.Render(model);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("ESOCS MOUNT ZION", html);
        Assert.Contains("Sister Deborah", html);
        Assert.Contains("849201", html);
        Assert.Contains("Access Dashboard", html);
        Assert.Contains("https://admin.esocs.org/dashboard", html);
        Assert.Contains("Youth Director", html);
        Assert.Contains("https://esocs.org/unsubscribe/test-token", html);
    }

    [Fact]
    public void Verification_code_email_contains_formatted_code_and_instructions()
    {
        var msg = ChurchEmailLayoutRenderer.BuildVerificationCodeMessage(
            to: "deborah@example.com",
            name: "Deborah",
            code: "554433",
            churchName: "ESOCS Cathedral");

        Assert.Equal("deborah@example.com", msg.To);
        Assert.Contains("554433", msg.Subject);
        Assert.Contains("554433", msg.HtmlBody);
        Assert.Contains("554433", msg.TextBody);
        Assert.Contains("ESOCS Cathedral", msg.HtmlBody);
    }

    [Fact]
    public void Invitation_message_renders_branded_call_to_action()
    {
        var msg = ChurchEmailLayoutRenderer.BuildInvitationMessage(
            to: "elder.samuel@example.com",
            name: "Elder Samuel",
            churchName: "ESOCS Central Parish",
            inviterName: "Senior Apostle John",
            roleName: "Finance Administrator",
            inviteUrl: "https://admin.esocs.org/invite/secret-token-123",
            expiresDays: 7);

        Assert.Equal("elder.samuel@example.com", msg.To);
        Assert.Contains("ESOCS Central Parish", msg.Subject);
        Assert.Contains("Senior Apostle John", msg.HtmlBody);
        Assert.Contains("Finance Administrator", msg.HtmlBody);
        Assert.Contains("https://admin.esocs.org/invite/secret-token-123", msg.HtmlBody);
    }

    [Fact]
    public void Password_reset_message_generates_security_action_link()
    {
        var msg = ChurchEmailLayoutRenderer.BuildPasswordResetMessage(
            to: "member@example.com",
            name: "Michael",
            churchName: "ESOCS",
            resetUrl: "https://admin.esocs.org/reset-password?token=xyz987",
            expiresMinutes: 30);

        Assert.Equal("member@example.com", msg.To);
        Assert.Equal("Reset your password", msg.Subject);
        Assert.Contains("https://admin.esocs.org/reset-password?token=xyz987", msg.HtmlBody);
        Assert.Contains("30 minutes", msg.HtmlBody);
    }

    [Fact]
    public void Donation_receipt_email_renders_currency_and_official_reference()
    {
        var date = new DateTimeOffset(2026, 10, 7, 18, 0, 0, TimeSpan.Zero);
        var msg = ChurchEmailLayoutRenderer.BuildDonationReceiptMessage(
            to: "donor@example.com",
            donorName: "Brother Emmanuel",
            churchName: "ESOCS Mount Zion",
            fundName: "Building Fund",
            amount: 50000m,
            currency: "NGN",
            reference: "REC-20261007-0042",
            date: date);

        Assert.Equal("donor@example.com", msg.To);
        Assert.Contains("NGN 50,000.00", msg.Subject);
        Assert.Contains("REC-20261007-0042", msg.HtmlBody);
        Assert.Contains("Building Fund", msg.HtmlBody);
        Assert.Contains("Brother Emmanuel", msg.HtmlBody);
    }

    [Fact]
    public async Task Resilient_sender_retries_transient_failures_and_succeeds()
    {
        var attempts = 0;
        var fakeSender = new FlakySender(() =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new InvalidOperationException("Simulated network timeout");
            }
        });

        var options = Options.Create(new EmailOptions
        {
            MaxRetries = 3,
            InitialRetryDelayMs = 10
        });

        var resilient = new ResilientEmailSender(fakeSender, options, NullLogger<ResilientEmailSender>.Instance);
        var msg = new EmailMessage("test@example.com", "Test", "<p>Hello</p>");

        await resilient.SendAsync(msg, CancellationToken.None);

        Assert.Equal(2, attempts);
    }

    private sealed class FlakySender(Action onSend) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            onSend();
            return Task.CompletedTask;
        }
    }
}


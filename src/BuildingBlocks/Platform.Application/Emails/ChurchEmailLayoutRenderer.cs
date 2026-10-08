using System.Net;
using System.Text;
using Platform.Application.Abstractions;

namespace Platform.Application.Emails;

public sealed record EmailButton(string Text, string Url, string? Variant = null);
public sealed record EmailKeyValue(string Label, string Value);

public sealed record ChurchEmailLayoutModel(
    string Subject,
    string? Preheader = null,
    string? ChurchName = null,
    string? LogoUrl = null,
    string? HeaderBadge = null,
    string? RecipientName = null,
    string? Title = null,
    string? HtmlContent = null,
    string? VerificationCode = null,
    IReadOnlyList<EmailKeyValue>? Details = null,
    EmailButton? PrimaryButton = null,
    EmailButton? SecondaryButton = null,
    string? Note = null,
    string? UnsubscribeUrl = null,
    string? PreferencesUrl = null,
    string? PostalAddress = null,
    string? SupportEmail = null);

/// <summary>
/// Professional, responsive HTML email renderer styled for the Church / ESOCS design system.
/// Compatible across Apple Mail, Gmail, Outlook, Yahoo, and mobile email clients.
/// </summary>
public static class ChurchEmailLayoutRenderer
{
    public const string DefaultChurchName = "Eternal Sacred Order of the Cherubim and Seraphim";
    public const string DefaultPostalAddress = "ESOCS Mount Zion Cathedral, International Headquarters";
    private const string PrimaryColor = "#1e3a8a"; // Ecclesiastical Deep Navy
    private const string AccentGold = "#d97706";   // Liturgical Gold
    private const string BackgroundColor = "#f8fafc";
    private const string CardBackground = "#ffffff";
    private const string TextColor = "#1e293b";
    private const string MutedTextColor = "#64748b";
    private const string BorderColor = "#e2e8f0";

    public static string Render(ChurchEmailLayoutModel model)
    {
        var church = string.IsNullOrWhiteSpace(model.ChurchName) ? DefaultChurchName : model.ChurchName;
        var address = string.IsNullOrWhiteSpace(model.PostalAddress) ? DefaultPostalAddress : model.PostalAddress;
        var year = DateTime.UtcNow.Year;

        var sb = new StringBuilder(4096);

        sb.Append($$"""
<!DOCTYPE html>
<html lang="en" xmlns="http://www.w3.org/1999/xhtml" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="X-UA-Compatible" content="IE=edge">
    <meta name="x-apple-disable-message-reformatting">
    <title>{{WebUtility.HtmlEncode(model.Subject)}}</title>
    <!--[if mso]>
    <noscript>
        <xml>
            <o:OfficeDocumentSettings>
                <o:PixelsPerInch>96</o:PixelsPerInch>
            </o:OfficeDocumentSettings>
        </xml>
    </noscript>
    <![endif]-->
    <style>
        body, table, td, a { -webkit-text-size-adjust: 100%; -ms-text-size-adjust: 100%; }
        table, td { mso-table-lspace: 0pt; mso-table-rspace: 0pt; }
        img { -ms-interpolation-mode: bicubic; border: 0; height: auto; line-height: 100%; outline: none; text-decoration: none; }
        body { height: 100% !important; margin: 0 !important; padding: 0 !important; width: 100% !important; background-color: {{BackgroundColor}}; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }
        @media screen and (max-width: 600px) {
            .email-container { width: 100% !important; margin: auto !important; }
            .fluid { max-width: 100% !important; height: auto !important; margin-left: auto !important; margin-right: auto !important; }
            .stack-column { display: block !important; width: 100% !important; max-width: 100% !important; direction: ltr !important; }
            .mobile-p-20 { padding: 20px !important; }
            .mobile-code { font-size: 28px !important; letter-spacing: 6px !important; }
        }
    </style>
</head>
<body style="margin: 0; padding: 0; background-color: {{BackgroundColor}}; color: {{TextColor}};">
""");

        // Preheader
        if (!string.IsNullOrWhiteSpace(model.Preheader))
        {
            sb.Append($$"""
    <div style="display: none; font-size: 1px; line-height: 1px; max-height: 0px; max-width: 0px; opacity: 0; overflow: hidden; mso-hide: all;">
        {{WebUtility.HtmlEncode(model.Preheader)}}
        &#847; &zwnj; &nbsp; &#8199; &shy; &#847; &zwnj; &nbsp; &#8199; &shy; &#847; &zwnj; &nbsp; &#8199; &shy;
    </div>
""");
        }

        sb.Append($$"""
    <table border="0" cellpadding="0" cellspacing="0" width="100%" style="table-layout: fixed;">
        <tr>
            <td align="center" style="padding: 24px 12px 36px 12px; background-color: {{BackgroundColor}};">
                <!--[if (gte mso 9)|(IE)]>
                <table align="center" border="0" cellspacing="0" cellpadding="0" width="600">
                <tr>
                <td align="center" valign="top" width="600">
                <![endif]-->
                <table class="email-container" border="0" cellpadding="0" cellspacing="0" width="100%" style="max-width: 600px; margin: 0 auto;">
                    
                    <!-- Header Branding -->
                    <tr>
                        <td align="center" style="padding: 0 0 20px 0;">
                            <table border="0" cellpadding="0" cellspacing="0">
                                <tr>
                                    <td align="center">
                                        <div style="font-size: 13px; font-weight: 700; letter-spacing: 1.5px; text-transform: uppercase; color: {{AccentGold}}; margin-bottom: 4px;">
                                            &#10014; {{WebUtility.HtmlEncode(church.ToUpperInvariant())}}
                                        </div>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Main Card Container -->
                    <tr>
                        <td style="background-color: {{CardBackground}}; border-radius: 12px; border: 1px solid {{BorderColor}}; overflow: hidden; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05), 0 2px 4px -1px rgba(0, 0, 0, 0.03);">
                            
                            <!-- Top Gold Accent Bar -->
                            <table border="0" cellpadding="0" cellspacing="0" width="100%">
                                <tr>
                                    <td height="4" style="background: linear-gradient(90deg, {{PrimaryColor}} 0%, {{AccentGold}} 50%, {{PrimaryColor}} 100%); font-size: 0; line-height: 0;">&nbsp;</td>
                                </tr>
                            </table>

                            <!-- Card Body -->
                            <table border="0" cellpadding="0" cellspacing="0" width="100%" style="padding: 36px 36px 28px 36px;" class="mobile-p-20">
""");

        // Optional Header Badge
        if (!string.IsNullOrWhiteSpace(model.HeaderBadge))
        {
            sb.Append($$"""
                                <tr>
                                    <td style="padding-bottom: 12px;">
                                        <span style="display: inline-block; padding: 4px 10px; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.8px; border-radius: 9999px; background-color: #eff6ff; color: {{PrimaryColor}}; border: 1px solid #bfdbfe;">
                                            {{WebUtility.HtmlEncode(model.HeaderBadge)}}
                                        </span>
                                    </td>
                                </tr>
""");
        }

        // Title
        if (!string.IsNullOrWhiteSpace(model.Title))
        {
            sb.Append($$"""
                                <tr>
                                    <td style="padding-bottom: 16px;">
                                        <h1 style="margin: 0; font-size: 22px; font-weight: 700; line-height: 1.3; color: {{TextColor}};">
                                            {{WebUtility.HtmlEncode(model.Title)}}
                                        </h1>
                                    </td>
                                </tr>
""");
        }

        // Greeting
        if (!string.IsNullOrWhiteSpace(model.RecipientName))
        {
            sb.Append($$"""
                                <tr>
                                    <td style="font-size: 15px; line-height: 1.6; color: {{TextColor}}; padding-bottom: 14px;">
                                        Dear {{WebUtility.HtmlEncode(model.RecipientName.Trim())}},
                                    </td>
                                </tr>
""");
        }

        // Main HTML Content
        if (!string.IsNullOrWhiteSpace(model.HtmlContent))
        {
            sb.Append($$"""
                                <tr>
                                    <td style="font-size: 15px; line-height: 1.65; color: {{TextColor}}; padding-bottom: 20px;">
                                        {{model.HtmlContent}}
                                    </td>
                                </tr>
""");
        }

        // Verification / OTP Code Box
        if (!string.IsNullOrWhiteSpace(model.VerificationCode))
        {
            sb.Append($$"""
                                <tr>
                                    <td align="center" style="padding: 12px 0 24px 0;">
                                        <div style="background-color: #f1f5f9; border: 2px dashed #cbd5e1; border-radius: 8px; padding: 18px 24px; text-align: center; display: inline-block; min-width: 220px;">
                                            <div style="font-size: 11px; font-weight: 700; letter-spacing: 1px; text-transform: uppercase; color: {{MutedTextColor}}; margin-bottom: 6px;">Verification Code</div>
                                            <div class="mobile-code" style="font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace; font-size: 32px; font-weight: 800; letter-spacing: 8px; color: {{PrimaryColor}}; margin-left: 8px;">
                                                {{WebUtility.HtmlEncode(model.VerificationCode)}}
                                            </div>
                                            <div style="font-size: 11px; color: {{MutedTextColor}}; margin-top: 6px;">Valid for 15 minutes</div>
                                        </div>
                                    </td>
                                </tr>
""");
        }

        // Key-Value Details Grid
        if (model.Details is { Count: > 0 })
        {
            sb.Append($$"""
                                <tr>
                                    <td style="padding: 12px 0 24px 0;">
                                        <table border="0" cellpadding="0" cellspacing="0" width="100%" style="background-color: #f8fafc; border: 1px solid {{BorderColor}}; border-radius: 8px; padding: 16px;">
""");
            foreach (var kv in model.Details)
            {
                sb.Append($$"""
                                            <tr>
                                                <td width="40%" style="padding: 6px 12px; font-size: 13px; color: {{MutedTextColor}}; font-weight: 600;">{{WebUtility.HtmlEncode(kv.Label)}}</td>
                                                <td width="60%" align="right" style="padding: 6px 12px; font-size: 14px; color: {{TextColor}}; font-weight: 700;">{{WebUtility.HtmlEncode(kv.Value)}}</td>
                                            </tr>
""");
            }
            sb.Append("""
                                        </table>
                                    </td>
                                </tr>
""");
        }

        // Action Buttons
        if (model.PrimaryButton is not null || model.SecondaryButton is not null)
        {
            sb.Append("""
                                <tr>
                                    <td align="center" style="padding: 16px 0 24px 0;">
                                        <table border="0" cellpadding="0" cellspacing="0">
                                            <tr>
""");
            if (model.PrimaryButton is not null)
            {
                sb.Append($$"""
                                                <td align="center" style="border-radius: 8px; background-color: {{PrimaryColor}};">
                                                    <a href="{{WebUtility.HtmlEncode(model.PrimaryButton.Url)}}" target="_blank" style="display: inline-block; padding: 14px 28px; font-size: 15px; font-weight: 700; color: #ffffff; text-decoration: none; border-radius: 8px; border: 1px solid {{PrimaryColor}};">
                                                        {{WebUtility.HtmlEncode(model.PrimaryButton.Text)}}
                                                    </a>
                                                </td>
""");
            }

            if (model.SecondaryButton is not null)
            {
                sb.Append($$"""
                                                <td align="center" style="padding-left: 12px;">
                                                    <a href="{{WebUtility.HtmlEncode(model.SecondaryButton.Url)}}" target="_blank" style="display: inline-block; padding: 14px 20px; font-size: 14px; font-weight: 600; color: {{PrimaryColor}}; text-decoration: none; border-radius: 8px; border: 1px solid {{BorderColor}}; background-color: #ffffff;">
                                                        {{WebUtility.HtmlEncode(model.SecondaryButton.Text)}}
                                                    </a>
                                                </td>
""");
            }

            sb.Append("""
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
""");
        }

        // Note / Disclaimer
        if (!string.IsNullOrWhiteSpace(model.Note))
        {
            sb.Append($$"""
                                <tr>
                                    <td style="font-size: 13px; line-height: 1.5; color: {{MutedTextColor}}; padding-top: 12px; border-top: 1px solid {{BorderColor}};">
                                        {{WebUtility.HtmlEncode(model.Note)}}
                                    </td>
                                </tr>
""");
        }

        sb.Append($$"""
                            </table>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td align="center" style="padding: 24px 16px 0 16px; font-size: 12px; line-height: 1.6; color: {{MutedTextColor}};">
                            <p style="margin: 0 0 6px 0; font-weight: 600; color: {{TextColor}};">
                                &copy; {{year}} {{WebUtility.HtmlEncode(church)}}. All rights reserved.
                            </p>
                            <p style="margin: 0 0 10px 0;">
                                {{WebUtility.HtmlEncode(address)}}
                            </p>
""");

        if (!string.IsNullOrWhiteSpace(model.UnsubscribeUrl) || !string.IsNullOrWhiteSpace(model.PreferencesUrl))
        {
            sb.Append("""
                            <p style="margin: 0 0 6px 0;">
""");
            if (!string.IsNullOrWhiteSpace(model.UnsubscribeUrl))
            {
                sb.Append($"<a href=\"{WebUtility.HtmlEncode(model.UnsubscribeUrl)}\" style=\"color: {PrimaryColor}; text-decoration: underline;\">Unsubscribe</a>");
            }
            if (!string.IsNullOrWhiteSpace(model.UnsubscribeUrl) && !string.IsNullOrWhiteSpace(model.PreferencesUrl))
            {
                sb.Append(" &bull; ");
            }
            if (!string.IsNullOrWhiteSpace(model.PreferencesUrl))
            {
                sb.Append($"<a href=\"{WebUtility.HtmlEncode(model.PreferencesUrl)}\" style=\"color: {PrimaryColor}; text-decoration: underline;\">Manage Preferences</a>");
            }
            sb.Append("""
                            </p>
""");
        }

        sb.Append("""
                        </td>
                    </tr>

                </table>
                <!--[if (gte mso 9)|(IE)]>
                </td>
                </tr>
                </table>
                <![endif]-->
            </td>
        </tr>
    </table>
</body>
</html>
""");

        return sb.ToString();
    }

    public static EmailMessage BuildVerificationCodeMessage(string to, string? name, string code, string churchName = DefaultChurchName, string? supportEmail = null)
    {
        var subject = $"{code} is your verification code";
        var html = Render(new ChurchEmailLayoutModel(
            Subject: subject,
            Preheader: $"Your one-time verification code is {code}.",
            ChurchName: churchName,
            HeaderBadge: "Security Verification",
            RecipientName: name,
            Title: "Verify Your Email Address",
            HtmlContent: "<p>Please use the verification code below to verify your email address and continue accessing your account. For security purposes, never share this code with anyone.</p>",
            VerificationCode: code,
            Note: "If you did not request this verification code, please ignore this email or contact security if you suspect unauthorized activity.",
            SupportEmail: supportEmail));

        var text = $"Hello {(name ?? "there")},\n\nYour verification code is: {code}\n\nValid for 15 minutes.\n\n--\n{churchName}";
        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage BuildInvitationMessage(string to, string? name, string churchName, string inviterName, string roleName, string inviteUrl, int expiresDays = 7)
    {
        var subject = $"You're invited to join {churchName}";
        var html = Render(new ChurchEmailLayoutModel(
            Subject: subject,
            Preheader: $"{inviterName} has invited you to join {churchName} as {roleName}.",
            ChurchName: churchName,
            HeaderBadge: "Portal Invitation",
            RecipientName: name,
            Title: "Welcome to the Administration Portal",
            HtmlContent: $"<p><strong>{WebUtility.HtmlEncode(inviterName)}</strong> has invited you to join the digital administration workspace of <strong>{WebUtility.HtmlEncode(churchName)}</strong> with the role of <strong>{WebUtility.HtmlEncode(roleName)}</strong>.</p><p>To activate your account and configure your secure credentials, please click the button below. This invitation link is valid for {expiresDays} days.</p>",
            PrimaryButton: new EmailButton("Accept Invitation & Activate Account", inviteUrl),
            Note: $"If the button above does not work, copy and paste this link into your browser:\n{inviteUrl}",
            PostalAddress: DefaultPostalAddress));

        var text = $"Hello {(name ?? "there")},\n\n{inviterName} has invited you to join {churchName} as {roleName}.\n\nAccept your invitation here:\n{inviteUrl}\n\nThis link expires in {expiresDays} days.\n\n--\n{churchName}";
        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage BuildPasswordResetMessage(string to, string? name, string churchName, string resetUrl, int expiresMinutes = 30)
    {
        var subject = "Reset your password";
        var html = Render(new ChurchEmailLayoutModel(
            Subject: subject,
            Preheader: "Follow this link to reset your administrator password.",
            ChurchName: churchName,
            HeaderBadge: "Security Alert",
            RecipientName: name,
            Title: "Password Reset Request",
            HtmlContent: $"<p>We received a request to reset the password for your account. Click the button below to choose a new, secure password. This secure link expires in {expiresMinutes} minutes.</p>",
            PrimaryButton: new EmailButton("Reset Password", resetUrl),
            Note: "If you did not request a password reset, you can safely ignore this email. Your password will remain unchanged.",
            PostalAddress: DefaultPostalAddress));

        var text = $"Hello {(name ?? "there")},\n\nWe received a request to reset your password. Use the link below:\n{resetUrl}\n\nExpires in {expiresMinutes} minutes.\n\nIf you did not request this, you can ignore this email.\n\n--\n{churchName}";
        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage BuildDonationReceiptMessage(string to, string donorName, string churchName, string fundName, decimal amount, string currency, string reference, DateTimeOffset date, string? taxReceiptUrl = null)
    {
        var formattedAmount = $"{currency.ToUpperInvariant()} {amount:N2}";
        var subject = $"Receipt for your donation to {churchName} - {formattedAmount}";

        var details = new List<EmailKeyValue>
        {
            new("Donation Reference", reference),
            new("Fund / Cause", fundName),
            new("Date & Time", date.ToString("MMM dd, yyyy HH:mm 'UTC'")),
            new("Amount Contributed", formattedAmount),
            new("Payment Status", "Confirmed / Cleared")
        };

        var html = Render(new ChurchEmailLayoutModel(
            Subject: subject,
            Preheader: $"Thank you for your generous contribution of {formattedAmount} to {fundName}.",
            ChurchName: churchName,
            HeaderBadge: "Official Giving Receipt",
            RecipientName: donorName,
            Title: "Thank You for Your Generosity",
            HtmlContent: $"<p>Thank you for supporting the ministry work of <strong>{WebUtility.HtmlEncode(churchName)}</strong>. Your financial partnership empowers the gospel, community outreach, and kingdom initiatives.</p><p>This email serves as your official receipt for tax and personal records.</p>",
            Details: details,
            PrimaryButton: taxReceiptUrl is not null ? new EmailButton("Download Tax Statement", taxReceiptUrl) : null,
            Note: "Eternal Sacred Order of the Cherubim and Seraphim is a registered religious and charitable organization.",
            PostalAddress: DefaultPostalAddress));

        var text = $"Dear {donorName},\n\nThank you for your donation of {formattedAmount} to {fundName} (Ref: {reference}) on {date:d}.\n\nMay God bless your giving!\n\n--\n{churchName}";
        return new EmailMessage(to, subject, html, text);
    }
}

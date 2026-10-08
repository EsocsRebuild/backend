using System.Net;
using Platform.Application.Abstractions;
using Platform.Application.Emails;

namespace Platform.Modules.Identity.Services;

/// <summary>
/// Transactional emails for administrator and user accounts. Branded, responsive HTML with plain-text fallback.
/// </summary>
internal static class EmailTemplates
{
    public static EmailMessage Invitation(string to, string? name, string organisation, string invitedBy, string roleName, string url) =>
        ChurchEmailLayoutRenderer.BuildInvitationMessage(to, name, organisation, invitedBy, roleName, url);

    public static EmailMessage AddedToOrganisation(string to, string name, string organisation, string roleName, string url)
    {
        var subject = $"You now have access to {organisation}";
        var html = ChurchEmailLayoutRenderer.Render(new ChurchEmailLayoutModel(
            Subject: subject,
            Preheader: $"Your account has been granted access to {organisation}.",
            ChurchName: organisation,
            HeaderBadge: "Account Access",
            RecipientName: name,
            Title: "Access Granted",
            HtmlContent: $"<p>Your account has been assigned access to the <strong>{WebUtility.HtmlEncode(organisation)}</strong> administrative portal with the role of <strong>{WebUtility.HtmlEncode(roleName)}</strong>.</p><p>You can sign in immediately using your registered email and credentials.</p>",
            PrimaryButton: new EmailButton("Sign In to Portal", url),
            PostalAddress: ChurchEmailLayoutRenderer.DefaultPostalAddress));

        var text = $"Hi {name},\n\nYour account now has access to {organisation} as {roleName}.\n\nSign in here:\n{url}";
        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage PasswordReset(string to, string name, string url) =>
        ChurchEmailLayoutRenderer.BuildPasswordResetMessage(to, name, ChurchEmailLayoutRenderer.DefaultChurchName, url);

    public static EmailMessage PasswordChanged(string to, string name)
    {
        var subject = "Your password was changed";
        var html = ChurchEmailLayoutRenderer.Render(new ChurchEmailLayoutModel(
            Subject: subject,
            Preheader: "Security notice regarding your recent password update.",
            ChurchName: ChurchEmailLayoutRenderer.DefaultChurchName,
            HeaderBadge: "Security Notice",
            RecipientName: name,
            Title: "Password Updated",
            HtmlContent: "<p>The password for your account was recently changed, and any existing active sessions on other devices were signed out.</p><p>If you made this change, no further action is necessary. <strong>If you did not make this change</strong>, please reset your password immediately and notify your church systems administrator.</p>",
            PostalAddress: ChurchEmailLayoutRenderer.DefaultPostalAddress));

        var text = $"Hi {name},\n\nThe password for your account was changed. If you did not make this change, please reset your password immediately and contact your administrator.";
        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage VerificationCode(string to, string name, string code) =>
        ChurchEmailLayoutRenderer.BuildVerificationCodeMessage(to, name, code, ChurchEmailLayoutRenderer.DefaultChurchName);

    public static EmailMessage AlreadyRegistered(string to, string organisation, string url)
    {
        var subject = $"About your request for {organisation}";
        var html = ChurchEmailLayoutRenderer.Render(new ChurchEmailLayoutModel(
            Subject: subject,
            Preheader: "Notice regarding your portal access request.",
            ChurchName: organisation,
            HeaderBadge: "Account Notice",
            Title: "Account Already Exists",
            HtmlContent: $"<p>An access request was submitted using this email address for <strong>{WebUtility.HtmlEncode(organisation)}</strong>, but an active account is already registered.</p><p>You can sign in directly or reset your password if you have forgotten your credentials.</p>",
            PrimaryButton: new EmailButton("Sign In", url),
            PostalAddress: ChurchEmailLayoutRenderer.DefaultPostalAddress));

        var text = $"Hello,\n\nAn access request was received for {organisation}, but you already have an account. Sign in at {url}";
        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage AccessApproved(string to, string name, string organisation, string roleName, string url)
    {
        var subject = $"Your access to {organisation} was approved";
        var html = ChurchEmailLayoutRenderer.Render(new ChurchEmailLayoutModel(
            Subject: subject,
            Preheader: $"Your access request for {organisation} has been approved.",
            ChurchName: organisation,
            HeaderBadge: "Access Approved",
            RecipientName: name,
            Title: "Welcome to the Team",
            HtmlContent: $"<p>Your access request for <strong>{WebUtility.HtmlEncode(organisation)}</strong> has been reviewed and approved. You are now assigned the role of <strong>{WebUtility.HtmlEncode(roleName)}</strong>.</p><p>Click below to sign in and begin.</p>",
            PrimaryButton: new EmailButton("Sign In to Portal", url),
            PostalAddress: ChurchEmailLayoutRenderer.DefaultPostalAddress));

        var text = $"Hi {name},\n\nYour access to {organisation} was approved as {roleName}. Sign in here:\n{url}";
        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage AccessRejected(string to, string name, string organisation, string? reason)
    {
        var subject = $"About your request for {organisation}";
        var reasonHtml = string.IsNullOrWhiteSpace(reason)
            ? string.Empty
            : $"<p style=\"background-color:#fef2f2;border-left:4px solid #ef4444;padding:12px;margin:12px 0;\"><strong>Reason:</strong> {WebUtility.HtmlEncode(reason)}</p>";

        var html = ChurchEmailLayoutRenderer.Render(new ChurchEmailLayoutModel(
            Subject: subject,
            Preheader: $"Update regarding your access request for {organisation}.",
            ChurchName: organisation,
            HeaderBadge: "Access Request Update",
            RecipientName: name,
            Title: "Access Request Update",
            HtmlContent: $"<p>Your request for access to <strong>{WebUtility.HtmlEncode(organisation)}</strong> was reviewed and could not be approved at this time.</p>{reasonHtml}<p>If you believe this decision is in error, please contact your unit or provincial administrator.</p>",
            PostalAddress: ChurchEmailLayoutRenderer.DefaultPostalAddress));

        var text = $"Hi {name},\n\nYour request for access to {organisation} was not approved.{(string.IsNullOrWhiteSpace(reason) ? "" : $"\nReason: {reason}")}\n\nIf you have questions, please contact your administrator.";
        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage NewDeviceSignIn(string to, string name, string device, string? ip)
    {
        var subject = "New sign-in to your account";
        var details = new List<EmailKeyValue>
        {
            new("Device / Client", device),
            new("Time", DateTimeOffset.UtcNow.ToString("MMM dd, yyyy HH:mm 'UTC'"))
        };
        if (!string.IsNullOrWhiteSpace(ip))
        {
            details.Add(new("IP Address", ip));
        }

        var html = ChurchEmailLayoutRenderer.Render(new ChurchEmailLayoutModel(
            Subject: subject,
            Preheader: $"New sign-in detected on {device}.",
            ChurchName: ChurchEmailLayoutRenderer.DefaultChurchName,
            HeaderBadge: "Security Alert",
            RecipientName: name,
            Title: "New Sign-in Detected",
            HtmlContent: "<p>We detected a new sign-in to your administrator account. Please verify that this activity was initiated by you.</p>",
            Details: details,
            Note: "If this wasn't you, your account may be compromised. Please change your password immediately and contact your system security administrator.",
            PostalAddress: ChurchEmailLayoutRenderer.DefaultPostalAddress));

        var text = $"Hi {name},\n\nA new sign-in was detected on your account ({device}, IP: {ip ?? "unknown"}). If this wasn't you, reset your password immediately.";
        return new EmailMessage(to, subject, html, text);
    }
}

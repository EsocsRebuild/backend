using System.Net;
using Platform.Application.Abstractions;

namespace Platform.Modules.Identity.Services;

/// <summary>Transactional emails for administrator accounts. Plain, accessible HTML with a text alternative.</summary>
internal static class EmailTemplates
{
    public static EmailMessage Invitation(string to, string? name, string organisation, string invitedBy, string roleName, string url) => Build(
        to, name, $"You’re invited to the {organisation} admin portal",
        $"{invitedBy} has invited you to help run {organisation} as {Article(roleName)} {roleName}. Choose a password to accept. This link works for 7 days.",
        "Accept invitation", url);

    public static EmailMessage AddedToOrganisation(string to, string name, string organisation, string roleName, string url) => Build(
        to, name, $"You now have access to {organisation}",
        $"Your account has been given access to the {organisation} admin portal as {Article(roleName)} {roleName}.", "Sign in", url);

    public static EmailMessage PasswordReset(string to, string name, string url) => Build(
        to, name, "Reset your password",
        "We received a request to reset your password. This link works once and expires in 30 minutes. If you didn’t ask for this, you can ignore this email — your password won’t change.",
        "Choose a new password", url);

    public static EmailMessage PasswordChanged(string to, string name) => Build(
        to, name, "Your password was changed",
        "The password for your account was just changed and your other devices were signed out. If this wasn’t you, reset your password straight away and contact your administrator.",
        null, null);

    public static EmailMessage VerificationCode(string to, string name, string code) => Build(
        to, name, $"{code} is your verification code",
        $"Enter this code to verify your email address: <strong style=\"font-size:22px;letter-spacing:4px\">{code}</strong><br>It expires in 15 minutes. If you didn’t request it, ignore this email.",
        null, null, rawHtmlBody: true);

    public static EmailMessage AlreadyRegistered(string to, string organisation, string url) => Build(
        to, null, $"About your request for {organisation}",
        "Someone (hopefully you) asked for access with this email address, but you already have an account. Sign in instead, or reset your password if you’ve forgotten it.",
        "Sign in", url);

    public static EmailMessage AccessApproved(string to, string name, string organisation, string roleName, string url) => Build(
        to, name, $"Your access to {organisation} was approved",
        $"Good news — your request was approved. You can now sign in as {Article(roleName)} {roleName}.", "Sign in", url);

    public static EmailMessage AccessRejected(string to, string name, string organisation, string? reason) => Build(
        to, name, $"About your request for {organisation}",
        $"Your request for access wasn’t approved.{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Reason given: {WebUtility.HtmlEncode(reason)}")} If you think this is a mistake, contact your administrator.",
        null, null, rawHtmlBody: true);

    public static EmailMessage NewDeviceSignIn(string to, string name, string device, string? ip) => Build(
        to, name, "New sign-in to your account",
        $"Your account was just used to sign in on {WebUtility.HtmlEncode(device)}{(ip is null ? string.Empty : $" ({WebUtility.HtmlEncode(ip)})")}. If this wasn’t you, change your password now.",
        null, null, rawHtmlBody: true);

    private static string Article(string word) => "aeiouAEIOU".Contains(word[0], StringComparison.Ordinal) ? "an" : "a";

    private static EmailMessage Build(string to, string? name, string subject, string body, string? action, string? url, bool rawHtmlBody = false)
    {
        var greeting = string.IsNullOrWhiteSpace(name) ? "Hello," : $"Hi {WebUtility.HtmlEncode(name.Split(' ')[0])},";
        var htmlBody = rawHtmlBody ? body : WebUtility.HtmlEncode(body);
        var button = action is null || url is null
            ? string.Empty
            : $"""
               <p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;padding:12px 20px;background:#2f4fb4;color:#ffffff;border-radius:6px;text-decoration:none;font-weight:600">{WebUtility.HtmlEncode(action)}</a></p>
               <p style="color:#6b7280;font-size:12px">If the button doesn’t work, copy this link into your browser:<br>{WebUtility.HtmlEncode(url)}</p>
               """;
        var html = $"""
            <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;font-size:15px;line-height:1.55;color:#111827;max-width:560px">
            <p>{greeting}</p>
            <p>{htmlBody}</p>
            {button}
            </div>
            """;
        var text = $"{(string.IsNullOrWhiteSpace(name) ? "Hello," : $"Hi {name.Split(' ')[0]},")}\n\n{System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", string.Empty)}\n\n{(url is null ? string.Empty : $"{action}: {url}")}";
        return new EmailMessage(to, subject, html, WebUtility.HtmlDecode(text));
    }
}

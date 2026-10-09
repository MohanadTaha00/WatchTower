using System.Net;
using System.Text;

namespace WatchTower.Application.Notifications;

public static class TwoFactorEmailComposer
{
    public static EmailContent ComposeLoginCode(string code, int lifetimeMinutes) => Compose(
        subject: $"[WatchTower] Your sign-in code: {code}",
        headline: "Your WatchTower sign-in code",
        intro: "Someone (hopefully you) entered your password on WatchTower. Enter this code to finish signing in:",
        code: code,
        lifetimeMinutes: lifetimeMinutes,
        warning: "If this wasn't you, someone knows your password. Change it from the Account page as soon as possible.");

    public static EmailContent ComposeSetupCode(string code, int lifetimeMinutes) => Compose(
        subject: $"[WatchTower] Confirm two-factor sign-in: {code}",
        headline: "Turn on email sign-in codes",
        intro: "Enter this code on the Account page to confirm you receive WatchTower sign-in codes at this address:",
        code: code,
        lifetimeMinutes: lifetimeMinutes,
        warning: "If you didn't ask for this, you can ignore this email. Nothing changes until the code is entered.");

    private static EmailContent Compose(string subject, string headline, string intro, string code, int lifetimeMinutes, string warning)
    {
        var validFor = IncidentEmailComposer.FormatDuration(lifetimeMinutes * 60);

        var text = new StringBuilder()
            .AppendLine(headline).AppendLine()
            .AppendLine(intro).AppendLine()
            .AppendLine($"    {code}").AppendLine()
            .AppendLine($"The code expires in {validFor} and works once.").AppendLine()
            .AppendLine(warning)
            .ToString();

        static string E(string value) => WebUtility.HtmlEncode(value);

        var html = new StringBuilder();
        html.Append("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f4f6f9;font-family:Segoe UI,Roboto,Arial,sans-serif;color:#1c2430;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" style=\"max-width:600px;margin:0 auto;background:#fff;border:1px solid #e2e7ee;border-radius:10px;border-collapse:separate;\">");
        html.Append($"<tr><td style=\"padding:18px 24px;background:#2f6fed;color:#fff;border-radius:10px 10px 0 0;font-size:18px;font-weight:700;\">{E(headline)}</td></tr>");
        html.Append("<tr><td style=\"padding:20px 24px;font-size:14px;line-height:1.5;\">");
        html.Append($"<p style=\"margin:0 0 16px;\">{E(intro)}</p>");
        html.Append($"<p style=\"margin:0 0 16px;font-size:30px;font-weight:700;letter-spacing:6px;font-family:Consolas,Menlo,monospace;\">{E(code)}</p>");
        html.Append($"<p style=\"margin:0 0 16px;\">The code expires in {E(validFor)} and works once.</p>");
        html.Append($"<p style=\"margin:0;color:#6b7686;\">{E(warning)}</p>");
        html.Append("</td></tr></table></body></html>");

        return new EmailContent(subject, text, html.ToString());
    }
}

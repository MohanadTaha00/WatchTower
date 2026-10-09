using System.Net;
using System.Text;

namespace WatchTower.Application.Notifications;

public static class PasswordResetEmailComposer
{
    public static EmailContent Compose(string resetLink, int lifetimeMinutes)
    {
        var validFor = IncidentEmailComposer.FormatDuration(lifetimeMinutes * 60);
        const string ignore = "If you didn't ask to reset your password, you can ignore this email. Your password stays the same.";

        var text = new StringBuilder()
            .AppendLine("Reset your WatchTower password").AppendLine()
            .AppendLine("Someone (hopefully you) asked to reset the password for this WatchTower account.")
            .AppendLine($"Open this link to choose a new password. It works once and expires in {validFor}:").AppendLine()
            .AppendLine(resetLink).AppendLine()
            .AppendLine(ignore)
            .ToString();

        static string E(string value) => WebUtility.HtmlEncode(value);

        var html = new StringBuilder();
        html.Append("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f4f6f9;font-family:Segoe UI,Roboto,Arial,sans-serif;color:#1c2430;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" style=\"max-width:600px;margin:0 auto;background:#fff;border:1px solid #e2e7ee;border-radius:10px;border-collapse:separate;\">");
        html.Append("<tr><td style=\"padding:18px 24px;background:#2f6fed;color:#fff;border-radius:10px 10px 0 0;font-size:18px;font-weight:700;\">Reset your WatchTower password</td></tr>");
        html.Append("<tr><td style=\"padding:20px 24px;font-size:14px;line-height:1.5;\">");
        html.Append("<p style=\"margin:0 0 16px;\">Someone (hopefully you) asked to reset the password for this WatchTower account.</p>");
        html.Append($"<p style=\"margin:0 0 20px;\">Click the button to choose a new password. The link works once and expires in {E(validFor)}.</p>");
        html.Append($"<a href=\"{E(resetLink)}\" style=\"display:inline-block;padding:10px 16px;background:#2f6fed;color:#fff;text-decoration:none;border-radius:6px;font-weight:600;\">Choose a new password</a>");
        html.Append($"<p style=\"margin:20px 0 0;color:#6b7686;font-size:12px;word-break:break-all;\">Or paste this link into your browser: {E(resetLink)}</p>");
        html.Append($"<p style=\"margin:16px 0 0;color:#6b7686;\">{E(ignore)}</p>");
        html.Append("</td></tr></table></body></html>");

        return new EmailContent("[WatchTower] Reset your password", text, html.ToString());
    }
}

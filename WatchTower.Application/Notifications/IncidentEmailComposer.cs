using System.Globalization;
using System.Net;
using System.Text;
using WatchTower.Domain.Entities;

namespace WatchTower.Application.Notifications;

public record EmailContent(string Subject, string TextBody, string HtmlBody);

public static class IncidentEmailComposer
{
    private const string DownColor = "#d64545";
    private const string UpColor = "#1f9d55";

    public static EmailContent ComposeOpened(UptimeMonitor monitor, Incident incident, CheckResult check, string dashboardUrl)
    {
        var explanation = IncidentExplainer.Explain(monitor, check);
        var details = Details(monitor, check, ("Detected at", FormatTime(incident.StartedAt)));
        if (!string.IsNullOrWhiteSpace(check.ErrorMessage))
            details.Add(("Technical error", check.ErrorMessage));
        var next = $"WatchTower keeps checking every {FormatDuration(monitor.IntervalSeconds)}. " +
                   "You will get another email as soon as it is back up.";

        return Build(
            subject: $"[WatchTower] DOWN: {monitor.Name}",
            color: DownColor,
            headline: $"\"{monitor.Name}\" is down",
            sections: [("What happened", explanation), ("What happens next", next)],
            details: details,
            dashboardUrl: dashboardUrl);
    }

    public static EmailContent ComposeResolved(UptimeMonitor monitor, Incident incident, CheckResult check, string dashboardUrl)
    {
        var resolvedAt = incident.ResolvedAt ?? check.CheckedAt;
        var duration = FormatDuration((resolvedAt - incident.StartedAt).TotalSeconds);
        var summary = $"The outage lasted {duration}, from {FormatTime(incident.StartedAt)} to {FormatTime(resolvedAt)}.";
        var details = Details(monitor, check, ("Recovered at", FormatTime(resolvedAt)));

        return Build(
            subject: $"[WatchTower] RESOLVED: {monitor.Name} is back up",
            color: UpColor,
            headline: $"\"{monitor.Name}\" is back up",
            sections: [("Summary", summary), ("What went wrong", incident.Summary)],
            details: details,
            dashboardUrl: dashboardUrl);
    }

    public static string FormatDuration(double totalSeconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(totalSeconds)));
        var parts = new List<string>();
        if (span.Days > 0) parts.Add(Plural(span.Days, "day"));
        if (span.Hours > 0) parts.Add(Plural(span.Hours, "hour"));
        if (span.Minutes > 0 && span.Days == 0) parts.Add(Plural(span.Minutes, "minute"));
        if (span.Seconds > 0 && span.TotalHours < 1) parts.Add(Plural(span.Seconds, "second"));
        return parts.Count == 0 ? "0 seconds" : string.Join(" ", parts);
    }

    private static List<(string Label, string Value)> Details(UptimeMonitor monitor, CheckResult check, (string, string) when)
    {
        var details = new List<(string, string)>
        {
            ("Monitor", $"{monitor.Name} ({TypeLabel(monitor.Type)})"),
            ("Target", IncidentExplainer.Describe(monitor)),
            when
        };
        if (check.StatusCode > 0)
            details.Add(("HTTP status", check.StatusCode.ToString(CultureInfo.InvariantCulture)));
        details.Add(("Response time", $"{check.ResponseTimeMs} ms"));
        return details;
    }

    private static EmailContent Build(
        string subject,
        string color,
        string headline,
        IReadOnlyList<(string Title, string Text)> sections,
        IReadOnlyList<(string Label, string Value)> details,
        string dashboardUrl)
    {
        var link = dashboardUrl.TrimEnd('/') + "/#/incidents";

        var text = new StringBuilder();
        text.AppendLine(headline).AppendLine();
        foreach (var (title, body) in sections)
            text.AppendLine(title).AppendLine(body).AppendLine();
        text.AppendLine("Details");
        var width = details.Max(d => d.Label.Length) + 2;
        foreach (var (label, value) in details)
            text.Append("  ").Append((label + ":").PadRight(width)).AppendLine(value);
        text.AppendLine().Append("View the incident report: ").AppendLine(link);

        static string E(string value) => WebUtility.HtmlEncode(value);

        var html = new StringBuilder();
        html.Append("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f4f6f9;font-family:Segoe UI,Roboto,Arial,sans-serif;color:#1c2430;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" style=\"max-width:600px;margin:0 auto;background:#fff;border:1px solid #e2e7ee;border-radius:10px;border-collapse:separate;\">");
        html.Append($"<tr><td style=\"padding:18px 24px;background:{color};color:#fff;border-radius:10px 10px 0 0;font-size:18px;font-weight:700;\">{E(headline)}</td></tr>");
        html.Append("<tr><td style=\"padding:20px 24px;font-size:14px;line-height:1.5;\">");
        foreach (var (title, body) in sections)
        {
            html.Append($"<h3 style=\"margin:0 0 6px;font-size:15px;\">{E(title)}</h3>");
            html.Append($"<p style=\"margin:0 0 16px;\">{E(body)}</p>");
        }
        html.Append("<h3 style=\"margin:0 0 6px;font-size:15px;\">Details</h3>");
        html.Append("<table role=\"presentation\" style=\"border-collapse:collapse;width:100%;margin-bottom:20px;\">");
        foreach (var (label, value) in details)
        {
            html.Append("<tr>");
            html.Append($"<td style=\"padding:6px 12px 6px 0;color:#6b7686;white-space:nowrap;vertical-align:top;border-bottom:1px solid #eef1f5;\">{E(label)}</td>");
            html.Append($"<td style=\"padding:6px 0;word-break:break-word;border-bottom:1px solid #eef1f5;\">{E(value)}</td>");
            html.Append("</tr>");
        }
        html.Append("</table>");
        html.Append($"<a href=\"{E(link)}\" style=\"display:inline-block;padding:10px 16px;background:#2f6fed;color:#fff;text-decoration:none;border-radius:6px;font-weight:600;\">View incident report</a>");
        html.Append("</td></tr></table>");
        html.Append("<p style=\"text-align:center;color:#6b7686;font-size:12px;\">You are receiving this because you monitor this endpoint with WatchTower.</p>");
        html.Append("</body></html>");

        return new EmailContent(subject, text.ToString(), html.ToString());
    }

    private static string TypeLabel(MonitorType type) => type switch
    {
        MonitorType.IpAddress => "IP address",
        MonitorType.Api => "API endpoint",
        _ => "Website"
    };

    private static string FormatTime(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    private static string Plural(int count, string word) => count == 1 ? $"1 {word}" : $"{count} {word}s";
}

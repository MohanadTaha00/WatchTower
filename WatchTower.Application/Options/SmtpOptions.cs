namespace WatchTower.Application.Options;

public class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>SMTP server host. Email notifications are skipped when this is empty.</summary>
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;

    /// <summary>One of Auto, None, StartTls, StartTlsWhenAvailable, SslOnConnect.</summary>
    public string Security { get; set; } = "Auto";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "watchtower@localhost";
    public string FromName { get; set; } = "WatchTower";
    public int TimeoutSeconds { get; set; } = 30;
}

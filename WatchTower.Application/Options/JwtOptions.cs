namespace WatchTower.Application.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Key { get; set; }
    public string Issuer { get; set; } = "WatchTower";
    public string Audience { get; set; } = "WatchTower";
    public int ExpiryMinutes { get; set; } = 60;
}

using System.Security.Cryptography;
using System.Text;

namespace WatchTower.Infrastructure.Services;

internal static class SecureTokens
{
    /// <summary>256-bit random token as lowercase hex, safe to put in URLs.</summary>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool HashMatches(string value, string? expectedHash) =>
        expectedHash is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(value)), Encoding.ASCII.GetBytes(expectedHash));
}

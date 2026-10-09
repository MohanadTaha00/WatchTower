using System.Text;
using WatchTower.Infrastructure.Services;

namespace WatchTower.Tests;

public class TotpTests
{
    private static readonly byte[] RfcKey = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void Matches_rfc6238_test_vectors(long unixSeconds, string expected)
    {
        var step = Totp.CurrentStep(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expected, Totp.ComputeCode(RfcKey, step));
    }

    [Fact]
    public void Accepts_adjacent_steps_but_not_reused_ones()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.CurrentStep(now);

        Assert.Equal(step - 1, Totp.Verify(RfcKey, Totp.ComputeCode(RfcKey, step - 1), now, null));
        Assert.Equal(step + 1, Totp.Verify(RfcKey, Totp.ComputeCode(RfcKey, step + 1), now, null));
        Assert.Null(Totp.Verify(RfcKey, Totp.ComputeCode(RfcKey, step + 2), now, null));
        Assert.Null(Totp.Verify(RfcKey, Totp.ComputeCode(RfcKey, step), now, lastUsedStep: step));
    }

    [Fact]
    public void Base32_round_trips()
    {
        var key = Totp.GenerateKey();

        var encoded = Totp.ToBase32(key);

        Assert.Equal(32, encoded.Length);
        Assert.Equal(key, Totp.FromBase32(encoded.ToLowerInvariant()));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.ToBase32(RfcKey));
    }
}

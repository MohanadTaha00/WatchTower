using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WatchTower.Application.DTOs;
using WatchTower.Infrastructure.Persistence;
using WatchTower.Infrastructure.Services;

namespace WatchTower.Tests;

public partial class TwoFactorApiTests : IClassFixture<WatchTowerFactory>
{
    private const string Password = "password123";

    private readonly WatchTowerFactory _factory;

    public TwoFactorApiTests(WatchTowerFactory factory)
    {
        _factory = factory;
        _factory.Emails.FailWith = null;
    }

    [Fact]
    public async Task Authenticator_setup_then_login_requires_a_code()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();
        var key = await EnableAuthenticatorAsync(client);

        var status = await client.GetFromJsonAsync<TwoFactorStatusDto>("/api/account/2fa");
        Assert.True(status!.Enabled);
        Assert.Equal("Authenticator", status.Method);
        Assert.Equal(10, status.RecoveryCodesLeft);

        var challenge = await LoginExpectingChallengeAsync(email);
        Assert.Equal("Authenticator", challenge.Method);
        Assert.Null(challenge.SentTo);

        var verify = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login/2fa",
            new VerifyTwoFactorRequest(challenge.ChallengeToken, NextCode(key)));

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var session = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(HttpStatusCode.OK, (await GetMonitorsAsync(session!.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Login_response_has_no_token_until_the_second_step()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();
        await EnableAuthenticatorAsync(client);

        var response = await LoginAsync(email, Password);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(json.RootElement.GetProperty("requiresTwoFactor").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("accessToken", out _));
    }

    [Fact]
    public async Task Authenticator_setup_rejects_a_wrong_code_and_stays_off()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        await client.PostAsync("/api/account/2fa/authenticator/setup", null);

        var response = await client.PostAsJsonAsync("/api/account/2fa/authenticator/enable", new EnableTwoFactorRequest("000000"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await client.GetFromJsonAsync<TwoFactorStatusDto>("/api/account/2fa"))!.Enabled);
    }

    [Fact]
    public async Task Authenticator_secret_is_encrypted_at_rest()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();
        var setup = await (await client.PostAsync("/api/account/2fa/authenticator/setup", null))
            .Content.ReadFromJsonAsync<AuthenticatorSetupResponse>();

        Assert.StartsWith("data:image/png;base64,", setup!.QrCodeDataUri);
        Assert.Contains("otpauth://totp/WatchTower:", setup.OtpauthUri);

        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Email == email);
        Assert.NotNull(user.AuthenticatorKey);
        Assert.DoesNotContain(setup.SharedKey.Replace(" ", "").ToUpperInvariant(), user.AuthenticatorKey!.ToUpperInvariant());
    }

    [Fact]
    public async Task Code_used_for_setup_cannot_be_replayed_at_login()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();
        var setup = await (await client.PostAsync("/api/account/2fa/authenticator/setup", null))
            .Content.ReadFromJsonAsync<AuthenticatorSetupResponse>();
        var code = CurrentCode(setup!.SharedKey);
        await client.PostAsJsonAsync("/api/account/2fa/authenticator/enable", new EnableTwoFactorRequest(code));

        var replay = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login/2fa",
            new VerifyTwoFactorRequest((await LoginExpectingChallengeAsync(email)).ChallengeToken, code));

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Contains("already used", await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Wrong_codes_burn_the_challenge_after_five_attempts()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();
        var key = await EnableAuthenticatorAsync(client);
        var challenge = await LoginExpectingChallengeAsync(email);
        var anonymous = _factory.CreateClient();

        HttpResponseMessage last = null!;
        for (var i = 0; i < 5; i++)
            last = await anonymous.PostAsJsonAsync("/api/auth/login/2fa", new VerifyTwoFactorRequest(challenge.ChallengeToken, "000000"));
        Assert.Contains("Too many incorrect codes", await last.Content.ReadAsStringAsync());

        var correct = await anonymous.PostAsJsonAsync("/api/auth/login/2fa", new VerifyTwoFactorRequest(challenge.ChallengeToken, NextCode(key)));
        Assert.Equal(HttpStatusCode.BadRequest, correct.StatusCode);
        Assert.Contains("expired", await correct.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Recovery_code_logs_in_once()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();
        var setup = await (await client.PostAsync("/api/account/2fa/authenticator/setup", null))
            .Content.ReadFromJsonAsync<AuthenticatorSetupResponse>();
        var codes = await (await client.PostAsJsonAsync("/api/account/2fa/authenticator/enable",
                new EnableTwoFactorRequest(CurrentCode(setup!.SharedKey))))
            .Content.ReadFromJsonAsync<RecoveryCodesResponse>();
        var recovery = codes!.RecoveryCodes[0];
        var anonymous = _factory.CreateClient();

        var first = await anonymous.PostAsJsonAsync("/api/auth/login/2fa",
            new VerifyTwoFactorRequest((await LoginExpectingChallengeAsync(email)).ChallengeToken, recovery.ToUpperInvariant()));
        var second = await anonymous.PostAsJsonAsync("/api/auth/login/2fa",
            new VerifyTwoFactorRequest((await LoginExpectingChallengeAsync(email)).ChallengeToken, recovery));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(9, (await client.GetFromJsonAsync<TwoFactorStatusDto>("/api/account/2fa"))!.RecoveryCodesLeft);
    }

    [Fact]
    public async Task Email_setup_then_login_sends_a_code()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();

        var setup = await client.PostAsync("/api/account/2fa/email/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var enable = await client.PostAsJsonAsync("/api/account/2fa/email/enable", new EnableTwoFactorRequest(LastCodeSentTo(email)));
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        Assert.Equal(10, (await enable.Content.ReadFromJsonAsync<RecoveryCodesResponse>())!.RecoveryCodes.Count);

        var challenge = await LoginExpectingChallengeAsync(email);
        Assert.Equal("Email", challenge.Method);
        Assert.Matches("^u•••[0-9a-f]@example\\.com$", challenge.SentTo!);

        var anonymous = _factory.CreateClient();
        var wrong = await anonymous.PostAsJsonAsync("/api/auth/login/2fa", new VerifyTwoFactorRequest(challenge.ChallengeToken, "12345"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var verify = await anonymous.PostAsJsonAsync("/api/auth/login/2fa",
            new VerifyTwoFactorRequest(challenge.ChallengeToken, LastCodeSentTo(email)));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Fact]
    public async Task Email_resend_respects_the_cooldown()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();
        await client.PostAsync("/api/account/2fa/email/setup", null);
        await client.PostAsJsonAsync("/api/account/2fa/email/enable", new EnableTwoFactorRequest(LastCodeSentTo(email)));
        var challenge = await LoginExpectingChallengeAsync(email);

        var resend = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login/2fa/resend",
            new ResendTwoFactorCodeRequest(challenge.ChallengeToken));

        Assert.Equal(HttpStatusCode.BadRequest, resend.StatusCode);
        Assert.Contains("just sent", await resend.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disable_requires_the_password_and_restores_one_step_login()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();
        await EnableAuthenticatorAsync(client);

        var wrong = await client.PostAsJsonAsync("/api/account/2fa/disable", new PasswordConfirmationRequest("wrong-password"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var disable = await client.PostAsJsonAsync("/api/account/2fa/disable", new PasswordConfirmationRequest(Password));
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        var login = await (await LoginAsync(email, Password)).Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrEmpty(login!.AccessToken));
    }

    [Fact]
    public async Task Cannot_start_another_setup_while_enabled()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        await EnableAuthenticatorAsync(client);

        var response = await client.PostAsync("/api/account/2fa/email/setup", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("already on", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Regenerating_recovery_codes_replaces_the_old_ones()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        await EnableAuthenticatorAsync(client);

        var response = await client.PostAsJsonAsync("/api/account/2fa/recovery-codes", new PasswordConfirmationRequest(Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var codes = (await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>())!.RecoveryCodes;
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[a-z2-9]{5}-[a-z2-9]{5}$", c));
    }

    private async Task<string> EnableAuthenticatorAsync(HttpClient client)
    {
        var setup = await (await client.PostAsync("/api/account/2fa/authenticator/setup", null))
            .Content.ReadFromJsonAsync<AuthenticatorSetupResponse>();
        var enable = await client.PostAsJsonAsync("/api/account/2fa/authenticator/enable",
            new EnableTwoFactorRequest(CurrentCode(setup!.SharedKey)));
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        return setup.SharedKey;
    }

    private static string CurrentCode(string sharedKey) =>
        Totp.ComputeCode(Totp.FromBase32(sharedKey.Replace(" ", "")), Totp.CurrentStep(DateTimeOffset.UtcNow));

    /// <summary>The code for the next time step: still accepted, and never the one already used during setup.</summary>
    private static string NextCode(string sharedKey) =>
        Totp.ComputeCode(Totp.FromBase32(sharedKey.Replace(" ", "")), Totp.CurrentStep(DateTimeOffset.UtcNow) + 1);

    private string LastCodeSentTo(string email)
    {
        lock (_factory.Emails.Sent)
            return SubjectCode().Match(_factory.Emails.Sent.Last(m => m.To == email).Subject).Groups[1].Value;
    }

    private async Task<TwoFactorChallengeResponse> LoginExpectingChallengeAsync(string email)
    {
        var response = await LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var challenge = await response.Content.ReadFromJsonAsync<TwoFactorChallengeResponse>();
        Assert.True(challenge!.RequiresTwoFactor);
        return challenge;
    }

    private async Task<HttpResponseMessage> GetMonitorsAsync(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync("/api/monitors");
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    private async Task<(HttpClient Client, string Email)> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));
        var login = await (await LoginAsync(email, Password)).Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, email);
    }

    [GeneratedRegex(": (\\d{6})$")]
    private static partial Regex SubjectCode();
}

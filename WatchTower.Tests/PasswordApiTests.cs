using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WatchTower.Application.DTOs;
using WatchTower.Infrastructure.Persistence;

namespace WatchTower.Tests;

public partial class PasswordApiTests : IClassFixture<WatchTowerFactory>
{
    private const string Password = "password123";
    private const string NewPassword = "new-password-456";

    private readonly WatchTowerFactory _factory;

    public PasswordApiTests(WatchTowerFactory factory)
    {
        _factory = factory;
        _factory.Emails.FailWith = null;
    }

    [Fact]
    public async Task Change_password_returns_new_token_and_signs_out_old_sessions()
    {
        var (client, email, oldToken) = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(Password, NewPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newToken = (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        Assert.Equal(HttpStatusCode.OK, (await GetMonitorsAsync(newToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMonitorsAsync(oldToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Change_password_with_wrong_current_password_returns_400_and_keeps_session()
    {
        var (client, _, token) = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("wrong-password", NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("current password is incorrect", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await GetMonitorsAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Change_password_rejects_short_new_password()
    {
        var (client, _, _) = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(Password, "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Change_password_requires_login()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(Password, NewPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_returns_202_without_sending()
    {
        var email = NewEmail();

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(_factory.Emails.Sent, m => m.To == email);
    }

    [Fact]
    public async Task Reset_link_sets_new_password_once_and_signs_out_sessions()
    {
        var (_, email, oldToken) = await CreateAuthenticatedClientAsync();
        var client = _factory.CreateClient();

        var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        var resetToken = ResetTokenSentTo(email);

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(resetToken, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMonitorsAsync(oldToken)).StatusCode);

        var reuse = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(resetToken, "another-password-789"));
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task Reset_token_is_stored_hashed()
    {
        var (_, email, _) = await CreateAuthenticatedClientAsync();
        await _factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var resetToken = ResetTokenSentTo(email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.PasswordResetTokens.SingleAsync(t => t.User.Email == email);
        Assert.NotEqual(resetToken, stored.TokenHash, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expired_reset_link_is_rejected()
    {
        var (_, email, _) = await CreateAuthenticatedClientAsync();
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var resetToken = ResetTokenSentTo(email);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.PasswordResetTokens.SingleAsync(t => t.User.Email == email);
            stored.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(resetToken, NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Contains("invalid or has expired", await reset.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Repeated_forgot_requests_send_one_email_within_cooldown()
    {
        var (_, email, _) = await CreateAuthenticatedClientAsync();
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var second = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        lock (_factory.Emails.Sent)
            Assert.Single(_factory.Emails.Sent, m => m.To == email);
    }

    [Fact]
    public async Task Failed_reset_email_returns_502_and_allows_retry()
    {
        var (_, email, _) = await CreateAuthenticatedClientAsync();
        var client = _factory.CreateClient();
        _factory.Emails.FailWith = new InvalidOperationException("SMTP down");

        var failed = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.DoesNotContain("SMTP down", await failed.Content.ReadAsStringAsync());

        _factory.Emails.FailWith = null;
        var retry = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.NotEmpty(ResetTokenSentTo(email));
    }

    private string ResetTokenSentTo(string email)
    {
        lock (_factory.Emails.Sent)
        {
            var message = _factory.Emails.Sent.Last(m => m.To == email);
            Assert.Equal("[WatchTower] Reset your password", message.Subject);
            return ResetLinkToken().Match(message.TextBody).Groups[1].Value;
        }
    }

    private async Task<HttpResponseMessage> GetMonitorsAsync(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync("/api/monitors");
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    private async Task<(HttpClient Client, string Email, string Token)> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));
        var login = await (await LoginAsync(email, Password)).Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, email, login.AccessToken);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    [GeneratedRegex("#/reset-password\\?token=([0-9a-f]{64})")]
    private static partial Regex ResetLinkToken();
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WatchTower.Application.DTOs;
using WatchTower.Application.Interfaces;

namespace WatchTower.Tests;

public class NotificationApiTests : IClassFixture<WatchTowerFactory>
{
    private readonly WatchTowerFactory _factory;

    public NotificationApiTests(WatchTowerFactory factory)
    {
        _factory = factory;
        _factory.Emails.FailWith = null;
    }

    [Fact]
    public async Task Settings_show_where_alerts_are_sent()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();

        var settings = await client.GetFromJsonAsync<NotificationSettingsDto>("/api/notifications/settings");

        Assert.True(settings!.Enabled);
        Assert.Equal(email, settings.Recipient);
    }

    [Fact]
    public async Task Test_email_is_sent_to_the_users_address()
    {
        var (client, email) = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/notifications/test", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email, (await response.Content.ReadFromJsonAsync<TestEmailResponse>())!.SentTo);
        Assert.Contains(_factory.Emails.Sent, m => m.To == email && m.Subject == "[WatchTower] Test email");
    }

    [Fact]
    public async Task Failed_test_email_returns_502_with_the_smtp_error()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        _factory.Emails.FailWith = new InvalidOperationException("535: Username and Password not accepted");

        var response = await client.PostAsync("/api/notifications/test", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Username and Password not accepted", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Test_email_requires_login()
    {
        var response = await _factory.CreateClient().PostAsync("/api/notifications/test", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(HttpClient Client, string Email)> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "password123"));
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "password123")))
            .Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, email);
    }
}

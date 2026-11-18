using System.Net;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The register form's minimum fill time. The GET signs the issue time with
/// DataProtection; a POST under three seconds old, over two hours old, missing, or with a
/// forged stamp is dropped exactly like the honeypot: the same success-looking redirect, no
/// account, no session. The clock is injected so none of these sleeps.
/// </summary>
[Collection("ProviderCredentialBridge")]
public sealed class RegisterFillTimeTests : IClassFixture<HostedWebHostFixture>
{
    private const string Password = "Correct-h0rse-battery";
    private readonly HostedWebHostFixture _host;

    public RegisterFillTimeTests(HostedWebHostFixture host) => _host = host;

    private static async Task<(string Token, string Stamp)> OpenFormAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/terminal/account/register");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        return (WebHostIntegration.ExtractAntiforgeryToken(html),
                WebHostIntegration.ExtractHiddenValue(html, "FillTimeStamp"));
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string token, string email, string? stamp, string honeypot = "")
    {
        var fields = new List<(string Key, string Value)>
        {
            ("__RequestVerificationToken", token),
            ("Input.Email", email),
            ("Input.Password", Password),
            ("Input.ConfirmPassword", Password),
            ("Website", honeypot),
        };
        if (stamp != null) fields.Add(("FillTimeStamp", stamp));
        return client.PostAsync("/terminal/account/register", WebHostIntegration.Form(fields.ToArray()));
    }

    private async Task AssertNoAccountAsync(string email)
    {
        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Null(await users.FindByEmailAsync(email));
    }

    [Fact]
    public async Task A_submission_under_three_seconds_is_a_silent_fake_success()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var (token, stamp) = await OpenFormAsync(client);
        _host.Clock.Advance(TimeSpan.FromSeconds(1));

        var resp = await PostAsync(client, token, "fill-fast@example.test", stamp);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/terminal/", resp.Headers.Location!.OriginalString);
        Assert.False(WebHostIntegration.SetsAuthCookie(resp));
        await AssertNoAccountAsync("fill-fast@example.test");
    }

    [Fact]
    public async Task Three_seconds_or_more_is_accepted()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var (token, stamp) = await OpenFormAsync(client);
        _host.Clock.Advance(TimeSpan.FromSeconds(3.1));

        var resp = await PostAsync(client, token, "fill-slow@example.test", stamp);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/terminal/", resp.Headers.Location!.OriginalString);
        Assert.True(WebHostIntegration.SetsAuthCookie(resp));

        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.NotNull(await users.FindByEmailAsync("fill-slow@example.test"));
    }

    [Fact]
    public async Task A_missing_stamp_is_a_silent_fake_success()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var (token, _) = await OpenFormAsync(client);

        var resp = await PostAsync(client, token, "fill-missing@example.test", stamp: null);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.False(WebHostIntegration.SetsAuthCookie(resp));
        await AssertNoAccountAsync("fill-missing@example.test");
    }

    [Fact]
    public async Task A_tampered_stamp_is_a_silent_fake_success()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var (token, stamp) = await OpenFormAsync(client);

        var resp = await PostAsync(client, token, "fill-tampered@example.test", stamp + "x");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.False(WebHostIntegration.SetsAuthCookie(resp));
        await AssertNoAccountAsync("fill-tampered@example.test");
    }

    [Fact]
    public async Task A_stamp_older_than_two_hours_is_a_silent_fake_success()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var (token, stamp) = await OpenFormAsync(client);
        _host.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));

        var resp = await PostAsync(client, token, "fill-stale@example.test", stamp);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.False(WebHostIntegration.SetsAuthCookie(resp));
        await AssertNoAccountAsync("fill-stale@example.test");
    }

    [Fact]
    public async Task The_honeypot_still_works_with_a_valid_fill_stamp()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var (token, stamp) = await OpenFormAsync(client);
        _host.Clock.Advance(TimeSpan.FromSeconds(4));

        var resp = await PostAsync(client, token, "fill-bot@example.test", stamp,
            honeypot: "https://spam.example");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.False(WebHostIntegration.SetsAuthCookie(resp));
        await AssertNoAccountAsync("fill-bot@example.test");
    }
}

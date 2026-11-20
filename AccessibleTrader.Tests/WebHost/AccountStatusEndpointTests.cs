using System.Net;
using System.Text.Json;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <c>/terminal/account/status</c> is how the public website knows a visitor is signed in, so
/// it can offer "My account" and "Sign out" instead of "Create your free account" (Cody,
/// 2026-11-20: signed in, set a display name, and the front page still treated him as a
/// stranger). See <see cref="AccountStatusEndpoint"/>.
/// </summary>
[Collection("ProviderCredentialBridge")]
public sealed class AccountStatusEndpointTests : IClassFixture<HostedWebHostFixture>
{
    private const string Url = "/terminal/account/status";
    private readonly HostedWebHostFixture _host;

    public AccountStatusEndpointTests(HostedWebHostFixture host) => _host = host;

    private static async Task<(bool SignedIn, string? DisplayName, string Body)> ReadAsync(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", resp.Headers.CacheControl?.ToString() ?? "");
        string body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var name = root.GetProperty("displayName");
        return (root.GetProperty("signedIn").GetBoolean(),
                name.ValueKind == JsonValueKind.Null ? null : name.GetString(),
                body);
    }

    private async Task SetDisplayNameAsync(string email, string name)
    {
        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        user!.DisplayName = name;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
    }

    [Fact]
    public async Task A_stranger_is_told_signed_out_not_sent_to_the_login_page()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);

        var (signedIn, name, _) = await ReadAsync(await client.GetAsync(Url));

        Assert.False(signedIn);
        Assert.Null(name);
    }

    [Fact]
    public async Task A_signed_in_visitor_is_told_so_with_their_display_name_and_never_their_email()
    {
        const string email = "status-named@example.test";
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(_host.Factory, email);
        await SetDisplayNameAsync(email, "Status Cody");

        var (signedIn, name, body) = await ReadAsync(await client.GetAsync(Url));

        Assert.True(signedIn);
        Assert.Equal("Status Cody", name);
        Assert.DoesNotContain("status-named", body);
        Assert.DoesNotContain("@", body);
    }

    [Fact]
    public async Task Signed_in_without_a_display_name_is_still_signed_in()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "status-noname@example.test");

        var (signedIn, name, _) = await ReadAsync(await client.GetAsync(Url));

        Assert.True(signedIn);
        Assert.Null(name);
    }

    [Fact]
    public async Task Signing_out_shows_on_the_very_next_ask()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "status-logout@example.test");
        Assert.True((await ReadAsync(await client.GetAsync(Url))).SignedIn);

        var token = await WebHostIntegration.GetAntiforgeryTokenAsync(client, "/terminal/account/logout");
        var logout = await client.PostAsync("/terminal/account/logout", WebHostIntegration.Form(
            ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);

        Assert.False((await ReadAsync(await client.GetAsync(Url))).SignedIn);
    }

    [Fact]
    public async Task A_deleted_account_reads_as_signed_out_even_with_its_cookie_still_held()
    {
        const string email = "status-deleted@example.test";
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(_host.Factory, email);
        Assert.True((await ReadAsync(await client.GetAsync(Url))).SignedIn);

        using (var scope = _host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByEmailAsync(email))!)).Succeeded);
        }

        Assert.False((await ReadAsync(await client.GetAsync(Url))).SignedIn);
    }
}

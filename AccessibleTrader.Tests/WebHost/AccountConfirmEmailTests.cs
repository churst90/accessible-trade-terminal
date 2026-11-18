using System.Net;
using System.Text.RegularExpressions;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

[Collection("ProviderCredentialBridge")]
public sealed class AccountConfirmEmailTests : IClassFixture<HostedEmailWebHostFixture>
{
    private readonly HostedEmailWebHostFixture _host;

    public AccountConfirmEmailTests(HostedEmailWebHostFixture host) => _host = host;

    private static string ConfirmPathFrom(string body)
    {
        var m = Regex.Match(body,
            @"https://accessibletrader\.com/terminal(/account/confirmemail\?[^\s]+)");
        Assert.True(m.Success, "the confirmation email contained no change-email link");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private async Task<AppUser> FindUserAsync(UserManager<AppUser> users, string email)
    {
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user!;
    }

    [Fact]
    public async Task Get_changes_nothing_and_post_changes_the_address_and_notifies_the_old_one()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "confirm-old@example.test");

        var request = await HostedAccountTestSupport.PostManageAsync(client, "Email",
            ("EmailInput.NewEmail", "confirm-new@example.test"),
            ("EmailInput.CurrentPassword", HostedAccountTestSupport.Password));
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);

        var confirmation = Assert.Single(_host.Email.Snapshot(),
            m => m.To == "confirm-new@example.test"
              && m.Subject == "Confirm your new Accessible Trader email address");
        var path = ConfirmPathFrom(confirmation.Body);

        // A mail scanner following the link must change nothing.
        var get = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var html = await get.Content.ReadAsStringAsync();
        Assert.Contains("Confirm the new address", html);

        using (var scope = _host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.Equal("confirm-old@example.test",
                (await FindUserAsync(users, "confirm-old@example.test")).Email);
        }

        // The invalid-link page deliberately has no form; any antiforgery token from
        // this host is valid for the POST a stale form might still send.
        var antiforgery = await WebHostIntegration.GetAntiforgeryTokenAsync(
            client, "/terminal/account/login");
        var post = await client.PostAsync(path, WebHostIntegration.Form(
            ("__RequestVerificationToken", antiforgery),
            ("UserId", WebHostIntegration.ExtractHiddenValue(html, "UserId")),
            ("Email", WebHostIntegration.ExtractHiddenValue(html, "Email")),
            ("Code", WebHostIntegration.ExtractHiddenValue(html, "Code"))));

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Contains("Your email is now confirm-new@example.test.",
            await post.Content.ReadAsStringAsync());

        using (var scope = _host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.Null(await users.FindByEmailAsync("confirm-old@example.test"));
            var user = await FindUserAsync(users, "confirm-new@example.test");
            Assert.Equal("confirm-new@example.test", user.Email);
            Assert.Equal("confirm-new@example.test", user.UserName);
        }

        var notice = Assert.Single(_host.Email.Snapshot(),
            m => m.To == "confirm-old@example.test"
              && m.Subject == "Your Accessible Trader email was changed");
        Assert.Contains("confirm-new@example.test", notice.Body);
        Assert.Contains("If this was not you, contact support.", notice.Body);

        // The session survived the rename.
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync("/terminal/account/manage")).StatusCode);
    }

    [Fact]
    public async Task A_bad_link_says_so_and_changes_nothing()
    {
        await WebHostIntegration.SeedUserAsync(_host.Factory, "confirm-stale@example.test",
            HostedAccountTestSupport.Password);
        AppUser user;
        using (var scope = _host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            user = await FindUserAsync(users, "confirm-stale@example.test");
        }

        var path = $"/terminal/account/confirmemail?userId={user.Id}"
                 + "&email=confirm-stale-new%40example.test&code=not-a-real-token";

        using var client = WebHostIntegration.NewClient(_host.Factory);
        var get = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var html = await get.Content.ReadAsStringAsync();
        Assert.Contains("That confirmation link is invalid or has expired. Nothing was changed.", html);
        Assert.DoesNotContain("Confirm the new address", html);

        // The invalid-link page deliberately has no form; any antiforgery token from
        // this host is valid for the POST a stale form might still send.
        var antiforgery = await WebHostIntegration.GetAntiforgeryTokenAsync(
            client, "/terminal/account/login");
        var post = await client.PostAsync(path, WebHostIntegration.Form(
            ("__RequestVerificationToken", antiforgery),
            ("UserId", user.Id),
            ("Email", "confirm-stale-new@example.test"),
            ("Code", "not-a-real-token")));

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Contains("That confirmation link is invalid or has expired. Nothing was changed.",
            await post.Content.ReadAsStringAsync());

        using var finalScope = _host.Factory.Services.CreateScope();
        var finalUsers = finalScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Equal("confirm-stale@example.test",
            (await FindUserAsync(finalUsers, "confirm-stale@example.test")).Email);
    }
}

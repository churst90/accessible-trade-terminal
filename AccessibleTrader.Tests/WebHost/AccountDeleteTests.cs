using System.Net;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

[Collection("ProviderCredentialBridge")]
public sealed class AccountDeleteTests : IClassFixture<HostedEmailWebHostFixture>
{
    private readonly HostedEmailWebHostFixture _host;

    public AccountDeleteTests(HostedEmailWebHostFixture host) => _host = host;

    private async Task<AppUser> FindAsync(string email)
    {
        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user!;
    }

    private string WriteMarker(AppUser user)
    {
        var folder = HostedAccountTestSupport.UserFolder(_host.DataRoot, user.Id);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "settings.json");
        File.WriteAllText(path, "{\"keep\":\"me\"}");
        return path;
    }

    [Fact]
    public async Task Delete_removes_the_account_and_its_folder_and_keeps_it_gone()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "delete-me@example.test");
        var user = await FindAsync("delete-me@example.test");
        var marker = WriteMarker(user);
        var folder = Path.GetDirectoryName(marker)!;

        // A second account's folder is never part of this deletion.
        await WebHostIntegration.SeedUserAsync(_host.Factory, "delete-bystander@example.test",
            HostedAccountTestSupport.Password);
        var bystander = await FindAsync("delete-bystander@example.test");
        var bystanderMarker = WriteMarker(bystander);

        // Give the shared hub a real account for this user so the eviction has something
        // to tear down, and drive its persistence once (the "flush" that used to write the
        // folder back).
        PaperAccountHub hub;
        using (var scope = _host.Factory.Services.CreateScope())
        {
            var current = (CurrentUser)scope.ServiceProvider.GetRequiredService<ICurrentUser>();
            current.Set(user.Id);
            var paper = scope.ServiceProvider.GetRequiredService<IPaperTradingProvider>();
            paper.ResetAccount();
            hub = scope.ServiceProvider.GetRequiredService<PaperAccountHub>();
            Assert.Contains(user.Id, hub.ActiveUsers);
        }
        Assert.True(File.Exists(Path.Combine(folder, "paper_account.json")));

        var resp = await HostedAccountTestSupport.PostManageAsync(client, "Delete",
            ("DeleteInput.CurrentPassword", HostedAccountTestSupport.Password),
            ("DeleteInput.Understands", "true"));

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/terminal/account/login", resp.Headers.Location!.OriginalString);

        Assert.Null(await FindByEmailOrNullAsync("delete-me@example.test"));
        Assert.False(Directory.Exists(folder));
        Assert.DoesNotContain(user.Id, hub.ActiveUsers);
        Assert.True(File.Exists(bystanderMarker), "another user's data folder must be untouched");

        // Nothing left behind may write the folder back.
        await Task.Delay(300);
        Assert.False(Directory.Exists(folder));

        var login = await client.GetAsync("/terminal/account/login");
        Assert.Contains("Your account has been deleted.",
            await login.Content.ReadAsStringAsync());

        // The old password no longer opens an account.
        var attempt = await WebHostIntegration.LoginAsync(
            client, "delete-me@example.test", HostedAccountTestSupport.Password);
        Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
        Assert.False(WebHostIntegration.SetsAuthCookie(attempt));
    }

    [Fact]
    public async Task A_wrong_password_deletes_nothing()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "delete-wrongpw@example.test");
        var user = await FindAsync("delete-wrongpw@example.test");
        var marker = WriteMarker(user);

        var resp = await HostedAccountTestSupport.PostManageAsync(client, "Delete",
            ("DeleteInput.CurrentPassword", "not-the-password"),
            ("DeleteInput.Understands", "true"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Current password is incorrect",
            await resp.Content.ReadAsStringAsync());
        Assert.NotNull(await FindByEmailOrNullAsync("delete-wrongpw@example.test"));
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task A_missing_confirmation_checkbox_deletes_nothing()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "delete-nobox@example.test");
        var user = await FindAsync("delete-nobox@example.test");
        var marker = WriteMarker(user);

        var resp = await HostedAccountTestSupport.PostManageAsync(client, "Delete",
            ("DeleteInput.CurrentPassword", HostedAccountTestSupport.Password));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Tick the box to confirm",
            await resp.Content.ReadAsStringAsync());
        Assert.NotNull(await FindByEmailOrNullAsync("delete-nobox@example.test"));
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task A_wrong_two_factor_code_deletes_nothing()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "delete-2fa@example.test");
        var user = await FindAsync("delete-2fa@example.test");
        var marker = WriteMarker(user);

        using (var scope = _host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var live = await users.FindByEmailAsync("delete-2fa@example.test");
            await users.ResetAuthenticatorKeyAsync(live!);
            Assert.True((await users.SetTwoFactorEnabledAsync(live!, true)).Succeeded);
        }

        var resp = await HostedAccountTestSupport.PostManageAsync(client, "Delete",
            ("DeleteInput.CurrentPassword", HostedAccountTestSupport.Password),
            ("DeleteInput.Understands", "true"),
            ("DeleteInput.TwoFactorCode", "000000"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Nothing was deleted",
            await resp.Content.ReadAsStringAsync());
        var kept = await FindByEmailOrNullAsync("delete-2fa@example.test");
        Assert.NotNull(kept);
        Assert.True(File.Exists(marker));
        // The right password reset the count; the wrong code then counts toward lockout.
        Assert.Equal(1, kept!.AccessFailedCount);
    }

    private async Task<AppUser?> FindByEmailOrNullAsync(string email)
    {
        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await users.FindByEmailAsync(email);
    }
}

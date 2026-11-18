using System.Net;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

[Collection("ProviderCredentialBridge")]
public sealed class AccountManagePasswordTests : IClassFixture<HostedEmailWebHostFixture>
{
    private readonly HostedEmailWebHostFixture _host;

    public AccountManagePasswordTests(HostedEmailWebHostFixture host) => _host = host;

    [Fact]
    public async Task Changing_the_password_keeps_the_session_and_emails_a_notice()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "pw-change@example.test");

        var resp = await HostedAccountTestSupport.PostManageAsync(client, "Password",
            ("PasswordInput.CurrentPassword", HostedAccountTestSupport.Password),
            ("PasswordInput.NewPassword", "BrandNewPass9"),
            ("PasswordInput.ConfirmPassword", "BrandNewPass9"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Your password was changed.", await resp.Content.ReadAsStringAsync());

        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync("pw-change@example.test");
        Assert.NotNull(user);
        Assert.True(await users.CheckPasswordAsync(user!, "BrandNewPass9"));
        Assert.False(await users.CheckPasswordAsync(user!, HostedAccountTestSupport.Password));

        // Still signed in on this client.
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync("/terminal/account/manage")).StatusCode);

        var mail = Assert.Single(_host.Email.Snapshot(),
            m => m.To == "pw-change@example.test"
              && m.Subject == "Your Accessible Trader password was changed");
        Assert.Contains("UTC", mail.Body);
        Assert.Contains("/account/forgotpassword", mail.Body);
        Assert.Contains("contact support", mail.Body);
    }

    [Fact]
    public async Task A_wrong_current_password_changes_nothing()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "pw-wrong@example.test");

        var resp = await HostedAccountTestSupport.PostManageAsync(client, "Password",
            ("PasswordInput.CurrentPassword", "not-the-password"),
            ("PasswordInput.NewPassword", "BrandNewPass9"),
            ("PasswordInput.ConfirmPassword", "BrandNewPass9"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Current password is incorrect", await resp.Content.ReadAsStringAsync());

        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync("pw-wrong@example.test");
        Assert.True(await users.CheckPasswordAsync(user!, HostedAccountTestSupport.Password));
        Assert.False(await users.CheckPasswordAsync(user!, "BrandNewPass9"));
        // The wrong guess counts toward lockout, as on the sign-in page.
        Assert.Equal(1, await users.GetAccessFailedCountAsync(user!));
    }
}

[Collection("ProviderCredentialBridge")]
public sealed class AccountManageEmailTests : IClassFixture<HostedEmailWebHostFixture>
{
    private readonly HostedEmailWebHostFixture _host;

    public AccountManageEmailTests(HostedEmailWebHostFixture host) => _host = host;

    [Fact]
    public async Task A_change_request_emails_the_new_address_and_the_old_one_stays()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "email-old@example.test");

        var resp = await HostedAccountTestSupport.PostManageAsync(client, "Email",
            ("EmailInput.NewEmail", "email-new@example.test"),
            ("EmailInput.CurrentPassword", HostedAccountTestSupport.Password));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("We sent a link to email-new@example.test. Your email stays email-old@example.test until you open it.",
            await resp.Content.ReadAsStringAsync());

        var oldUser = await HostedAccountTestSupport.UserAsync(_host.Factory, "email-old@example.test");
        Assert.Equal("email-old@example.test", oldUser.Email);

        var mail = Assert.Single(_host.Email.Snapshot(),
            m => m.To == "email-new@example.test"
              && m.Subject == "Confirm your new Accessible Trader email address");
        Assert.Contains("/account/confirmemail?userId=", mail.Body);
        Assert.Contains("email=email-new%40example.test", mail.Body);
        Assert.Contains("code=", mail.Body);
        Assert.Contains("Your email stays email-old@example.test", mail.Body);
    }

    [Fact]
    public async Task With_no_email_service_the_section_says_so_and_has_no_form()
    {
        using var host = new HostedWebHostFixture();
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            host.Factory, "email-off@example.test");

        var page = await client.GetAsync("/terminal/account/manage");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains("Changing your email needs the email service", html);
        Assert.DoesNotContain("handler=Email", html);
    }

    [Fact]
    public async Task An_address_used_by_another_account_is_refused_plainly()
    {
        await WebHostIntegration.SeedUserAsync(_host.Factory, "email-other@example.test",
            HostedAccountTestSupport.Password);
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "email-mine@example.test");

        var resp = await HostedAccountTestSupport.PostManageAsync(client, "Email",
            ("EmailInput.NewEmail", "email-other@example.test"),
            ("EmailInput.CurrentPassword", HostedAccountTestSupport.Password));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("That address is already used by another account.",
            await resp.Content.ReadAsStringAsync());
        Assert.Equal("email-mine@example.test",
            (await HostedAccountTestSupport.UserAsync(_host.Factory, "email-mine@example.test")).Email);
    }
}

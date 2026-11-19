using System.Net;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

public class DisplayNameRulesTests
{
    [Theory]
    [InlineData("Cody", "Cody")]
    [InlineData("  Cody Hurst  ", "Cody Hurst")]
    [InlineData("trader_01", "trader_01")]
    [InlineData("A.B-C", "A.B-C")]
    [InlineData("abc", "abc")]
    [InlineData("a23456789012345678901234567890", "a23456789012345678901234567890")]
    public void Valid_names_are_trimmed_and_kept(string input, string expected)
    {
        Assert.True(DisplayNameRules.TryNormalize(input, out var name, out var error));
        Assert.Null(error);
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Empty_clears_the_name(string input, string? expected)
    {
        Assert.True(DisplayNameRules.TryNormalize(input, out var name, out var error));
        Assert.Null(error);
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("ab", "at least 3")]
    [InlineData("a234567890123456789012345678901", "at most 30")]
    [InlineData("me@example.com", "cannot contain @")]
    [InlineData("hello!", "letters, digits, spaces, dots, dashes and underscores")]
    public void Invalid_names_say_which_rule_failed(string input, string expectedWords)
    {
        Assert.False(DisplayNameRules.TryNormalize(input, out var name, out var error));
        Assert.Null(name);
        Assert.Contains(expectedWords, error);
    }
}

public class AccountDataFolderGuardTests
{
    private static readonly string Root = TestTemp.NewPath("att-guard-root-");

    [Fact]
    public void A_user_folder_under_users_is_safe()
    {
        var folder = Path.Combine(Root, "users", "11111111-2222-3333-4444-555555555555");
        Assert.True(AccountDataFolderGuard.IsSafeUserFolder(Root, folder, out var full));
        Assert.Equal(Path.GetFullPath(folder), full);
    }

    [Fact]
    public void The_anon_folder_is_never_safe()
    {
        var folder = Path.Combine(Root, "users", "anon");
        Assert.False(AccountDataFolderGuard.IsSafeUserFolder(Root, folder, out _));
    }

    [Fact]
    public void The_users_root_itself_is_never_safe()
    {
        Assert.False(AccountDataFolderGuard.IsSafeUserFolder(
            Root, Path.Combine(Root, "users"), out _));
    }

    [Fact]
    public void A_sibling_that_merely_starts_with_users_is_never_safe()
    {
        Assert.False(AccountDataFolderGuard.IsSafeUserFolder(
            Root, Path.Combine(Root, "users2", "someone"), out _));
    }

    [Fact]
    public void A_folder_inside_another_users_folder_is_never_safe()
    {
        // Only a direct child of users/ is an account's folder; anything deeper belongs to
        // the account (or the anon space) above it.
        Assert.False(AccountDataFolderGuard.IsSafeUserFolder(
            Root, Path.Combine(Root, "users", "anon", "11111111-2222-3333-4444-555555555555"), out _));
        Assert.False(AccountDataFolderGuard.IsSafeUserFolder(
            Root, Path.Combine(Root, "users", "someone-else", "charts"), out _));
    }

    [Fact]
    public void Traversal_out_of_the_users_root_is_never_safe()
    {
        Assert.False(AccountDataFolderGuard.IsSafeUserFolder(
            Root, Path.Combine(Root, "users", "..", "settings"), out _));
    }
}

[Collection("ProviderCredentialBridge")]
public sealed class AccountManageDisplayNameTests : IClassFixture<HostedEmailWebHostFixture>
{
    private readonly HostedEmailWebHostFixture _host;

    public AccountManageDisplayNameTests(HostedEmailWebHostFixture host) => _host = host;

    private async Task SetProfileAsync(string email, string? name, bool optIn)
    {
        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        user!.DisplayName = name;
        user.LeaderboardOptIn = optIn;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
    }

    [Fact]
    public async Task A_display_name_is_saved()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "name-save@example.test");

        var resp = await HostedAccountTestSupport.PostManageAsync(
            client, "DisplayName", ("NewDisplayName", "Cody H."));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Your display name was saved.", await resp.Content.ReadAsStringAsync());
        Assert.Equal("Cody H.",
            (await HostedAccountTestSupport.UserAsync(_host.Factory, "name-save@example.test")).DisplayName);
    }

    [Fact]
    public async Task A_display_name_already_taken_in_another_case_is_refused_in_words()
    {
        await WebHostIntegration.SeedUserAsync(_host.Factory, "name-owner@example.test",
            HostedAccountTestSupport.Password);
        await SetProfileAsync("name-owner@example.test", "Cody", optIn: true);
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "name-taker@example.test");

        var resp = await HostedAccountTestSupport.PostManageAsync(
            client, "DisplayName", ("NewDisplayName", "cody"));

        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("already taken", html);
        Assert.Contains("regardless of letter case", html);
        Assert.Null(
            (await HostedAccountTestSupport.UserAsync(_host.Factory, "name-taker@example.test")).DisplayName);
    }

    [Fact]
    public async Task Clearing_the_name_turns_the_leaderboard_off_and_says_so()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "name-clear@example.test");
        await SetProfileAsync("name-clear@example.test", "Keeper", optIn: true);

        var resp = await HostedAccountTestSupport.PostManageAsync(
            client, "DisplayName", ("NewDisplayName", ""));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("no longer on the public leaderboard",
            await resp.Content.ReadAsStringAsync());

        var user = await HostedAccountTestSupport.UserAsync(_host.Factory, "name-clear@example.test");
        Assert.Null(user.DisplayName);
        Assert.False(user.LeaderboardOptIn);
    }
}

[Collection("ProviderCredentialBridge")]
public sealed class AccountManageLeaderboardTests : IClassFixture<HostedEmailWebHostFixture>
{
    private readonly HostedEmailWebHostFixture _host;

    public AccountManageLeaderboardTests(HostedEmailWebHostFixture host) => _host = host;

    [Fact]
    public async Task Turning_it_on_without_a_display_name_is_refused()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "board-noname@example.test");

        var resp = await HostedAccountTestSupport.PostManageAsync(
            client, "Leaderboard", ("NewLeaderboardOptIn", "true"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Set a display name first.", await resp.Content.ReadAsStringAsync());
        Assert.False((await HostedAccountTestSupport.UserAsync(
            _host.Factory, "board-noname@example.test")).LeaderboardOptIn);
    }

    [Fact]
    public async Task With_a_display_name_the_choice_is_saved()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "board-name@example.test");

        using (var scope = _host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync("board-name@example.test");
            user!.DisplayName = "Board Trader";
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }

        var resp = await HostedAccountTestSupport.PostManageAsync(
            client, "Leaderboard", ("NewLeaderboardOptIn", "true"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("You will appear on the public leaderboard",
            await resp.Content.ReadAsStringAsync());
        Assert.True((await HostedAccountTestSupport.UserAsync(
            _host.Factory, "board-name@example.test")).LeaderboardOptIn);
    }
}

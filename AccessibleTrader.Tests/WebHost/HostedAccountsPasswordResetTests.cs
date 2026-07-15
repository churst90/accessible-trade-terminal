using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.WebHost;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Exercises the Identity token plumbing the admin-mediated password-reset flow
/// relies on (Program.cs <c>--reset-link</c> mints the token; the ResetPassword
/// page consumes it). Mirrors the ServiceCollection + EphemeralDataProtection
/// setup of <see cref="HostedAccountsAuthPolicyTests"/>, but drives a real
/// UserManager against an on-disk SQLite auth store so the reset-token generate →
/// consume round-trip is validated end to end.
/// </summary>
public class HostedAccountsPasswordResetTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly ServiceProvider _provider;

    public HostedAccountsPasswordResetTests()
    {
        _dataRoot = Directory.CreateTempSubdirectory("att-reset-tests-").FullName;
        var services = new ServiceCollection();
        services.AddLogging();
        // AddHostedAccounts assumes the host already registered DataProtection
        // (Program.cs does); an ephemeral provider keeps the test off the real key
        // ring while still backing the reset-token DataProtector.
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddHostedAccounts(_dataRoot);
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        try { Directory.Delete(_dataRoot, recursive: true); } catch { /* best effort */ }
    }

    private async Task<UserManager<AppUser>> SeedUserAsync(IServiceScope scope, string email, string password)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email };
        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        return users;
    }

    [Fact]
    public async Task ResetPassword_WithFreshToken_SetsTheNewPassword()
    {
        using var scope = _provider.CreateScope();
        var users = await SeedUserAsync(scope, "reset-happy@example.com", "OldPassword1");
        var user = await users.FindByEmailAsync("reset-happy@example.com");
        Assert.NotNull(user);

        var token = await users.GeneratePasswordResetTokenAsync(user!);
        var result = await users.ResetPasswordAsync(user!, token, "BrandNewPass9");

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        Assert.True(await users.CheckPasswordAsync(user!, "BrandNewPass9"));
        Assert.False(await users.CheckPasswordAsync(user!, "OldPassword1"));
    }

    [Fact]
    public async Task ResetPassword_WithBadToken_FailsWithInvalidToken()
    {
        using var scope = _provider.CreateScope();
        var users = await SeedUserAsync(scope, "reset-bad@example.com", "OldPassword1");
        var user = await users.FindByEmailAsync("reset-bad@example.com");
        Assert.NotNull(user);

        var result = await users.ResetPasswordAsync(user!, "not-a-real-token", "BrandNewPass9");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "InvalidToken");
        // The original password must still be the valid one — a bad token is a no-op.
        Assert.True(await users.CheckPasswordAsync(user!, "OldPassword1"));
    }
}

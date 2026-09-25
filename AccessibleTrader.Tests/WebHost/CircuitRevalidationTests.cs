using System.Reflection;
using System.Security.Claims;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// A LIVE circuit is evicted when the account's security stamp rotates or the account is
/// locked out — the property <see cref="IdentityRevalidatingAuthenticationStateProvider"/>
/// was written for.
///
/// <para>
/// <b>What nothing checked (A2o, survivors O31 and O32).</b> The provider's whole decision is
/// two lines: <c>IsLockedOutAsync → false</c> and <c>principalStamp == userStamp</c>. Replacing
/// the comparison with <c>principalStamp != null</c> (a stolen session survives the victim's
/// password reset and 2FA enrolment for as long as the tab stays open) and short-circuiting
/// the lockout test both left the suite green: the only coverage was the options-object
/// registration in <c>HostedAccountsAuthPolicyTests</c>, which asserts the TYPE is
/// registered, never what it decides. The class's own summary describes the exact outage
/// this reopens — the attacker's WebSocket keeps the charts, alerts, paper account and
/// alert-channel credentials after the victim has done everything right.
/// </para>
///
/// <para>
/// Driven against the REAL Identity store of a booted hosted head, with a principal minted
/// by the real <see cref="SignInManager{TUser}"/> (so it carries the stamp claim exactly as
/// a sign-in cookie does). Each eviction test carries its own positive control — the same
/// principal validates BEFORE the change — so a provider that rejected everything would not
/// pass. The protected override is reached by reflection, the precedent
/// <c>tests-that-mirror-production-logic</c> records for a rule that is not public.
/// </para>
/// </summary>
[Collection("ProviderCredentialBridge")]
public sealed class CircuitRevalidationTests : IDisposable
{
    private readonly PluginBridgeScope _bridges = new();
    private readonly string _dataRoot = TestTemp.NewDir("att-revalidate-");
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<WebHostDemoMode> _factory;

    private const string Password = "Correct-h0rse-battery";

    public CircuitRevalidationTests()
    {
        _factory = WebHostIntegration.HostedFactory(_dataRoot);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _bridges.Dispose();
        try { Directory.Delete(_dataRoot, recursive: true); } catch { }
    }

    private IdentityRevalidatingAuthenticationStateProvider NewProvider()
        => new(_factory.Services.GetRequiredService<ILoggerFactory>(),
               _factory.Services.GetRequiredService<IServiceScopeFactory>(),
               _factory.Services.GetRequiredService<IOptions<IdentityOptions>>());

    private async Task<bool> StillValid(ClaimsPrincipal principal)
    {
        var method = typeof(IdentityRevalidatingAuthenticationStateProvider).GetMethod(
            "ValidateAuthenticationStateAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        using var provider = NewProvider();
        return await (Task<bool>)method!.Invoke(provider,
            new object[] { new AuthenticationState(principal), CancellationToken.None })!;
    }

    private async Task<(AppUser User, ClaimsPrincipal Principal)> SignedInCircuit(string email)
    {
        var user = await WebHostIntegration.SeedUserAsync(_factory, email, Password);
        using var scope = _factory.Services.CreateScope();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<AppUser>>();
        var principal = await signIn.CreateUserPrincipalAsync(user);
        Assert.False(string.IsNullOrEmpty(principal.FindFirstValue(
            _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType)),
            "the minted principal carries no security-stamp claim — the test would prove nothing");
        return (user, principal);
    }

    [Fact]
    public async Task A_password_reset_evicts_a_circuit_opened_before_it()
    {
        var (user, principal) = await SignedInCircuit("reval-stamp@example.test");
        Assert.True(await StillValid(principal), "positive control: an untouched session must stay valid");

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var fresh = await users.FindByIdAsync(user.Id);
            var token = await users.GeneratePasswordResetTokenAsync(fresh!);
            Assert.True((await users.ResetPasswordAsync(fresh!, token, "An0ther-long-passphrase")).Succeeded);
        }

        Assert.False(await StillValid(principal),
            "a circuit opened before the password reset is still authorised — a stolen session "
            + "survives the victim's own remediation");
    }

    [Fact]
    public async Task A_locked_out_account_loses_its_open_circuit()
    {
        var (user, principal) = await SignedInCircuit("reval-lock@example.test");
        Assert.True(await StillValid(principal), "positive control: an untouched session must stay valid");

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var fresh = await users.FindByIdAsync(user.Id);
            // Lockout does NOT rotate the stamp — which is exactly why the provider checks it
            // separately. Asserted, so this test cannot pass on the stamp comparison alone.
            string before = await users.GetSecurityStampAsync(fresh!);
            await users.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
            Assert.Equal(before, await users.GetSecurityStampAsync(fresh!));
        }

        Assert.False(await StillValid(principal),
            "a locked-out account's open circuit is still authorised");
    }
}

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AccessibleTrader.WebHost;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// TOTP 2FA against the REAL hosted-accounts stack (AddHostedAccounts DI, real
/// UserManager, real sqlite in a temp dir): key minting, code verification with
/// an independently-computed RFC 6238 code (so the test catches a provider
/// misconfiguration, not just Identity agreeing with itself), recovery-code
/// issue/redeem/single-use, and the enrollment helpers' formatting contracts.
/// </summary>
public class HostedAccountsTwoFactorTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly Microsoft.AspNetCore.Identity.UserManager<AppUser> _users;

    public HostedAccountsTwoFactorTests()
    {
        _dataRoot = Directory.CreateTempSubdirectory("att-2fa-tests-").FullName;
        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddHostedAccounts(_dataRoot);
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
        _users = _scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        try { Directory.Delete(_dataRoot, recursive: true); } catch { /* best effort */ }
    }

    private async Task<AppUser> NewUserAsync()
    {
        var user = new AppUser { UserName = $"u{Guid.NewGuid():N}@example.com" };
        user.Email = user.UserName;
        var create = await _users.CreateAsync(user, "Correct horse battery 7 staple");
        Assert.True(create.Succeeded, string.Join("; ", create.Errors.Select(e => e.Description)));
        return user;
    }

    // ── The full enrollment round-trip ───────────────────────────────────────

    [Fact]
    public async Task Enrollment_KeyMint_Verify_Enable_RoundTrips()
    {
        var user = await NewUserAsync();

        Assert.Null(await _users.GetAuthenticatorKeyAsync(user)); // fresh account: no key
        await _users.ResetAuthenticatorKeyAsync(user);
        string? key = await _users.GetAuthenticatorKeyAsync(user);
        Assert.False(string.IsNullOrEmpty(key));

        // Independently computed RFC 6238 code — proves the registered provider
        // really is standard TOTP that any authenticator app can produce.
        string code = ComputeTotp(key!, DateTimeOffset.UtcNow);
        bool valid = await _users.VerifyTwoFactorTokenAsync(
            user, _users.Options.Tokens.AuthenticatorTokenProvider, code);
        Assert.True(valid);

        await _users.SetTwoFactorEnabledAsync(user, true);
        Assert.True(await _users.GetTwoFactorEnabledAsync(user));
    }

    [Fact]
    public async Task Wrong_code_is_rejected()
    {
        var user = await NewUserAsync();
        await _users.ResetAuthenticatorKeyAsync(user);

        bool valid = await _users.VerifyTwoFactorTokenAsync(
            user, _users.Options.Tokens.AuthenticatorTokenProvider, "000000");
        // One-in-a-million flake guard: if 000000 happened to be the current
        // code, any OTHER fixed code can't also match inside the window.
        if (valid)
            valid = await _users.VerifyTwoFactorTokenAsync(
                user, _users.Options.Tokens.AuthenticatorTokenProvider, "111111");
        Assert.False(valid);
    }

    [Fact]
    public async Task Recovery_codes_are_single_use()
    {
        var user = await NewUserAsync();
        await _users.ResetAuthenticatorKeyAsync(user);
        await _users.SetTwoFactorEnabledAsync(user, true);

        var codes = (await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToList();
        Assert.Equal(10, codes.Count);
        Assert.Equal(10, await _users.CountRecoveryCodesAsync(user));

        var first = await _users.RedeemTwoFactorRecoveryCodeAsync(user, codes[0]);
        Assert.True(first.Succeeded);
        Assert.Equal(9, await _users.CountRecoveryCodesAsync(user));

        var replay = await _users.RedeemTwoFactorRecoveryCodeAsync(user, codes[0]);
        Assert.False(replay.Succeeded); // consumed — replay must fail
    }

    [Fact]
    public async Task Regenerating_codes_invalidates_the_old_set()
    {
        var user = await NewUserAsync();
        await _users.ResetAuthenticatorKeyAsync(user);
        await _users.SetTwoFactorEnabledAsync(user, true);

        var oldCodes = (await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToList();
        await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        var redeemOld = await _users.RedeemTwoFactorRecoveryCodeAsync(user, oldCodes[0]);
        Assert.False(redeemOld.Succeeded);
    }

    [Fact]
    public async Task Disable_flow_resets_the_key_so_reenrollment_mints_a_fresh_secret()
    {
        // Mirrors SecurityModel.OnPostDisableAsync: disable + key reset together.
        // Re-enabling must never revive a possibly-leaked old secret.
        var user = await NewUserAsync();
        await _users.ResetAuthenticatorKeyAsync(user);
        string? oldKey = await _users.GetAuthenticatorKeyAsync(user);
        await _users.SetTwoFactorEnabledAsync(user, true);

        await _users.SetTwoFactorEnabledAsync(user, false);
        await _users.ResetAuthenticatorKeyAsync(user);
        string? newKey = await _users.GetAuthenticatorKeyAsync(user);

        Assert.NotEqual(oldKey, newKey);
    }

    // ── Enrollment helper contracts ──────────────────────────────────────────

    [Fact]
    public void SharedKey_formats_in_groups_of_four_lowercase()
    {
        Assert.Equal("gmju 6fk2 abcd ef", TwoFactorSupport.FormatSharedKey("GMJU6FK2ABCDEF"));
        Assert.Equal("", TwoFactorSupport.FormatSharedKey(""));
    }

    [Fact]
    public void Otpauth_uri_escapes_issuer_and_email_but_not_the_secret()
    {
        string uri = TwoFactorSupport.BuildOtpauthUri("a+b@example.com", "GMJU6FK2");
        Assert.StartsWith("otpauth://totp/Accessible%20Trade%20Terminal:", uri);
        Assert.Contains("a%2Bb%40example.com", uri);
        Assert.Contains("secret=GMJU6FK2&", uri);
        Assert.Contains("issuer=Accessible%20Trade%20Terminal", uri);
        Assert.Contains("digits=6", uri);
    }

    [Theory]
    [InlineData("123 456", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData("  123456  ", "123456")]
    [InlineData(null, "")]
    public void Codes_normalize_the_way_people_type_them(string? raw, string expected)
        => Assert.Equal(expected, TwoFactorSupport.NormalizeCode(raw));

    [Fact]
    public void Qr_renders_as_an_inline_png_data_uri()
    {
        string uri = TwoFactorSupport.QrPngDataUri("otpauth://totp/test?secret=ABC");
        Assert.StartsWith("data:image/png;base64,", uri);
        byte[] png = Convert.FromBase64String(uri.Substring("data:image/png;base64,".Length));
        // PNG magic bytes — the img tag must actually render.
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
    }

    // ── Minimal RFC 6238 (30s step, 6 digits, HMAC-SHA1) ─────────────────────

    private static string ComputeTotp(string base32Key, DateTimeOffset now)
    {
        byte[] key = Base32Decode(base32Key);
        long step = now.ToUnixTimeSeconds() / 30;
        Span<byte> counter = stackalloc byte[8];
        for (int i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xff); step >>= 8; }
        using var hmac = new HMACSHA1(key);
        byte[] hash = hmac.ComputeHash(counter.ToArray());
        int offset = hash[^1] & 0x0f;
        int binary = ((hash[offset] & 0x7f) << 24)
                   | (hash[offset + 1] << 16)
                   | (hash[offset + 2] << 8)
                   | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.TrimEnd('=').ToUpperInvariant();
        var output = new System.Collections.Generic.List<byte>(input.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (char c in input)
        {
            int v = alphabet.IndexOf(c);
            if (v < 0) continue;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xff));
            }
        }
        return output.ToArray();
    }
}

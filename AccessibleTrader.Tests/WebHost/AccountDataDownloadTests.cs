using System.IO.Compression;
using System.Net;
using System.Text.Json;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

[Collection("ProviderCredentialBridge")]
public sealed class AccountDataDownloadTests : IClassFixture<HostedEmailWebHostFixture>
{
    private readonly HostedEmailWebHostFixture _host;

    public AccountDataDownloadTests(HostedEmailWebHostFixture host) => _host = host;

    private static void Write(string folder, string relative, string contents)
    {
        var path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    [Fact]
    public async Task The_zip_has_this_users_files_at_their_paths_and_never_another_users()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "download-mine@example.test");
        var mine = await HostedAccountTestSupport.UserAsync(_host.Factory, "download-mine@example.test");

        await WebHostIntegration.SeedUserAsync(_host.Factory, "download-other@example.test",
            HostedAccountTestSupport.Password);
        var other = await HostedAccountTestSupport.UserAsync(_host.Factory, "download-other@example.test");

        var myFolder = HostedAccountTestSupport.UserFolder(_host.DataRoot, mine.Id);
        var otherFolder = HostedAccountTestSupport.UserFolder(_host.DataRoot, other.Id);
        Write(myFolder, "settings.json", "{\"theme\":\"mine\"}");
        Write(myFolder, Path.Combine("Workspaces", "layout.json"), "{\"chart\":\"mine\"}");
        Write(otherFolder, "private.json", "{\"owner\":\"someone-else\"}");

        var resp = await HostedAccountTestSupport.PostManageAsync(client, "Download");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/zip", resp.Content.Headers.ContentType?.MediaType);
        Assert.Contains("accessible-trader-data-",
            resp.Content.Headers.ContentDisposition?.FileNameStar
            ?? resp.Content.Headers.ContentDisposition?.FileName ?? "");

        using var zip = new ZipArchive(
            new MemoryStream(await resp.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("settings.json", names);
        Assert.Contains("Workspaces/layout.json", names);
        Assert.Contains("account.json", names);
        Assert.DoesNotContain("private.json", names);
        Assert.DoesNotContain(names, n => n.Contains("someone-else"));

        using var reader = new StreamReader(zip.GetEntry("account.json")!.Open());
        var json = await reader.ReadToEndAsync();
        Assert.Contains("download-mine@example.test", json);
    }

    [Fact]
    public async Task Account_json_carries_no_password_hash_security_stamp_or_tokens()
    {
        using var client = await HostedAccountTestSupport.SeedAndSignInAsync(
            _host.Factory, "download-secrets@example.test");
        var user = await HostedAccountTestSupport.UserAsync(_host.Factory, "download-secrets@example.test");

        // Make the secrets real and distinctive, then prove none of them reach the zip.
        using (var scope = _host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var live = (await users.FindByEmailAsync("download-secrets@example.test"))!;
            await users.ResetAuthenticatorKeyAsync(live);
            live.DisplayName = "Data Owner";
            live.LeaderboardOptIn = true;
            Assert.True((await users.UpdateAsync(live)).Succeeded);
        }

        var current = await HostedAccountTestSupport.UserAsync(_host.Factory, "download-secrets@example.test");
        var resp = await HostedAccountTestSupport.PostManageAsync(client, "Download");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var zip = new ZipArchive(
            new MemoryStream(await resp.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("account.json")!.Open());
        var json = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("download-secrets@example.test", root.GetProperty("email").GetString());
        Assert.Equal("Data Owner", root.GetProperty("displayName").GetString());
        Assert.True(root.GetProperty("leaderboardOptIn").GetBoolean());
        Assert.False(root.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.True(root.TryGetProperty("tier", out _));
        Assert.True(root.TryGetProperty("createdUtc", out _));
        Assert.True(root.TryGetProperty("lastSeenUtc", out _));

        foreach (var forbidden in new[]
                 {
                     "passwordHash", "securityStamp", "authenticatorKey",
                     "recoveryCodes", "token", "concurrencyStamp",
                 })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(user.PasswordHash!, json);
        Assert.DoesNotContain(current.SecurityStamp!, json);
    }
}

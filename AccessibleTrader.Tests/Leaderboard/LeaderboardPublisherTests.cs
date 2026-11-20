using System.Globalization;
using AccessibleTrader.Tests.WebHost;
using AccessibleTrader.WebHost.Account;
using AccessibleTrader.WebHost.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests.Leaderboard;

/// <summary>
/// End-to-end coverage of the hosted leaderboard publisher: real Program.cs, real auth.db,
/// real per-user folders. Each publish is driven by <c>PublishOnceAsync</c> so the suite
/// never waits five minutes.
/// </summary>
[Collection("ProviderCredentialBridge")]
public sealed class LeaderboardPublisherTests : IDisposable
{
    private const string Password = "Correct-h0rse-battery";

    // One host per test: a shared fixture would carry every earlier test's users into
    // the next publish, and then "exactly two entries" would depend on test order.
    private readonly HostedWebHostFixture _host = new();

    public void Dispose() => _host.Dispose();

    private string OutputPath => Path.Combine(_host.DataRoot, "public", "leaderboard.json");

    private LeaderboardPublisher Publisher()
        => _host.Factory.Services.GetServices<IHostedService>()
            .OfType<LeaderboardPublisher>().Single();

    private Task PublishAsync() => Publisher().PublishOnceAsync(CancellationToken.None);

    private async Task<AppUser> SeedAsync(string email, string? displayName, bool optIn)
    {
        var created = await WebHostIntegration.SeedUserAsync(_host.Factory, email, Password);
        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var live = await users.FindByIdAsync(created.Id);
        Assert.NotNull(live);
        live!.DisplayName = displayName;
        live.LeaderboardOptIn = optIn;
        var updated = await users.UpdateAsync(live);
        Assert.True(updated.Succeeded,
            string.Join("; ", updated.Errors.Select(e => e.Description)));
        return live;
    }

    private void WriteAccount(AppUser user, string json)
    {
        string folder = Path.Combine(_host.DataRoot, "users", user.Id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "paper_account.json"), json);
    }

    private void WriteAccount(string folderName, string json)
    {
        string folder = Path.Combine(_host.DataRoot, "users", folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "paper_account.json"), json);
    }

    private static string Account(
        double cash = 100_000, int trades = 1, int wins = 1, double realized = 0,
        string positions = "[]", string collateral = "[]", string charts = "[]")
        => string.Create(CultureInfo.InvariantCulture, $$"""
            {
              "Cash": {{cash}},
              "Positions": {{positions}},
              "Collateral": {{collateral}},
              "Charts": {{charts}},
              "Stats": {
                "ClosedTrades": {{trades}},
                "Wins": {{wins}},
                "RealizedTotal": {{realized}},
                "FirstFillAt": "2026-08-25T00:00:00Z",
                "LastFillAt": "2026-09-01T00:00:00Z"
              },
              "History": []
            }
            """);

    // ── Opt-in, naming and ordering ──────────────────────────────────────────

    [Fact]
    public async Task Only_opted_in_users_with_a_display_name_are_published()
    {
        var cody = await SeedAsync("lb-cody@example.test", "Cody", optIn: true);
        var ada = await SeedAsync("lb-ada@example.test", "Ada", optIn: true);
        var quiet = await SeedAsync("lb-quiet@example.test", "Quiet", optIn: false);
        var nameless = await SeedAsync("lb-nameless@example.test", null, optIn: true);
        var sneaky = await SeedAsync("lb-sneaky@example.test", "sneaky@example.test", optIn: true);

        WriteAccount(cody, Account(cash: 112_400, trades: 18, wins: 11, realized: 1_240.55));
        WriteAccount(ada, Account(cash: 105_000, trades: 4, wins: 3, realized: 500));
        WriteAccount(quiet, Account(cash: 250_000, trades: 50, wins: 40, realized: 150_000));
        WriteAccount(nameless, Account(cash: 200_000, trades: 20, wins: 10));
        WriteAccount(sneaky, Account(cash: 200_000, trades: 20, wins: 10));

        await PublishAsync();

        var root = JObject.Parse(File.ReadAllText(OutputPath));
        var entries = (JArray)root["entries"]!;
        Assert.Equal(2, entries.Count);

        Assert.Equal(1, (int)entries[0]["rank"]!);
        Assert.Equal("Cody", (string?)entries[0]["displayName"]);
        Assert.Equal(12.4, (double)entries[0]["returnPct"]!, 6);
        Assert.Equal(112_400.0, (double)entries[0]["accountValue"]!, 6);
        Assert.Equal(1_240.55, (double)entries[0]["realizedPnL"]!, 6);
        Assert.Equal(18, (int)entries[0]["tradeCount"]!);
        Assert.Equal(0.6111, (double)entries[0]["winRate"]!, 6);
        Assert.Equal("2026-08-25", (string?)entries[0]["since"]);
        Assert.False((bool)entries[0]["markToMarketStale"]!);

        Assert.Equal(2, (int)entries[1]["rank"]!);
        Assert.Equal("Ada", (string?)entries[1]["displayName"]);
        Assert.Equal(5.0, (double)entries[1]["returnPct"]!, 6);
        Assert.Equal(105_000.0, (double)entries[1]["accountValue"]!, 6);

        string text = File.ReadAllText(OutputPath);
        Assert.DoesNotContain("@", text);
        foreach (var email in new[]
                 {
                     "lb-cody@example.test", "lb-ada@example.test",
                     "lb-quiet@example.test", "lb-nameless@example.test", "lb-sneaky@example.test",
                 })
            Assert.DoesNotContain(email, text);

        Assert.False(string.IsNullOrWhiteSpace((string?)root["generatedAt"]));
        Assert.Equal(100_000.0, (double)root["startingBalance"]!, 6);
    }

    [Fact]
    public async Task Tied_returns_share_a_rank_and_the_next_rank_is_skipped()
    {
        var first = await SeedAsync("lb-tie-a@example.test", "Alpha", optIn: true);
        var second = await SeedAsync("lb-tie-b@example.test", "Beta", optIn: true);
        var third = await SeedAsync("lb-tie-c@example.test", "Gamma", optIn: true);
        var fourth = await SeedAsync("lb-tie-d@example.test", "Delta", optIn: true);

        WriteAccount(first, Account(cash: 110_000, trades: 3));
        WriteAccount(second, Account(cash: 105_000, trades: 2));
        WriteAccount(third, Account(cash: 105_000, trades: 1));
        WriteAccount(fourth, Account(cash: 101_000, trades: 9));

        await PublishAsync();

        var entries = (JArray)JObject.Parse(File.ReadAllText(OutputPath))["entries"]!;
        Assert.Equal(new[] { 1, 2, 2, 4 },
            entries.Select(e => (int)e["rank"]!).ToArray());
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma", "Delta" },
            entries.Select(e => (string?)e["displayName"]).ToArray());
    }

    // ── Damage containment ───────────────────────────────────────────────────

    [Fact]
    public async Task One_malformed_account_does_not_stop_the_others()
    {
        var broken = await SeedAsync("lb-broken@example.test", "Broken", optIn: true);
        var good = await SeedAsync("lb-good@example.test", "Good", optIn: true);

        WriteAccount(broken, "{ this is not json");
        WriteAccount(good, Account(cash: 110_000, trades: 2));

        await PublishAsync();

        var entries = (JArray)JObject.Parse(File.ReadAllText(OutputPath))["entries"]!;
        Assert.Single(entries);
        Assert.Equal("Good", (string?)entries[0]["displayName"]);
    }

    [Fact]
    public async Task The_anon_folder_and_a_nested_folder_are_never_read()
    {
        var user = await SeedAsync("lb-nested@example.test", "Nested", optIn: true);

        // Neither of these is this user's own account file.
        WriteAccount("anon", Account(cash: 999_999, trades: 99, wins: 99));
        string nested = Path.Combine(_host.DataRoot, "users", user.Id, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "paper_account.json"),
            Account(cash: 999_999, trades: 99, wins: 99));

        await PublishAsync();

        var root = JObject.Parse(File.ReadAllText(OutputPath));
        Assert.Empty((JArray)root["entries"]!);
        Assert.NotNull(root["generatedAt"]);   // no rows is still a published file
    }

    // ── The file ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_output_is_written_atomically_and_is_group_readable()
    {
        var user = await SeedAsync("lb-file@example.test", "File", optIn: true);
        WriteAccount(user, Account(cash: 110_000, trades: 1));

        await PublishAsync();
        await PublishAsync();   // overwrite, not append

        string folder = Path.GetDirectoryName(OutputPath)!;
        var files = Directory.GetFiles(folder);
        var only = Assert.Single(files);
        Assert.Equal(OutputPath, only);
        Assert.DoesNotContain(files, f => f.Contains(".tmp", StringComparison.Ordinal));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
                File.GetUnixFileMode(OutputPath));
        }
    }

    [Fact]
    public async Task A_missing_price_publishes_the_entry_value_and_marks_the_row_stale()
    {
        var user = await SeedAsync("lb-stale@example.test", "Stale", optIn: true);
        WriteAccount(user, Account(
            cash: 50_000, trades: 1,
            positions: """[ { "Symbol": "BTC/USDT", "Qty": 1.0, "Avg": 50000.0 } ]""",
            charts: "[]"));

        await PublishAsync();

        var entry = Assert.Single((JArray)JObject.Parse(File.ReadAllText(OutputPath))["entries"]!);
        Assert.True((bool)entry["markToMarketStale"]!);
        Assert.Equal(0.0, (double)entry["returnPct"]!, 6);   // (50000 + 1*50000 - 100000)
        Assert.Equal(100_000.0, (double)entry["accountValue"]!, 6);   // the stale position at what was paid
    }

    // ── Open positions (Cody, 2026-11-20: "be it closed or open position") ──

    [Fact]
    public async Task Open_positions_are_published_with_each_row_and_hold_a_place_before_any_close()
    {
        var holder = await SeedAsync("lb-holder@example.test", "Holder", optIn: true);
        var idle = await SeedAsync("lb-idle@example.test", "Idle", optIn: true);

        // No closed trade yet, a long and a short held. No chart, so no price: the row is
        // published at entry, flagged stale, and the positions say their profit is unknown.
        WriteAccount(holder, Account(
            cash: 40_000, trades: 0, wins: 0,
            positions: """
                [ { "Symbol": "ETH/USDT", "Qty": -2.0, "Avg": 3000.0 },
                  { "Symbol": "BTC/USDT", "Qty": 0.5, "Avg": 60000.0 },
                  { "Symbol": "SOL/USDT", "Qty": 0.0, "Avg": 150.0 } ]
                """,
            collateral: """[ { "Symbol": "ETH/USDT", "Value": 36000.0 } ]"""));
        // Never traded and holds nothing: still left off.
        WriteAccount(idle, Account(cash: 100_000, trades: 0, wins: 0));

        await PublishAsync();

        var entry = Assert.Single((JArray)JObject.Parse(File.ReadAllText(OutputPath))["entries"]!);
        Assert.Equal("Holder", (string?)entry["displayName"]);
        Assert.Equal(0, (int)entry["tradeCount"]!);
        Assert.Equal(JTokenType.Null, entry["winRate"]!.Type);

        var positions = (JArray)entry["openPositions"]!;
        Assert.Equal(2, positions.Count);   // the zero-quantity SOL row is not a position

        Assert.Equal("BTC/USDT", (string?)positions[0]["symbol"]);
        Assert.Equal("long", (string?)positions[0]["side"]);
        Assert.Equal(0.5, (double)positions[0]["quantity"]!, 9);
        Assert.Equal(60_000.0, (double)positions[0]["entryPrice"]!, 9);
        Assert.Equal(JTokenType.Null, positions[0]["markPrice"]!.Type);
        Assert.Equal(JTokenType.Null, positions[0]["unrealizedPnL"]!.Type);
        Assert.Equal(JTokenType.Null, positions[0]["unrealizedPct"]!.Type);

        Assert.Equal("ETH/USDT", (string?)positions[1]["symbol"]);
        Assert.Equal("short", (string?)positions[1]["side"]);
        Assert.Equal(2.0, (double)positions[1]["quantity"]!, 9);
    }

    [Fact]
    public async Task A_row_with_nothing_held_publishes_an_empty_list_not_a_missing_field()
    {
        var user = await SeedAsync("lb-flat@example.test", "Flat", optIn: true);
        WriteAccount(user, Account(cash: 101_000, trades: 3, wins: 2, realized: 1_000));

        await PublishAsync();

        var entry = Assert.Single((JArray)JObject.Parse(File.ReadAllText(OutputPath))["entries"]!);
        Assert.Empty((JArray)entry["openPositions"]!);
    }

    [Fact]
    public void The_hosted_head_registers_the_publisher()
    {
        Assert.Single(_host.Factory.Services.GetServices<IHostedService>()
            .OfType<LeaderboardPublisher>());
    }

    // ── Accounts off ─────────────────────────────────────────────────────────

    [Fact]
    public void Desktop_and_demo_do_not_register_the_publisher()
    {
        using var full = WebHostIntegration.FullFactory();
        using var demo = WebHostIntegration.DemoFactory();

        Assert.DoesNotContain(full.Services.GetServices<IHostedService>(),
            service => service is LeaderboardPublisher);
        Assert.DoesNotContain(demo.Services.GetServices<IHostedService>(),
            service => service is LeaderboardPublisher);
    }
}

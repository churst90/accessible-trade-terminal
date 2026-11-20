using System.Globalization;
using System.Text.Json;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Leaderboard;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.WebHost.Account;
using Microsoft.EntityFrameworkCore;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Publishes the public paper-trading leaderboard for the hosted terminal.
    ///
    /// <para>
    /// One JSON file, rewritten every five minutes from the opted-in users' paper accounts.
    /// Accounts-only: <c>--accounts</c> is the one head with per-user paper accounts and an
    /// Identity database. Desktop and <c>--demo</c> never register it.
    /// </para>
    ///
    /// <para>
    /// The calculator is pure Core code (<see cref="PaperAccountStanding"/>); this class only
    /// gathers the inputs — the users, their files, and one price per traded chart — and writes
    /// the result atomically. A broken account is skipped with its user id in the log, never its
    /// display name or email, so the file is the only public surface.
    /// </para>
    /// </summary>
    public sealed class LeaderboardPublisher : BackgroundService
    {
        public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan PublishInterval = TimeSpan.FromMinutes(5);
        public const int TopCount = 25;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        private readonly IServiceScopeFactory _scopes;
        private readonly string _instanceRoot;
        private readonly string _usersRoot;
        private readonly string _outputPath;
        private readonly TimeProvider _time;
        private readonly ILogger<LeaderboardPublisher> _logger;

        public LeaderboardPublisher(
            IServiceScopeFactory scopes,
            string instanceRoot,
            string outputPath,
            TimeProvider time,
            ILogger<LeaderboardPublisher> logger)
        {
            _scopes = scopes;
            _instanceRoot = instanceRoot;
            _usersRoot = Path.Combine(instanceRoot, "users");
            _outputPath = outputPath;
            _time = time;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            _logger.LogInformation(
                "Leaderboard publisher active (output: {Path}, first run in {Delay}s, then every {Minutes} minutes).",
                _outputPath, StartupDelay.TotalSeconds, PublishInterval.TotalMinutes);

            try { await Task.Delay(StartupDelay, _time, ct); }
            catch (OperationCanceledException) { return; }

            while (!ct.IsCancellationRequested)
            {
                try { await PublishOnceAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // One failed run must never stop the loop: the site's "publisher down"
                    // signal is the stale generatedAt, not a permanently missing file.
                    _logger.LogWarning(ex, "Leaderboard publish failed; retrying next cycle.");
                }

                try { await Task.Delay(PublishInterval, _time, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>One publish. Internal so a test can drive it without waiting five minutes.</summary>
        internal async Task PublishOnceAsync(CancellationToken ct)
        {
            var accounts = new List<(AppUser User, PaperAccountFile File)>();

            foreach (var user in await OptedInUsersAsync(ct))
            {
                ct.ThrowIfCancellationRequested();

                // The account page refuses '@' in a display name and never falls back to
                // UserName or Email. This is the second line of that defence: a row here
                // is public, and an email on it cannot be taken back.
                if (string.IsNullOrWhiteSpace(user.DisplayName) || user.DisplayName.Contains('@'))
                    continue;

                if (!AccountDataFolderGuard.IsSafeUserFolder(
                        _instanceRoot, Path.Combine(_usersRoot, user.Id), out var folder))
                    continue;

                string path = Path.Combine(folder, "paper_account.json");
                if (!File.Exists(path)) continue;

                PaperAccountFile? file;
                try
                {
                    if (!PaperAccountFile.TryParse(await File.ReadAllTextAsync(path, ct), out file)
                        || file == null)
                    {
                        _logger.LogWarning(
                            "Leaderboard skipped a malformed paper account for user {UserId}.", user.Id);
                        continue;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Leaderboard could not read the paper account for user {UserId}.", user.Id);
                    continue;
                }

                // An untouched account would sit at 0% and look like a player. One that holds a
                // position is playing even before its first close (Cody, 2026-11-20: open
                // positions are shown, "be it closed or open position").
                if (file.EffectiveStats().ClosedTrades == 0 && !PositionsOf(file).Any()) continue;

                accounts.Add((user, file));
            }

            var standings = await ComputeAsync(accounts, ct);
            var sorted = standings
                .OrderByDescending(s => s.Standing.ReturnPct)
                .ThenByDescending(s => s.Standing.TradeCount)
                .ThenBy(s => s.User.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(TopCount)
                .ToList();

            var entries = new List<PublishedEntry>(sorted.Count);
            int rank = 0;
            double? previousReturn = null;
            for (int i = 0; i < sorted.Count; i++)
            {
                var (user, standing) = sorted[i];
                double returnPct = Round2(standing.ReturnPct);

                // Ties share a rank when the PUBLISHED return is equal: 1, 2, 2, 4.
                if (i == 0 || returnPct != previousReturn) rank = i + 1;
                previousReturn = returnPct;

                entries.Add(new PublishedEntry(
                    rank,
                    user.DisplayName!,
                    returnPct,
                    Round2(standing.RealizedPnL),
                    standing.TradeCount,
                    standing.WinRate is double rate ? Round4(rate) : null,
                    standing.FirstTradeAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    standing.MarkToMarketStale,
                    standing.OpenPositions.Select(p => new PublishedPosition(
                        p.Symbol,
                        p.IsShort ? "short" : "long",
                        Round8(p.Quantity),
                        Round8(p.Entry),
                        p.Mark is double mark ? Round8(mark) : null,
                        p.UnrealizedPnL is double pnl ? Round2(pnl) : null,
                        p.UnrealizedPct is double pct ? Round2(pct) : null)).ToList()));
            }

            var payload = new PublishedLeaderboard(
                _time.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                PaperTradingProvider.DefaultStartingBalance,
                entries);

            // Always written, even with no entries: the site tells "no one yet" from
            // "publisher down" by generatedAt, which needs a file to read.
            string json = JsonSerializer.Serialize(payload, JsonOptions);
            AtomicFile.WriteAllText(_outputPath, json);

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    // The service runs under a 077 umask and the web server reads the file
                    // through the group; AtomicFile's temp file would otherwise carry 0600.
                    File.SetUnixFileMode(_outputPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Leaderboard wrote {Path} but could not set its mode to 0640.", _outputPath);
                }
            }
        }

        private async Task<List<AppUser>> OptedInUsersAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            return await db.Users.AsNoTracking()
                .Where(u => u.LeaderboardOptIn
                         && u.DisplayName != null
                         && u.DisplayName != "")
                .ToListAsync(ct);
        }

        private async Task<List<(AppUser User, PaperAccountStanding Standing)>> ComputeAsync(
            List<(AppUser User, PaperAccountFile File)> accounts, CancellationToken ct)
        {
            // One fetch per distinct chart per run, shared by every account holding it.
            var prices = new Dictionary<(string Provider, string Market, string Symbol, string Timeframe), double?>();
            var needed = accounts
                .SelectMany(a => PositionsOf(a.File)
                    .Select(p => ChartFor(a.File, p.Symbol))
                    .Where(c => c != null)
                    .Select(c => (c!.Provider, c.Market, c.Symbol, c.Timeframe)))
                .Distinct()
                .ToList();

            if (needed.Count > 0)
            {
                // One shared data scope per run, as the hosted alert monitor does: hosted
                // market data keys are server-seeded and identical for every user.
                using var dataScope = _scopes.CreateScope();
                var data = dataScope.ServiceProvider.GetRequiredService<IDataService>();
                await data.InitializeAsync(
                    dataScope.ServiceProvider.GetRequiredService<IPluginLoaderService>());
                await data.ConfigureStoredKeyProvidersAsync();

                foreach (var key in needed)
                {
                    ct.ThrowIfCancellationRequested();
                    prices[key] = await FetchCloseAsync(data, key, ct);
                }
            }

            var result = new List<(AppUser, PaperAccountStanding)>(accounts.Count);
            foreach (var (user, file) in accounts)
            {
                double? PriceOf(string symbol)
                {
                    var chart = ChartFor(file, symbol);
                    if (chart == null) return null;
                    var key = (chart.Provider, chart.Market, chart.Symbol, chart.Timeframe);
                    return prices.TryGetValue(key, out var price) ? price : null;
                }

                result.Add((user,
                    PaperAccountStanding.Compute(file, PriceOf, PaperTradingProvider.DefaultStartingBalance)));
            }
            return result;
        }

        private async Task<double?> FetchCloseAsync(
            IDataService data,
            (string Provider, string Market, string Symbol, string Timeframe) chart,
            CancellationToken ct)
        {
            try
            {
                var provider = await data.GetProviderAsync(chart.Provider);
                if (provider == null) return null;

                var bars = await BackgroundBarFetch.FetchAsync(
                    provider, new ResamplerService(), chart.Market, chart.Symbol, chart.Timeframe, 1);
                return bars is { Count: > 0 } ? bars[^1].Close : null;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Null, not a guessed price: the row is published with its entry value and
                // markToMarketStale true.
                _logger.LogDebug(ex,
                    "Leaderboard price fetch failed for {Symbol} on {Provider}.", chart.Symbol, chart.Provider);
                return null;
            }
        }

        private static IEnumerable<PaperAccountPosition> PositionsOf(PaperAccountFile file)
            => (file.Positions ?? new List<PaperAccountPosition>())
                .Where(p => p != null && Math.Abs(p.Qty) >= 1e-12);

        private static PaperAccountChart? ChartFor(PaperAccountFile file, string symbol)
            => (file.Charts ?? new List<PaperAccountChart>()).FirstOrDefault(
                c => c != null && string.Equals(c.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

        private static double Round2(double value)
        {
            double rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
            return rounded == 0 ? 0.0 : rounded;   // never publish "-0"
        }

        /// <summary>Prices and quantities: enough places for a coin worth a fraction of a cent.</summary>
        private static double Round8(double value)
        {
            double rounded = Math.Round(value, 8, MidpointRounding.AwayFromZero);
            return rounded == 0 ? 0.0 : rounded;
        }

        private static double Round4(double value)
        {
            double rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
            return rounded == 0 ? 0.0 : rounded;
        }

        private sealed record PublishedLeaderboard(
            string GeneratedAt, double StartingBalance, List<PublishedEntry> Entries);

        private sealed record PublishedEntry(
            int Rank,
            string DisplayName,
            double ReturnPct,
            double RealizedPnL,
            int TradeCount,
            double? WinRate,
            string? Since,
            bool MarkToMarketStale,
            List<PublishedPosition> OpenPositions);

        /// <summary>A held position. <c>MarkPrice</c>, <c>UnrealizedPnL</c> and
        /// <c>UnrealizedPct</c> are null when no price could be fetched.</summary>
        private sealed record PublishedPosition(
            string Symbol,
            string Side,
            double Quantity,
            double EntryPrice,
            double? MarkPrice,
            double? UnrealizedPnL,
            double? UnrealizedPct);
    }
}

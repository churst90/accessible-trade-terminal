using System.Globalization;
using System.Reflection;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>The shared provider infrastructure: timestamps, symbol casing, and the socket.</b>
    ///
    /// <para>
    /// Four defects that live in <c>AccessibleTrader.Sdk</c> and are therefore wrong for every
    /// provider at once. They are pinned here rather than in a per-provider suite because that
    /// is the level at which they are true.
    /// </para>
    /// </summary>
    public class TimestampParserTests
    {
        /// <summary>US Eastern, chosen because its offset is non-zero on every date.</summary>
        private static readonly TimeZoneInfo Eastern =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");

        /// <summary>
        /// A <c>Local</c> DateTime is CONVERTED, not relabelled.
        ///
        /// <para>
        /// <c>SpecifyKind(dt, Utc)</c> stamps "UTC" onto a wall-clock reading, which moves the
        /// bar by the machine's offset in the wrong direction: a 12:00 Eastern reading became
        /// 12:00Z, five hours in the FUTURE, instead of 17:00Z. Newtonsoft's <c>JObject</c>
        /// hands back exactly this Kind for an ISO string carrying a non-zero offset.
        /// </para>
        ///
        /// <para>
        /// <b>The zone is passed in.</b> This box and both CI agents run UTC, so an assertion
        /// against <c>ToUniversalTime()</c> would compare zero against zero and pass against the
        /// bug itself. The offset below is fixed and non-zero by construction.
        /// </para>
        /// </summary>
        [Fact]
        public void A_local_datetime_is_converted_to_utc_not_relabelled()
        {
            var local = DateTime.SpecifyKind(new DateTime(2026, 1, 15, 12, 0, 0), DateTimeKind.Local);

            var parsed = TimestampParser.Parse(local, Eastern);

            // January: Eastern is UTC-5, so noon local is 17:00Z.
            Assert.Equal(new DateTime(2026, 1, 15, 17, 0, 0, DateTimeKind.Utc), parsed);
            Assert.Equal(DateTimeKind.Utc, parsed.Kind);
            // The relabelling bug returns the same wall-clock reading. This is the assertion
            // that actually fails when it comes back, and it cannot be satisfied by an
            // implementation that only changes the Kind.
            Assert.NotEqual(local.TimeOfDay, parsed.TimeOfDay);
        }

        [Fact]
        public void An_unspecified_datetime_is_still_read_as_utc()
        {
            // The venue convention this fleet reads. Nothing better is available, and changing
            // it would move every bar from every provider that hands back a bare DateTime.
            var unspecified = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);

            var parsed = TimestampParser.Parse(unspecified, Eastern);

            Assert.Equal(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc), parsed);
        }

        [Fact]
        public void A_utc_datetime_passes_through_unchanged()
        {
            var utc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            Assert.Equal(utc, TimestampParser.Parse(utc, Eastern));
        }

        /// <summary>
        /// The "invalid" sentinel is a CONSTANT.
        ///
        /// <para>
        /// It was <c>DateTime.MinValue.ToUniversalTime()</c>, which converts from the machine's
        /// zone — so it read 0001-01-01T05:00:00Z on a US-Eastern box and clamped east of
        /// Greenwich. Callers compare against it (<c>OandaProvider</c> does, on the live-tick
        /// path), and a sentinel whose value depends on where the terminal is running cannot be
        /// compared against.
        /// </para>
        /// </summary>
        [Fact]
        public void The_invalid_sentinel_is_a_constant_not_a_local_conversion()
        {
            Assert.Equal(DateTime.MinValue, TimestampParser.Invalid);
            Assert.Equal(DateTimeKind.Utc, TimestampParser.Invalid.Kind);
            Assert.Equal(TimestampParser.Invalid, TimestampParser.Parse(null));
            Assert.Equal(TimestampParser.Invalid, TimestampParser.Parse("not a timestamp at all"));
        }

        /// <summary>
        /// Every epoch tier lands in the right century.
        ///
        /// <para>
        /// The nanosecond case is the one that was broken: the comment claimed to handle
        /// "Nano or Micro seconds" and the body divided by 1000 exactly once, so 1.75e18 became
        /// 1.75e15, was read as milliseconds, and dated the bar to roughly the year 57000.
        /// Latent — no provider feeds nanoseconds today — but the comment told the next author
        /// it was covered.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData("1750000000")]              // seconds
        [InlineData("1750000000000")]           // milliseconds
        [InlineData("1750000000000000")]        // microseconds
        [InlineData("1750000000000000000")]     // nanoseconds
        public void Every_epoch_precision_tier_lands_on_the_same_instant(string epoch)
        {
            var parsed = TimestampParser.Parse(epoch);

            // 1750000000 seconds is 2025-06-15T15:06:40Z. Each tier must reach it, not a
            // date thousands of years out.
            Assert.Equal(new DateTime(2025, 6, 15, 15, 6, 40, DateTimeKind.Utc), parsed);
        }
    }

    /// <summary>
    /// <b>Symbols are cased invariantly, everywhere they go to the wire.</b>
    ///
    /// <para>
    /// Under <c>tr-TR</c> the dotless-i rule turns <c>"link/usd"</c> into <c>"LİNKUSD"</c> — a
    /// different string from the one every other path produces. <c>CleanSymbol</c> is the symbol
    /// Binance calls go to the wire with and the default <c>GetCanonicalSymbol</c>, which the
    /// paper ledger keys positions on, so under a Turkish locale one market becomes two.
    /// </para>
    ///
    /// <para>
    /// The item filed ONE site. The recount found <b>fifteen</b> across the SDK and the plugin
    /// set — every one on a symbol or a timeframe string — which is why the scan below exists
    /// alongside the behavioural test: a single fixed call site does not stop the next one.
    /// </para>
    /// </summary>
    public class InvariantSymbolCasingTests
    {
        private sealed class ProbeProvider : AccessibleTrader.Sdk.Plugins.BaseMarketDataProvider
        {
            public override string Name => "Probe";
            public override string Description => "casing probe";
            public override List<AccessibleTrader.Sdk.Enums.MarketType> SupportedMarkets => new();
            public override bool RequiresApiKey => false;
            public override bool SupportsSymbolSearch => false;
            public override AccessibleTrader.Sdk.Plugins.ProviderEnvironment Environment
                => AccessibleTrader.Sdk.Plugins.ProviderEnvironment.Paper;
            public override bool IsConfigured => true;
            public override bool SupportsLiveUpdates => false;
            public override int MaxBarsPerRequest => 100;
            public override List<string> NativelySupportedTimeframes => new() { "1d" };
            public override void Configure(Dictionary<string, string> config) { }
            public override Task<(bool IsValid, string Message)> ValidateApiKeyAsync() => Task.FromResult((true, ""));
            public override Task EnsureConnectedAsync() => Task.CompletedTask;
            public override Task SetSubscriptionAsync(string m, string s, string t) => Task.CompletedTask;
            public override Task DisconnectAsync() => Task.CompletedTask;
            public override Task<List<string>> GetAvailableSymbolsAsync(AccessibleTrader.Sdk.Enums.MarketType m, string s = "Spot") => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedSubTypesAsync(AccessibleTrader.Sdk.Enums.MarketType m) => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(new List<string>());
            public override Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest r)
                => Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));
            public override Task<(List<AccessibleTrader.Sdk.Models.OrderBookEntry> Bids, List<AccessibleTrader.Sdk.Models.OrderBookEntry> Asks)> GetOrderBookAsync(string s, int l = 10)
                => Task.FromResult((new List<AccessibleTrader.Sdk.Models.OrderBookEntry>(), new List<AccessibleTrader.Sdk.Models.OrderBookEntry>()));

            public string Clean(string symbol) => CleanSymbol(symbol);
        }

        [Fact]
        public void CleanSymbol_produces_the_same_string_under_a_turkish_locale()
        {
            var probe = new ProbeProvider();
            var original = CultureInfo.CurrentCulture;
            try
            {
                // CurrentCulture is async-local in .NET, so this does not leak to other tests
                // the way a process-wide static would.
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

                // The dotless-i rule: tr-TR uppercases "i" to "İ", not "I".
                Assert.Equal("LINKUSD", probe.Clean("link/usd"));
                Assert.Equal("BTCUSD", probe.Clean("btc-usd"));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        /// <summary>
        /// No culture-sensitive casing anywhere in the SDK or the plugin set.
        ///
        /// <para>
        /// The behavioural test above covers one call site. This covers the other fourteen, and
        /// every one somebody writes next — because the failure is invisible on a machine whose
        /// locale happens to be English, which is every machine any of this was tested on.
        /// </para>
        /// </summary>
        [Fact]
        public void No_culture_sensitive_casing_in_the_sdk_or_the_plugins()
        {
            var offenders = new List<string>();
            foreach (var file in ProviderSourceFiles.SdkAndPlugins())
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();
                    // Comments describing the OLD code are how these fixes are documented.
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!line.Contains(".ToUpper()", StringComparison.Ordinal)
                        && !line.Contains(".ToLower()", StringComparison.Ordinal)) continue;
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {trimmed}");
                }
            }

            Assert.True(offenders.Count == 0,
                "Culture-sensitive casing on a wire string. Use ToUpperInvariant/ToLowerInvariant:\n"
                + string.Join("\n", offenders));
        }
    }

    /// <summary>
    /// <b>The reconnecting socket: one writer at a time, and giving up is a disconnect.</b>
    /// </summary>
    public class ReconnectingWebSocketContractTests
    {
        private static string SocketSource() =>
            File.ReadAllText(ProviderSourceFiles.SdkFile("Services", "ReconnectingWebSocket.cs"));

        /// <summary>
        /// Every write to the socket goes through the one locked path.
        ///
        /// <para>
        /// <c>ClientWebSocket.SendAsync</c> throws on an overlapping call, and this class has two
        /// independent writers: the heartbeat timer and whatever caller is sending a subscribe.
        /// Kraken's heartbeat is 30 s, so a symbol switch lands on it routinely over a session —
        /// and the symptom is a subscribe that silently never went and a chart that never
        /// updates.
        /// </para>
        ///
        /// <para>
        /// This is a source check because <c>ClientWebSocket</c> is sealed and the failure needs
        /// two writes genuinely in flight against a live socket. What it pins is the property
        /// that matters: there is exactly one call site, and it holds the lock. A test that
        /// merely asserted <c>_sendLock</c> appears in the file would stay green if a new method
        /// wrote to <c>_ws</c> directly.
        /// </para>
        /// </summary>
        [Fact]
        public void There_is_exactly_one_socket_write_and_it_holds_the_send_lock()
        {
            string src = SocketSource();

            // One raw write, in SendFrameAsync.
            int rawWrites = System.Text.RegularExpressions.Regex.Matches(
                src, @"\bws\.SendAsync\(|\b_ws\.SendAsync\(|\b_ws!\.SendAsync\(").Count;
            Assert.True(rawWrites == 1,
                $"ReconnectingWebSocket must have exactly ONE socket write; found {rawWrites}. "
                + "Every writer has to go through SendFrameAsync or the heartbeat can collide with it.");

            int frameStart = src.IndexOf("private async Task SendFrameAsync", StringComparison.Ordinal);
            Assert.True(frameStart >= 0, "SendFrameAsync is gone — the single write path has been removed.");
            int writeAt = src.IndexOf("ws.SendAsync(", frameStart, StringComparison.Ordinal);
            int lockAt = src.IndexOf("_sendLock.WaitAsync(", frameStart, StringComparison.Ordinal);
            Assert.True(lockAt >= 0 && writeAt > lockAt,
                "The socket write must happen after _sendLock.WaitAsync inside SendFrameAsync.");
            Assert.Contains("_sendLock.Release()", src.AsSpan(frameStart).ToString(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Giving up permanently reports a DISCONNECT, not just an error.
        ///
        /// <para>
        /// On the last consecutive failure the receive loop returns for good.
        /// <c>_onDisconnected</c> was never called on that path, so
        /// <c>ConnectionStateStream</c> stayed at <c>Connected</c> over a permanently dead feed:
        /// the UI said the provider was fine and the chart simply stopped. Kraken's main socket
        /// and every keyed feed take the default ten attempts — about four minutes of outage.
        /// </para>
        /// </summary>
        [Fact]
        public void The_give_up_path_invokes_on_disconnected()
        {
            string src = SocketSource();
            int giveUp = src.IndexOf("if (reconnectAttempts >= _maxReconnectAttempts)", StringComparison.Ordinal);
            Assert.True(giveUp >= 0, "The give-up branch is gone; this guard no longer covers anything.");

            // Look only at the branch body, up to its `return`.
            int returnAt = src.IndexOf("return;", giveUp, StringComparison.Ordinal);
            Assert.True(returnAt > giveUp, "The give-up branch no longer returns.");
            string branch = src[giveUp..returnAt];

            Assert.Contains("_onError?.Invoke", branch, StringComparison.Ordinal);
            Assert.Contains("_onDisconnected?.Invoke", branch, StringComparison.Ordinal);
        }

        /// <summary>
        /// A retired token source is disposed once its loops have drained.
        ///
        /// <para>
        /// <c>ConnectAsync</c> cancelled the previous CTS and abandoned it, leaking its
        /// registration list — and any armed timer handle — on every reconnect and every symbol
        /// switch. Disposing it inline would race the loops that still hold its token, which is
        /// why the drain is what makes the dispose safe.
        /// </para>
        /// </summary>
        [Fact]
        public async Task Retiring_a_generation_disposes_its_token_source_after_the_loops_exit()
        {
            var ws = new AccessibleTrader.Sdk.Services.ReconnectingWebSocket("ws://127.0.0.1:9/");
            var retire = typeof(AccessibleTrader.Sdk.Services.ReconnectingWebSocket)
                .GetMethod("RetirePreviousGeneration", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(retire);

            var type = typeof(AccessibleTrader.Sdk.Services.ReconnectingWebSocket);
            var ctsField = type.GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var cts = new CancellationTokenSource();
            ctsField.SetValue(ws, cts);

            // A live loop task, so the DRAIN path is what disposes — the path that actually
            // exists in production. Without it the method takes its "nothing to wait for"
            // shortcut and the continuation this is about never runs at all.
            var loopRunning = new TaskCompletionSource();
            type.GetField("_receiveLoopTask", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(ws, loopRunning.Task);

            retire!.Invoke(ws, null);

            // Still alive while the loop is: disposing under a running loop is the race the
            // drain exists to avoid.
            cts.Token.Register(() => { }).Dispose();

            loopRunning.SetResult();
            for (int i = 0; i < 100 && !IsDisposed(cts); i++) await Task.Delay(10);

            Assert.True(IsDisposed(cts), "The retired token source was never disposed.");
            Assert.Null(ctsField.GetValue(ws));

            ws.Dispose();

            static bool IsDisposed(CancellationTokenSource source)
            {
                try { source.Token.Register(() => { }).Dispose(); return false; }
                catch (ObjectDisposedException) { return true; }
            }
        }
    }

    /// <summary>Locates provider and SDK source files for the scan guards above.</summary>
    internal static class ProviderSourceFiles
    {
        /// <summary>Walks up from the test binary to the repository root.</summary>
        internal static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AccessibleTrader.Sdk")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the repository root from the test binary.");
            return dir!.FullName;
        }

        internal static string SdkFile(params string[] parts) =>
            Path.Combine(new[] { RepoRoot(), "AccessibleTrader.Sdk" }.Concat(parts).ToArray());

        internal static IEnumerable<string> SdkAndPlugins()
        {
            string root = RepoRoot();
            foreach (var dir in new[] { Path.Combine(root, "AccessibleTrader.Sdk"), Path.Combine(root, "Plugins") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
                    if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
                    yield return f;
                }
            }
        }

        internal static string ProviderFile(string tier, string plugin, string file) =>
            Path.Combine(RepoRoot(), "Plugins", tier, $"AccessibleTrader.Plugins.{plugin}", file);
    }
}

using System.Collections.Concurrent;
using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Screening;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Screening;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.MarketPulse
{
    /// <summary>
    /// Alt+P: answer "how is the market right now?" in one sentence.
    ///
    /// <para>
    /// <b>The list is a watchlist named "Pulse".</b> It is seeded once from
    /// <see cref="MarketPulseDefaults"/>, edited in the existing watchlist screen, and never
    /// re-sorted. Held positions are appended for this reading only and are not written into the
    /// saved list.
    /// </para>
    ///
    /// <para>
    /// <b>One deadline for every quote.</b> All items are fetched at once, with at most four
    /// requests in flight per provider, under a single four-second
    /// <see cref="CancellationTokenSource"/>. An item that misses the deadline or throws is
    /// "unavailable"; nothing is said after the sentence, so a late answer is dropped rather than
    /// updating a sentence the user has already heard.
    /// </para>
    /// </summary>
    public sealed class MarketPulseService
    {
        private const int MaxRequestsPerProvider = 4;
        private static readonly TimeSpan FetchLimit = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan GatherNoticeAfter = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan HoldingsLimit = TimeSpan.FromSeconds(2);

        private readonly IWatchlistLibrary _watchlists;
        private readonly IDataService _data;
        private readonly IOrderExecutionService _orders;
        private readonly IPaperTradingProvider _paper;
        private readonly IApiKeyService _apiKeys;
        private readonly ISettingsManager _settings;
        private readonly IEventBus _bus;
        private readonly TimeProvider _time;
        private readonly ILogger<MarketPulseService>? _logger;

        private int _gathering;

        public MarketPulseService(
            IWatchlistLibrary watchlists,
            IDataService data,
            IOrderExecutionService orders,
            IPaperTradingProvider paper,
            IApiKeyService apiKeys,
            ISettingsManager settings,
            IEventBus bus,
            TimeProvider? time = null,
            ILogger<MarketPulseService>? logger = null)
        {
            _watchlists = watchlists;
            _data = data;
            _orders = orders;
            _paper = paper;
            _apiKeys = apiKeys;
            _settings = settings;
            _bus = bus;
            _time = time ?? TimeProvider.System;
            _logger = logger;
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        public async Task SpeakAsync(WorkspaceState state)
        {
            if (Interlocked.CompareExchange(ref _gathering, 1, 0) != 0)
            {
                Publish("Still gathering the pulse.", interrupt: true);
                return;
            }

            ITimer? gatherTimer = null;
            int spoken = 0;
            try
            {
                gatherTimer = _time.CreateTimer(_ =>
                {
                    if (Volatile.Read(ref spoken) == 0)
                        Publish("Gathering the pulse.", interrupt: false);
                }, null, GatherNoticeAfter, Timeout.InfiniteTimeSpan);

                var (entries, seeded, leftOut) = await BuildListAsync(state).ConfigureAwait(false);
                var quotes = await FetchQuotesAsync(entries, state).ConfigureAwait(false);

                if (Volatile.Read(ref spoken) == 0)
                {
                    string sentence = MarketPulseWording.Sentence(quotes, NowUtc);
                    if (seeded && leftOut.Count > 0)
                    {
                        sentence += " Left out, no data source: "
                                 + string.Join(", ", leftOut)
                                 + ". Add them in the watchlist screen, Alt+M.";
                    }
                    Interlocked.Exchange(ref spoken, 1);
                    gatherTimer.Dispose();
                    gatherTimer = null;
                    Publish(sentence, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Market pulse failed: {Type}.", ex.GetType().Name);
                if (Volatile.Read(ref spoken) == 0)
                {
                    Interlocked.Exchange(ref spoken, 1);
                    Publish("The market pulse is unavailable.", interrupt: true);
                }
            }
            finally
            {
                gatherTimer?.Dispose();
                Volatile.Write(ref _gathering, 0);
            }
        }

        private void Publish(string message, bool interrupt)
            => _bus.Publish(new AnnouncementEvent(
                message, interrupt, Channel: SpeechChannel.Interface));

        // ── The list ────────────────────────────────────────────────────────

        private async Task<(IReadOnlyList<WatchlistEntry> Entries, bool Seeded,
            IReadOnlyList<string> LeftOut)> BuildListAsync(WorkspaceState state)
        {
            var existing = _watchlists.All.FirstOrDefault(w =>
                string.Equals(w.Name, "Pulse", StringComparison.OrdinalIgnoreCase));

            bool seeded = false;
            IReadOnlyList<string> leftOut = Array.Empty<string>();
            IReadOnlyList<WatchlistEntry> entries;

            if (existing == null)
            {
                seeded = true;
                var providers = await BuildProviderCatalogAsync().ConfigureAwait(false);
                var resolved = MarketPulseDefaults.ResolveAll(providers);
                entries = resolved.Entries;
                leftOut = resolved.LeftOut;

                var list = Watchlist.Create("Pulse") with { Entries = entries };
                _watchlists.Upsert(list);
            }
            else
            {
                entries = existing.Entries;
            }

            // The holdings read is held to its own short limit: a slow broker must not hold up
            // the sentence. Missed holdings are left out of this reading.
            var heldRead = ReadHeldEntriesAsync(state);
            using (var heldLimit = new CancellationTokenSource(HoldingsLimit, _time))
            {
                var waited = Task.Delay(Timeout.InfiniteTimeSpan, heldLimit.Token);
                await Task.WhenAny(heldRead, waited).ConfigureAwait(false);
            }
            var held = heldRead.IsCompletedSuccessfully ? heldRead.Result : new List<WatchlistEntry>();
            if (held.Count == 0) return (entries, seeded, leftOut);

            var combined = entries.ToList();
            foreach (var candidate in held)
            {
                bool already = combined.Any(e =>
                    ProviderNames.Match(e.Provider, candidate.Provider)
                    && string.Equals(e.Symbol, candidate.Symbol, StringComparison.OrdinalIgnoreCase));
                if (!already) combined.Add(candidate);
            }
            return (combined, seeded, leftOut);
        }

        private async Task<IReadOnlyList<MarketPulseProvider>> BuildProviderCatalogAsync()
        {
            List<string> names;
            try
            {
                names = await _data.LoadProvidersAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Market pulse could not list providers: {Type}.", ex.GetType().Name);
                return Array.Empty<MarketPulseProvider>();
            }

            var neededMarkets = MarketPulseDefaults.Items
                .SelectMany(i => i.Markets)
                .Distinct()
                .ToList();

            var providers = new List<MarketPulseProvider>();
            foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool usable;
                try
                {
                    bool configured = _data.IsProviderConfigured(name);
                    bool needsKey = await _data.ProviderRequiresApiKeyAsync(name).ConfigureAwait(false);
                    usable = configured || !needsKey;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("Market pulse could not inspect {Provider}: {Type}.",
                        name, ex.GetType().Name);
                    continue;
                }
                if (!usable) continue;

                List<MarketType> supported;
                try
                {
                    supported = await _data.GetSupportedMarketsForProviderAsync(name)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("Market pulse could not read {Provider}'s markets: {Type}.",
                        name, ex.GetType().Name);
                    supported = new List<MarketType>();
                }

                var symbolsByMarket = new Dictionary<MarketType, IReadOnlyList<string>>();
                var subTypeByMarket = new Dictionary<MarketType, string>();
                foreach (var market in neededMarkets)
                {
                    if (!supported.Contains(market)) continue;
                    var subTypes = new List<string>();
                    try
                    {
                        subTypes = await _data.GetSupportedSubTypesAsync(name, market.ToString())
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(
                            "Market pulse could not read {Provider}'s sub-types: {Type}.",
                            name, ex.GetType().Name);
                    }
                    string subType = subTypes.Count > 0 ? subTypes[0] : "Spot";

                    // The watchlist screen's exact key: the market alone for a one-sub-type
                    // provider, otherwise "Market|SubType".
                    string marketInfo = subTypes.Count > 1 ? $"{market}|{subType}" : market.ToString();

                    try
                    {
                        symbolsByMarket[market] = await _data.LoadSymbolsAsync(marketInfo, name)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug("Market pulse could not read {Provider}'s symbols: {Type}.",
                            name, ex.GetType().Name);
                        symbolsByMarket[market] = Array.Empty<string>();
                    }
                    subTypeByMarket[market] = subType;
                }

                providers.Add(new MarketPulseProvider(
                    name,
                    Usable: true,
                    supported,
                    market => symbolsByMarket.TryGetValue(market, out var symbols) ? symbols : null,
                    market => subTypeByMarket.TryGetValue(market, out var subType) ? subType : "Spot"));
            }
            return providers;
        }

        // ── Holdings ─────────────────────────────────────────────────────────

        private async Task<List<WatchlistEntry>> ReadHeldEntriesAsync(WorkspaceState state)
        {
            var result = new List<WatchlistEntry>();
            var chartIdentities = (state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty)
                .Select(t => t.Identity)
                .Append(state.Identity)
                .ToList();

            bool paper = _settings.GetSetting(SettingsKeys.PaperTradingMode)?.ToObject<bool>() ?? false;
            try
            {
                if (paper)
                {
                    var rows = await _paper.GetPositionsAsync().ConfigureAwait(false);
                    var identities = chartIdentities.Concat(_paper.ExposedIdentities()).ToList();
                    foreach (var position in rows)
                    {
                        if (!IsHeld(position.Quantity)) continue;
                        if (Match(identities, position.Symbol) is not { } identity) continue;
                        if (MarketOf(identity.Market) is not { } market) continue;
                        result.Add(new WatchlistEntry(
                            identity.Provider,
                            identity.Symbol,
                            market,
                            SubTypeOf(identity.Market)));
                    }
                }
                else
                {
                    List<ApiKeyConfig> keys;
                    try
                    {
                        keys = await _apiKeys.GetAllKeysAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(
                            "Market pulse could not enumerate accounts: {Type}.",
                            ex.GetType().Name);
                        keys = new List<ApiKeyConfig>();
                    }

                    foreach (string provider in keys
                        .Where(k => k.IsActive && !k.AllowsWithdrawal)
                        .Select(k => k.Provider)
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (!await _orders.SupportsTradingAsync(provider).ConfigureAwait(false))
                                continue;
                            var read = await _orders.GetPositionsAsync(provider).ConfigureAwait(false);
                            if (!read.IsOk) continue;

                            foreach (var position in read.OrElse(new List<Position>()))
                            {
                                if (!IsHeld(position.Quantity)) continue;
                                if (Match(chartIdentities, position.Symbol) is not { } identity)
                                    continue;
                                if (MarketOf(identity.Market) is not { } market) continue;
                                result.Add(new WatchlistEntry(
                                    provider,
                                    position.Symbol,
                                    market,
                                    SubTypeOf(identity.Market)));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(
                                "Market pulse positions for {Provider} failed: {Type}.",
                                provider, ex.GetType().Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Market pulse could not read positions: {Type}.",
                    ex.GetType().Name);
            }

            return result;
        }

        private static bool IsHeld(double quantity)
            => quantity != 0 && double.IsFinite(quantity);

        private static ChartIdentity? Match(
            IReadOnlyList<ChartIdentity> identities, string symbol)
        {
            foreach (var identity in identities)
                if (string.Equals(identity.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    return identity;
            return null;
        }

        private static MarketType? MarketOf(string? market)
        {
            string category = MarketKey.Category(market ?? "");
            return Enum.TryParse<MarketType>(category, ignoreCase: true, out var parsed)
                ? parsed
                : null;
        }

        private static string SubTypeOf(string? market)
        {
            string subType = MarketKey.SubType(market ?? "");
            return subType.Length > 0 ? subType : "Spot";
        }

        // ── Quotes ──────────────────────────────────────────────────────────

        private async Task<IReadOnlyList<MarketPulseQuote>> FetchQuotesAsync(
            IReadOnlyList<WatchlistEntry> entries, WorkspaceState state)
        {
            var start = MarketPulseSettings.CryptoStart(state);
            var gates = new ConcurrentDictionary<string, SemaphoreSlim>(
                StringComparer.OrdinalIgnoreCase);
            using var deadline = new CancellationTokenSource(FetchLimit, _time);
            var token = deadline.Token;
            var timedOut = Task.Delay(Timeout.InfiniteTimeSpan, token);

            var work = entries
                .Select(entry => FetchOneAsync(entry, start, gates, token))
                .ToList();

            var quotes = new List<MarketPulseQuote>(entries.Count);
            for (int i = 0; i < work.Count; i++)
            {
                var quote = new MarketPulseQuote(
                    MarketPulseDefaults.SpokenName(entries[i]),
                    MarketPulseKind.Unavailable,
                    IsYield: MarketPulseDefaults.IsYield(entries[i]));
                try
                {
                    var completed = await Task.WhenAny(work[i], timedOut).ConfigureAwait(false);
                    if (completed != timedOut)
                        quote = await work[i].ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The common deadline, not a failure worth naming.
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("Market pulse quote failed: {Type}.", ex.GetType().Name);
                }
                quotes.Add(quote);
            }

            deadline.Cancel();
            return quotes;
        }

        private async Task<MarketPulseQuote> FetchOneAsync(
            WatchlistEntry entry,
            PulseCryptoStart cryptoStart,
            ConcurrentDictionary<string, SemaphoreSlim> gates,
            CancellationToken token)
        {
            var gate = gates.GetOrAdd(entry.Provider,
                _ => new SemaphoreSlim(MaxRequestsPerProvider, MaxRequestsPerProvider));

            bool last24 = entry.Market == MarketType.Crypto
                          && cryptoStart == PulseCryptoStart.Last24Hours;
            string primaryTimeframe = last24 ? "1h" : "1d";
            int primaryLimit = last24 ? 30 : 3;

            try
            {
                var primary = FetchAsync(entry, primaryTimeframe, primaryLimit, gate, token);
                var intraday = FetchIntradayAsync(entry, gate, token);
                await Task.WhenAll(primary, intraday).ConfigureAwait(false);

                return MarketPulseMath.Quote(
                    entry,
                    primary.Result,
                    intraday.Result,
                    NowUtc,
                    cryptoStart);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Market pulse fetch failed for {Provider}: {Type}.",
                    entry.Provider, ex.GetType().Name);
                return new MarketPulseQuote(
                    MarketPulseDefaults.SpokenName(entry),
                    MarketPulseKind.Unavailable,
                    IsYield: MarketPulseDefaults.IsYield(entry));
            }
        }

        /// <summary>
        /// The minute bars only sharpen a quote (age, last price, a holiday); the daily bars carry
        /// the change. A provider without one-minute bars, or a minute fetch that fails, leaves
        /// the item on its daily bars instead of making it "unavailable".
        /// </summary>
        private async Task<IReadOnlyList<Ohlcv>> FetchIntradayAsync(
            WatchlistEntry entry, SemaphoreSlim gate, CancellationToken token)
        {
            try
            {
                return await FetchAsync(entry, "1m", 3, gate, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Market pulse minute bars failed for {Provider}: {Type}.",
                    entry.Provider, ex.GetType().Name);
                return Array.Empty<Ohlcv>();
            }
        }

        private async Task<IReadOnlyList<Ohlcv>> FetchAsync(
            WatchlistEntry entry,
            string timeframe,
            int limit,
            SemaphoreSlim gate,
            CancellationToken token)
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var request = new MarketDataRequest(entry.SubType, entry.Symbol, timeframe, limit);
                var (bars, _) = await _data
                    .FetchOhlcvAsync(entry.Provider, request, token)
                    .ConfigureAwait(false);
                return bars ?? new List<Ohlcv>();
            }
            finally
            {
                gate.Release();
            }
        }
    }
}

using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services.MyData
{
    /// <summary>Marker for built-in (non-plugin) data providers that the startup
    /// path registers into the DataService after plugin loading.</summary>
    public interface IBuiltInDataProvider : IMarketDataProvider { }

    /// <summary>
    /// The "My Data" market: user-imported CSV datasets served through the same
    /// provider contract as any exchange. Symbols are dataset names (OHLCV files)
    /// or "dataset — column" (value files); shape resolves PER SYMBOL so an OHLCV
    /// import charts as candles and a budget column as a line. Events datasets are
    /// not listed here — they surface as chart markers via MyDataEventsProvider.
    /// </summary>
    public sealed class MyDataProvider : BaseMarketDataProvider, IBuiltInDataProvider
    {
        public const string ProviderName = "My Data";
        private const string ColumnSeparator = " — ";

        private readonly IMyDataStore _store;

        public MyDataProvider(IMyDataStore store) => _store = store;

        public override string Name => ProviderName;
        public override string Description => "Your own imported CSV files — charts, series, and event markers.";
        public override List<MarketType> SupportedMarkets => new() { MarketType.MyData };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey => false;
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest => CsvDataParser.MaxRows;
        public override List<string> NativelySupportedTimeframes =>
            new() { "1m", "1h", "1d", "1w", "1M", "1y" };

        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        public override ProviderDataShape GetDataShapeForSymbol(string symbol) =>
            Resolve(symbol).Dataset?.Shape == MyDataShape.Ohlcv
                ? ProviderDataShape.Ohlcv
                : ProviderDataShape.SingleValueLine;

        public override string GetSymbolDisplayName(string symbol) => symbol;

        public override void Configure(Dictionary<string, string> config) { }
        public override Task EnsureConnectedAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }
        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe)
            => EnsureConnectedAsync(); // no live stream — data is at rest
        public override Task DisconnectAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            var symbols = new List<string>();
            foreach (var ds in _store.Datasets)
            {
                switch (ds.Shape)
                {
                    case MyDataShape.Ohlcv:
                        symbols.Add(ds.Name);
                        break;
                    case MyDataShape.Values:
                        // One loadable symbol per column; single-column files list
                        // under the plain dataset name.
                        if (ds.Columns.Count == 1) symbols.Add(ds.Name);
                        else symbols.AddRange(ds.Columns.Select(c => ds.Name + ColumnSeparator + c));
                        break;
                    // Events are markers, not charts — see MyDataEventsProvider.
                }
            }
            return Task.FromResult(symbols);
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Spot" });

        /// <summary>
        /// Every native spacing across every imported dataset — a UNION, because this interface
        /// method takes no symbol and so cannot know which dataset is being charted.
        ///
        /// <para>
        /// That means the dropdown offers a timeframe the selected dataset may not have: import
        /// one daily file and one monthly file and both appear for either. There is no
        /// resampling here, so a mismatch cannot be honoured — <see cref="FetchOhlcvAsync"/>
        /// refuses it and says which spacing the dataset actually is, rather than handing back
        /// daily bars for a chart that has been told they are monthly.
        /// </para>
        /// </summary>
        public override Task<List<string>> GetSupportedTimeframesAsync()
        {
            var frames = _store.Datasets.Select(d => d.Timeframe).Distinct().ToList();
            return Task.FromResult(frames.Count > 0 ? frames : new List<string> { "1d" });
        }

        public override Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)>
            FetchOhlcvAsync(MarketDataRequest request)
        {
            var (dataset, column) = Resolve(request.Symbol);
            if (dataset == null)
                return Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));

            // A dataset has ONE spacing and this provider does not resample. The timeframe
            // dropdown is the union across every import (see GetSupportedTimeframesAsync), so a
            // user with a daily file and a monthly file is offered "1M" while charting the daily
            // one — and picking it used to return the daily bars anyway, placed on a chart that
            // had been told they were monthly. Refuse, and name the spacing this dataset has:
            // that is the fact that lets someone pick a timeframe that works.
            if (!string.IsNullOrEmpty(request.Timeframe) &&
                !string.Equals(request.Timeframe, dataset.Timeframe, StringComparison.OrdinalIgnoreCase))
            {
                _errorStream.OnNext(
                    $"\"{dataset.Name}\" was imported at {dataset.Timeframe} spacing and cannot be shown at "
                    + $"{request.Timeframe} — this data is not resampled.");
                return Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));
            }

            var parsed = _store.GetParsed(dataset.Id);
            if (parsed == null)
                return Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));

            List<Ohlcv> bars = parsed.Shape switch
            {
                MyDataShape.Ohlcv => parsed.Bars.ToList(),
                MyDataShape.Values => BarsFromColumn(parsed, column),
                _ => new List<Ohlcv>(),
            };

            // Honour the request window (backfill asks with Since/Until).
            if (request.Since is long since)
            {
                var sinceDate = DateTimeOffset.FromUnixTimeMilliseconds(since).UtcDateTime;
                bars = bars.Where(b => b.Date >= sinceDate).ToList();
            }
            if (request.Until is long until)
            {
                var untilDate = DateTimeOffset.FromUnixTimeMilliseconds(until).UtcDateTime;
                bars = bars.Where(b => b.Date <= untilDate).ToList();
            }
            if (request.Limit > 0 && bars.Count > request.Limit)
                bars = bars.TakeLast(request.Limit).ToList();

            var volume = bars.Select(b =>
                (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
            return Task.FromResult((bars, volume));
        }

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>
            GetOrderBookAsync(string symbol, int limit = 10)
            => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        // ── Symbol resolution ────────────────────────────────────────────────

        internal (MyDataset? Dataset, string? Column) Resolve(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return (null, null);

            // Exact dataset-name match first (OHLCV files, single-column files).
            var direct = _store.Datasets.FirstOrDefault(d =>
                d.Name.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            if (direct != null)
                return (direct, direct.Shape == MyDataShape.Values ? direct.Columns.FirstOrDefault() : null);

            // "dataset — column" form.
            int sep = symbol.LastIndexOf(ColumnSeparator, StringComparison.Ordinal);
            if (sep < 0) return (null, null);
            string dsName = symbol[..sep];
            string column = symbol[(sep + ColumnSeparator.Length)..];
            var ds = _store.Datasets.FirstOrDefault(d =>
                d.Name.Equals(dsName, StringComparison.OrdinalIgnoreCase));
            return ds == null ? (null, null) : (ds, column);
        }

        private static List<Ohlcv> BarsFromColumn(ParsedDataset parsed, string? column)
        {
            column ??= parsed.Columns.FirstOrDefault();
            if (column == null || !parsed.ColumnData.TryGetValue(column, out var values))
                return new List<Ohlcv>();

            var bars = new List<Ohlcv>(parsed.Dates.Count);
            for (int i = 0; i < parsed.Dates.Count; i++)
            {
                double v = values[i];
                if (double.IsNaN(v)) continue; // gaps are simply absent bars
                bars.Add(new Ohlcv(parsed.Dates[i], v, v, v, v, 0));
            }
            return bars;
        }
    }
}

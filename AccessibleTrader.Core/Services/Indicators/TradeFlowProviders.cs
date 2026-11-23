using System.Globalization;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// True buy and sell volume, counted from the exchange's own trades (Cody, 2026-11-23: "if it
    /// can be useful for any exchange, yes"). Two indicators:
    ///
    /// <list type="bullet">
    /// <item><b>Buy and Sell Volume (trades)</b>: each bar's volume split by the side that took it,
    /// buying drawn up from zero and selling down.</item>
    /// <item><b>Volume Delta (trades)</b>: buying minus selling, added up bar by bar from the first
    /// bar counted, and back to zero after a bar that could not be counted. Crossing zero is said:
    /// buyers or sellers have taken the lead.</item>
    /// </list>
    ///
    /// <para>
    /// Unlike <see cref="CumulativeVolumeDeltaProvider"/>, which signs each bar's volume by its
    /// candle, these read every trade, on any venue whose plugin streams them with the taker's
    /// side (<see cref="ITradeStreamProvider"/>). The venues send no history, so a bar is counted
    /// only if the chart was open, and the stream up, from its first second; the bars before are
    /// blank, and so is a bar a dropped connection fell in.
    /// </para>
    /// </summary>
    public sealed class TradeFlowProvider : IIndicatorProvider
    {
        public const string VolumeCode = "TRADE_VOLUME";
        public const string DeltaCode = "TRADE_DELTA";
        public const string CompBuy = "Buy";
        public const string CompSell = "Sell";
        public const string CompDelta = "Delta";

        private readonly ITradeFlowService _flow;

        /// <summary>Why the last calculation's bars are blank, for the detail fact, which is not
        /// handed the chart's venue. Null when bars are being counted.</summary>
        private volatile string? _lastBlank;

        public TradeFlowProvider(ITradeFlowService flow) => _flow = flow;

        public string Name => "Core.TradeFlow";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = VolumeCode, Name = "Buy and Sell Volume (trades)", Category = "Volume",
                DefaultPane = "Pane_TradeVolume",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Each bar's volume split into buying and selling, counted from the exchange's own trades: " +
                    "a trade is buying when the buyer took the ask. Crypto exchanges only, and only from when " +
                    "the chart opened; there is no history.",
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompBuy, DisplayName = "Buy Volume", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#26A69A", DefaultReferenceLevel = 0.0,
                            SpeechTemplate = "Buy volume {value:F0}." },
                    new() { Name = CompSell, DisplayName = "Sell Volume", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#EF5350", DefaultReferenceLevel = 0.0,
                            SpeechTemplate = "Sell volume {value:F0}." },
                },
            },
            new IndicatorMetadata
            {
                Code = DeltaCode, Name = "Volume Delta (trades)", Category = "Volume",
                DefaultPane = "Pane_TradeDelta",
                Causality = ComponentCausality.Causal,
                RequiresFullRecalcOnTick = true,
                Description =
                    "Buying volume minus selling volume from the exchange's own trades, added up from the first " +
                    "bar counted. Above zero, buyers have taken more since then. Crypto exchanges only, from when " +
                    "the chart opened.",
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompDelta, DisplayName = "Trade Delta", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350",
                            DefaultColorSource = ColorSource.Value, DefaultReferenceLevel = 0.0,
                            SubscribedLevelNames = new[] { "Zero" },
                            SpeechTemplate = "Trade delta {value:F0}." },
                },
            },
        };

        public List<LevelDescriptor> GetDefaultLevels(string code)
            => string.Equals(code, DeltaCode, StringComparison.OrdinalIgnoreCase) ? VolumeFlowMath.ZeroLevel() : new();

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var (buy, sell) = Split(data, parameters, out var blank);
            _lastBlank = blank;
            if (string.Equals(code, DeltaCode, StringComparison.OrdinalIgnoreCase))
            {
                var span = buffer.GetComponentSpan(CompDelta);
                var delta = Accumulate(buy, sell);
                for (int i = 0; i < data.Length; i++) span[i] = delta[i];
            }
            else
            {
                var b = buffer.GetComponentSpan(CompBuy);
                var s = buffer.GetComponentSpan(CompSell);
                for (int i = 0; i < data.Length; i++)
                {
                    b[i] = buy[i];
                    s[i] = double.IsNaN(sell[i]) ? double.NaN : -sell[i];   // drawn down from zero
                }
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 1;

        /// <summary>
        /// Each bar's buying and selling, NaN where it was not counted. A bar runs from its own
        /// start to the next bar's; the last one, still forming, to one bar step on.
        /// <paramref name="blank"/> says why nothing was counted, or is null when something was.
        /// </summary>
        internal (double[] Buy, double[] Sell) Split(ReadOnlySpan<Ohlcv> data, Dictionary<string, object>? parameters, out string? blank)
        {
            int n = data.Length;
            var buy = new double[n];
            var sell = new double[n];
            Array.Fill(buy, double.NaN);
            Array.Fill(sell, double.NaN);

            string provider = Hint(parameters, "__provider");
            string symbol = Hint(parameters, "__symbol");
            string market = Hint(parameters, "__market");
            if (provider.Length == 0 || symbol.Length == 0)
            {
                blank = "Buy and sell volume is counted from a live chart's trades, and this one has none.";
                return (buy, sell);
            }

            var tape = _flow.Watch(market, provider, symbol);
            TimeSpan step = VolumeFlowMath.BarStep(data);
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                DateTime to = i + 1 < n ? data[i + 1].Date : data[i].Date + step;
                var sum = tape.Sum(data[i].Date, to);
                if (sum is not { } v) continue;
                buy[i] = v.Buy;
                sell[i] = v.Sell;
                any = true;
            }
            blank = any ? null : WhyBlank(tape, n > 0 ? data[^1].Date : default);
            return (buy, sell);
        }

        private static string WhyBlank(TradeTape tape, DateTime lastBar) => tape.State switch
        {
            TradeTape.TapeState.Unsupported =>
                $"{tape.Provider} does not send which side took each trade, so there is no buy and sell volume for this chart.",
            TradeTape.TapeState.Failed =>
                $"The trade stream from {tape.Provider} could not be opened. It is tried again on the next update.",
            TradeTape.TapeState.Connecting =>
                $"Connecting to {tape.Provider}'s trades. Counting starts with the next bar.",
            TradeTape.TapeState.Dropped =>
                $"The trade stream from {tape.Provider} dropped. Counting starts again with the bar after it reconnects.",
            _ => tape.CountingSince is { } since
                ? $"Counting {tape.Provider}'s trades since {SpeechTimeFormatter.ToBarDisplay(since, 60):HH:mm}. The first bar counted is the first one that started after that."
                : $"Counting starts with the next bar.",
        };

        /// <summary>Buying minus selling, added up from the first counted bar; a bar that was not
        /// counted is NaN and the bar after it starts from zero again.</summary>
        internal static double[] Accumulate(double[] buy, double[] sell)
        {
            var delta = new double[buy.Length];
            double run = 0;
            bool counting = false;
            for (int i = 0; i < buy.Length; i++)
            {
                if (double.IsNaN(buy[i]) || double.IsNaN(sell[i]))
                {
                    delta[i] = double.NaN;
                    counting = false;
                    continue;
                }
                if (!counting) { run = 0; counting = true; }
                run += buy[i] - sell[i];
                delta[i] = run;
            }
            return delta;
        }

        private static string Hint(Dictionary<string, object>? p, string key)
            => p != null && p.TryGetValue(key, out var raw) ? Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() ?? "" : "";

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            if (string.Equals(code, DeltaCode, StringComparison.OrdinalIgnoreCase))
            {
                double d = At(calculatedResults, CompDelta, index);
                if (double.IsNaN(d)) return "Trade delta: not counted for this bar. " + (_lastBlank ?? NotCountedWhy);
                return $"Trade delta {VolumeFlowMath.SignedSpoken(d)} since the first bar counted" +
                       (d > 0 ? ": buyers ahead." : d < 0 ? ": sellers ahead." : ": even.");
            }

            double buy = At(calculatedResults, CompBuy, index);
            double sell = -At(calculatedResults, CompSell, index);
            if (double.IsNaN(buy) || double.IsNaN(sell))
                return "Buy and sell volume: not counted for this bar. " + (_lastBlank ?? NotCountedWhy);
            return Spoken(buy, sell);
        }

        private const string NotCountedWhy =
            "It is counted from the exchange's live trades, only for bars that started after the chart opened.";

        /// <summary>"Buy 12, sell 8: buyers took 60 percent."</summary>
        internal static string Spoken(double buy, double sell)
        {
            double total = buy + sell;
            string text = $"Buy {QuantityFormatter.FormatSpoken(buy)}, sell {QuantityFormatter.FormatSpoken(sell)}";
            if (total <= 0) return text + ": no trades.";
            double share = buy / total * 100;
            return share >= 50
                ? $"{text}: buyers took {Math.Round(share).ToString(CultureInfo.InvariantCulture)} percent."
                : $"{text}: sellers took {Math.Round(100 - share).ToString(CultureInfo.InvariantCulture)} percent.";
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            switch (componentName)
            {
                case CompBuy or "Buy Volume":
                {
                    double sell = -At(allComponentData, CompSell, dataIndex);
                    return double.IsNaN(sell) ? $"Buy volume {QuantityFormatter.FormatSpoken(value)}." : Spoken(value, sell);
                }
                case CompSell or "Sell Volume":
                {
                    double buy = At(allComponentData, CompBuy, dataIndex);
                    return double.IsNaN(buy) ? $"Sell volume {QuantityFormatter.FormatSpoken(-value)}." : Spoken(buy, -value);
                }
                case CompDelta or "Trade Delta":
                    return $"Trade delta {VolumeFlowMath.SignedSpoken(value)}{(value > 0 ? ", buyers ahead." : value < 0 ? ", sellers ahead." : ".")}";
                default:
                    return null;
            }
        }

        private static double At(IReadOnlyDictionary<string, double[]> data, string key, int index)
            => data.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length ? arr[index] : double.NaN;
    }
}

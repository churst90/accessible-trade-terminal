using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Feeds
{
    /// <summary>
    /// Go bar <c>FUND</c>: the chart's perpetual as its venue publishes it right now, funding,
    /// open interest, mark against index and the day's volume, in one sentence. Read from the
    /// venue each time (<see cref="IPerpetualContextProvider"/>); nothing is kept.
    /// </summary>
    public sealed class FundingNowService
    {
        private readonly IDataService _data;
        private readonly IEventBus _bus;
        private readonly ILogger<FundingNowService>? _logger;
        private int _reading;

        public FundingNowService(IDataService data, IEventBus bus, ILogger<FundingNowService>? logger = null)
        {
            _data = data;
            _bus = bus;
            _logger = logger;
        }

        public async Task SpeakAsync(WorkspaceState state)
        {
            if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
            {
                Publish("Still reading funding.");
                return;
            }
            try
            {
                Publish(await SentenceAsync(state.Identity).ConfigureAwait(false));
            }
            finally
            {
                Volatile.Write(ref _reading, 0);
            }
        }

        /// <summary>The sentence for one chart; never throws.</summary>
        public async Task<string> SentenceAsync(ChartIdentity chart)
        {
            if (string.IsNullOrEmpty(chart.Symbol)) return "No chart is open.";
            IPerpetualContextProvider? source;
            try
            {
                var provider = await _data.GetProviderAsync(chart.Provider).ConfigureAwait(false);
                source = provider as IPerpetualContextProvider ?? provider?.GetCapability<IPerpetualContextProvider>();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Funding now: provider lookup failed: {Type}.", ex.GetType().Name);
                source = null;
            }
            if (source is null)
                return $"{chart.Provider} does not give the terminal its current funding and open interest. " +
                       "Hyperliquid does.";

            PerpetualContext? ctx;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                ctx = await source.GetPerpetualContextAsync(chart.Symbol, cts.Token)
                    .WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Funding now failed: {Type}.", ex.GetType().Name);
                return $"Funding and open interest could not be read from {chart.Provider}.";
            }
            return ctx is null
                ? $"{chart.Symbol} is not a perpetual future on {chart.Provider}, so it has no funding or open interest."
                : Sentence(ctx, chart.Provider);
        }

        /// <summary>
        /// The words. Funding is said per 8 hours whatever the venue's period, the way the Funding
        /// Rate indicator shows it, with how often it is paid and what it comes to over a year.
        /// </summary>
        public static string Sentence(PerpetualContext c, string venue)
        {
            var parts = new List<string> { $"{c.Symbol} on {venue}, now." };

            double hours = c.FundingIntervalHours > 0 ? c.FundingIntervalHours : 8;
            double perHour = c.FundingRate / hours;
            double per8 = perHour * 8 * 100, perYear = perHour * 24 * 365 * 100;
            string who = c.FundingRate > 0 ? "longs pay shorts" : c.FundingRate < 0 ? "shorts pay longs" : "nobody pays";
            string paid = hours == 1 ? "paid every hour" : $"paid every {Num(hours, "0.#")} hours";
            parts.Add(c.FundingRate == 0
                ? $"Funding zero, {paid}."
                : $"Funding {Num(Math.Abs(per8), "0.0###")} percent per 8 hours, {who}, {paid}, " +
                  $"about {Num(Math.Abs(perYear), Math.Abs(perYear) < 10 ? "0.#" : "0")} percent a year.");

            string unit = BaseOf(c.Symbol);
            string oi = $"Open interest {QuantityFormatter.FormatSpoken(c.OpenInterest)} {unit}";
            if (c.MarkPrice > 0) oi += $", about {Money(c.OpenInterest * c.MarkPrice)}";
            parts.Add(oi + ".");

            if (c.MarkPrice > 0)
            {
                string mark = $"Mark {SpeechPriceFormatter.FormatPrice(c.MarkPrice)}";
                if (c.IndexPrice > 0)
                {
                    double gap = (c.MarkPrice - c.IndexPrice) / c.IndexPrice * 100;
                    mark += $", index {SpeechPriceFormatter.FormatPrice(c.IndexPrice)}, mark " +
                            (Math.Abs(gap) < 0.0005 ? "level with index"
                                : $"{Num(Math.Abs(gap), "0.0##")} percent {(gap > 0 ? "above" : "below")}");
                }
                parts.Add(mark + ".");
            }
            if (c.DayVolumeQuote > 0) parts.Add($"24-hour volume {Money(c.DayVolumeQuote)}.");
            return string.Join(" ", parts);
        }

        /// <summary>"BTC-PERP" is counted in BTC, "xyz:TSLA-PERP" in TSLA.</summary>
        internal static string BaseOf(string symbol)
        {
            string s = symbol;
            int colon = s.IndexOf(':');
            if (colon >= 0) s = s[(colon + 1)..];
            int cut = s.IndexOfAny(new[] { '-', '/' });
            return cut > 0 ? s[..cut] : s;
        }

        private static string Money(double usd) => "$" + QuantityFormatter.FormatSpoken(Math.Round(usd));
        private static string Num(double v, string f) => v.ToString(f, CultureInfo.InvariantCulture);

        private void Publish(string message)
            => _bus.Publish(new AnnouncementEvent(message, true, Channel: SpeechChannel.Interface));
    }
}

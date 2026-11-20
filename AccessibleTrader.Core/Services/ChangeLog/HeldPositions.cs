using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.ChangeLog
{
    /// <summary>One open position as it stood at a moment: what "since you left" compares.</summary>
    public sealed record HeldPositionMark(string Provider, string Symbol, double Quantity, double UnrealizedPnL);

    /// <summary>The open positions when the user left, kept in change-log.json until Alt+U says them.</summary>
    public sealed record ChangeLogAwayPositions(DateTime TimeUtc, List<HeldPositionMark> Positions);

    /// <summary>Every open position the terminal can read now; null when it could not read them.</summary>
    public interface IHeldPositionsReader
    {
        Task<IReadOnlyList<HeldPositionMark>?> ReadAsync();
    }

    /// <summary>
    /// The paper account in paper mode; otherwise every active trading key's venue (never a key
    /// that allows withdrawal), as the market pulse reads holdings. Any venue that fails makes
    /// the whole read null, so "since you left" never compares a partial list and calls the rest
    /// closed.
    /// </summary>
    public sealed class HeldPositionsReader : IHeldPositionsReader
    {
        private readonly IOrderExecutionService _orders;
        private readonly IPaperTradingProvider _paper;
        private readonly IApiKeyService _apiKeys;
        private readonly ISettingsManager _settings;
        private readonly ILogger<HeldPositionsReader>? _logger;

        public HeldPositionsReader(
            IOrderExecutionService orders,
            IPaperTradingProvider paper,
            IApiKeyService apiKeys,
            ISettingsManager settings,
            ILogger<HeldPositionsReader>? logger = null)
        {
            _orders = orders;
            _paper = paper;
            _apiKeys = apiKeys;
            _settings = settings;
            _logger = logger;
        }

        public async Task<IReadOnlyList<HeldPositionMark>?> ReadAsync()
        {
            try
            {
                bool paper = _settings.GetSetting(SettingsKeys.PaperTradingMode)?.ToObject<bool>() ?? false;
                if (paper)
                    return Held("Paper", await _paper.GetPositionsAsync().ConfigureAwait(false));

                var marks = new List<HeldPositionMark>();
                var keys = await _apiKeys.GetAllKeysAsync().ConfigureAwait(false);
                foreach (string provider in keys
                    .Where(k => k.IsActive && !k.AllowsWithdrawal)
                    .Select(k => k.Provider)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!await _orders.SupportsTradingAsync(provider).ConfigureAwait(false)) continue;
                    var read = await _orders.GetPositionsAsync(provider).ConfigureAwait(false);
                    if (!read.IsOk) return null;
                    marks.AddRange(Held(provider, read.OrElse(new List<Position>())));
                }
                return marks;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Open positions could not be read for the catch-up: {Type}.", ex.GetType().Name);
                return null;
            }
        }

        private static List<HeldPositionMark> Held(string provider, IEnumerable<Position> positions)
            => positions
                .Where(p => p.Quantity != 0 && double.IsFinite(p.Quantity))
                .Select(p => new HeldPositionMark(provider, p.Symbol, p.Quantity,
                    double.IsFinite(p.UnrealizedPnL) ? p.UnrealizedPnL : 0))
                .ToList();
    }

    /// <summary>
    /// "Positions since 09:40: BTC/USDT made 42.10, ETH/USDT lost 8.30, SOL/USDT opened, XRP/USDT
    /// closed." The change in each position's unrealized profit, in its venue's own currency,
    /// from when you left to the press. A position whose size changed says so instead: its
    /// profit then and now are not the same trade.
    /// </summary>
    public static class AwayPositionsWording
    {
        /// <summary>Under half a cent is "unchanged".</summary>
        public const double Unchanged = 0.005;

        public static string? Sentence(
            IReadOnlyList<HeldPositionMark>? then, IReadOnlyList<HeldPositionMark>? now, DateTime sinceUtc)
        {
            if (then == null) return null;
            string since = Accessibility.SpeechTimeFormatter.FormatTime(sinceUtc);
            if (now == null)
                return then.Count == 0 ? null : $"Positions since {since} could not be read.";
            if (then.Count == 0 && now.Count == 0) return null;

            var all = then.Concat(now).ToList();
            string Name(HeldPositionMark p)
                => all.Any(o => SameSymbol(o, p) && !SameVenue(o, p))
                    ? $"{p.Symbol} on {p.Provider}"
                    : p.Symbol;

            var parts = new List<string>();
            foreach (var position in now)
            {
                var before = then.FirstOrDefault(t => Same(t, position));
                if (before == null)
                    parts.Add($"{Name(position)} opened");
                else if (!SameSize(before.Quantity, position.Quantity))
                    parts.Add($"{Name(position)} size changed");
                else
                {
                    double change = position.UnrealizedPnL - before.UnrealizedPnL;
                    parts.Add(Math.Abs(change) < Unchanged
                        ? $"{Name(position)} unchanged"
                        : $"{Name(position)} {(change > 0 ? "made" : "lost")} "
                          + Math.Abs(change).ToString("#,0.00", CultureInfo.InvariantCulture));
                }
            }
            foreach (var before in then)
                if (!now.Any(n => Same(n, before)))
                    parts.Add($"{Name(before)} closed");

            return $"Positions since {since}: {string.Join(", ", parts)}.";
        }

        private static bool Same(HeldPositionMark a, HeldPositionMark b)
            => SameVenue(a, b) && SameSymbol(a, b);

        private static bool SameVenue(HeldPositionMark a, HeldPositionMark b)
            => string.Equals(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase);

        private static bool SameSymbol(HeldPositionMark a, HeldPositionMark b)
            => string.Equals(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase);

        private static bool SameSize(double a, double b)
            => Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    }
}

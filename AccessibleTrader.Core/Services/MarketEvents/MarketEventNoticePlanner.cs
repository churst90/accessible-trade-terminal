using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>One thing the live market-events service is due to say or ask for.</summary>
    public enum MarketEventNoticeKind
    {
        /// <summary>"CPI in 15 minutes."</summary>
        Countdown,
        /// <summary>"CPI out. BTC down 0.8% in the first minute."</summary>
        ReadOut,
        /// <summary>Ask FRED for the release figure, and say it if it has arrived.</summary>
        Figure,
    }

    /// <summary>
    /// A notice the planner says is due. <see cref="Minutes"/> is the countdown lead for a
    /// countdown, and the minutes after the event for a figure try; it is 0 for a read-out.
    /// </summary>
    public sealed record MarketEventNotice(
        MarketEventNoticeKind Kind,
        MarketEvent Event,
        int Minutes = 0)
    {
        /// <summary>
        /// The once-per-service identity of this notice. Each (event id, lead) and each read-out
        /// fires once; each figure try is once too.
        /// </summary>
        public string Key => Kind switch
        {
            MarketEventNoticeKind.Countdown => $"countdown|{Minutes}|{Event.Id}",
            MarketEventNoticeKind.ReadOut => $"readout|{Event.Id}",
            _ => $"figure|{Minutes}|{Event.Id}",
        };
    }

    /// <summary>Parsing and wording for the countdown setting, in one place.</summary>
    public static class MarketEventCountdowns
    {
        public const string Off = "off";
        public const string Default = "15,1";

        /// <summary>The shipped default: 15 minutes and 1 minute.</summary>
        public static readonly IReadOnlyList<int> DefaultLeads = new[] { 15, 1 };

        /// <summary>
        /// The leads in the setting, longest first. "off" (or an empty string) means none.
        /// An unrecognised value falls back to <see cref="DefaultLeads"/> rather than to
        /// silence: a corrupted settings file must not turn the countdowns off.
        /// </summary>
        public static IReadOnlyList<int> Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<int>();
            if (string.Equals(value.Trim(), Off, StringComparison.OrdinalIgnoreCase))
                return Array.Empty<int>();

            var leads = new List<int>();
            foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out int minutes) && minutes > 0 && !leads.Contains(minutes))
                    leads.Add(minutes);
            }
            if (leads.Count == 0) return DefaultLeads;
            leads.Sort((a, b) => b.CompareTo(a));
            return leads;
        }

        /// <summary>The setting string for a set of leads, longest first.</summary>
        public static string ToSetting(IEnumerable<int> leads)
        {
            var ordered = leads.Where(m => m > 0).Distinct().OrderByDescending(m => m).ToList();
            return ordered.Count == 0 ? Off : string.Join(",", ordered);
        }

        /// <summary>The choice as a screen reader should say it.</summary>
        public static string Label(IEnumerable<int> leads)
        {
            var ordered = leads.Where(m => m > 0).Distinct().OrderByDescending(m => m).Select(m => m.ToString()).ToList();
            return ordered.Count switch
            {
                0 => "Off",
                1 => $"{ordered[0]} minute{(ordered[0] == "1" ? "" : "s")}",
                _ => string.Join(", ", ordered.Take(ordered.Count - 1)) + " and " + ordered[^1] + " minutes",
            };
        }
    }

    /// <summary>
    /// Decides which live notices are due at <paramref name="nowUtc"/>. Pure and static: no clock,
    /// no bus, no I/O, and "what has already fired" is supplied by the caller.
    ///
    /// <para>
    /// <b>Windows, so nothing stale is ever said.</b> A countdown lead L fires only in
    /// [T − L, T − L + 20 s); the read-out fires only in [T + 60 s, T + 90 s); a figure try at
    /// minute M fires only in [T + M, T + M + 20 s). A window that passed while the app slept is
    /// skipped, never said late.
    /// </para>
    /// </summary>
    public static class MarketEventNoticePlanner
    {
        /// <summary>How long a countdown or figure-try window stays open.</summary>
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

        /// <summary>How long the read-out window stays open after T + 60 s.</summary>
        public static readonly TimeSpan ReadOutWindow = TimeSpan.FromSeconds(30);

        /// <summary>The minutes after the event at which the release figure is asked for.</summary>
        public static readonly IReadOnlyList<int> FigureTries = new[] { 2, 5, 15, 30, 60 };

        public static IReadOnlyList<MarketEventNotice> Due(
            DateTime nowUtc,
            IReadOnlyList<MarketEvent> events,
            IReadOnlyList<int> leadsMinutes,
            IReadOnlyCollection<MarketType> openMarkets,
            IReadOnlyCollection<MarketType> heldMarkets,
            MarketEventLevel level,
            MarketEventKindFilter kinds,
            IReadOnlyCollection<string> firedKeys,
            bool fredKeyAvailable)
        {
            if (level == MarketEventLevel.Off || events.Count == 0)
                return Array.Empty<MarketEventNotice>();

            var markets = new HashSet<MarketType>(openMarkets);
            foreach (var market in heldMarkets) markets.Add(market);
            if (markets.Count == 0) return Array.Empty<MarketEventNotice>();

            var now = AsUtc(nowUtc);
            var fired = new HashSet<string>(firedKeys, StringComparer.Ordinal);
            var due = new List<MarketEventNotice>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var e in events)
            {
                // Day-only events have no moment to count down to and no release minute.
                if (e.Precision != MarketEventPrecision.Exact) continue;
                if (!MarketEventLevels.Includes(level, e.Importance)) continue;
                if (!kinds.Includes(e.Kind)) continue;
                if (!e.Scope.Any(markets.Contains)) continue;

                var moment = AsUtc(e.MomentUtc);

                foreach (int lead in leadsMinutes)
                {
                    if (lead <= 0) continue;
                    var start = moment.AddMinutes(-lead);
                    if (now < start || now >= start + Window) continue;
                    Add(new MarketEventNotice(MarketEventNoticeKind.Countdown, e, lead));
                }

                var readOut = moment.AddSeconds(60);
                if (now >= readOut && now < readOut + ReadOutWindow)
                    Add(new MarketEventNotice(MarketEventNoticeKind.ReadOut, e));

                if (fredKeyAvailable && e.Kind == MarketEventKind.Economy
                    && IsFredRelease(e) && e.Figures == null)
                {
                    foreach (int minute in FigureTries)
                    {
                        var start = moment.AddMinutes(minute);
                        if (now < start || now >= start + Window) continue;
                        Add(new MarketEventNotice(MarketEventNoticeKind.Figure, e, minute));
                    }
                }
            }

            due.Sort((a, b) =>
            {
                int byMoment = a.Event.MomentUtc.CompareTo(b.Event.MomentUtc);
                if (byMoment != 0) return byMoment;
                int byKind = a.Kind.CompareTo(b.Kind);
                if (byKind != 0) return byKind;
                return b.Minutes.CompareTo(a.Minutes);
            });
            return due;

            void Add(MarketEventNotice notice)
            {
                if (fired.Contains(notice.Key) || !seen.Add(notice.Key)) return;
                due.Add(notice);
            }
        }

        /// <summary>True for the four FRED release sources the seed and feed build.</summary>
        public static bool IsFredRelease(MarketEvent e)
            => e.Source.StartsWith("FRED release ", StringComparison.Ordinal);

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}

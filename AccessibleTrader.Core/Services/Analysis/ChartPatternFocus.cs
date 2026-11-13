using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// Which of several overlapping formations the user has chosen to hear about.
    ///
    /// <para>
    /// A region can satisfy four definitions at once, and the terminal ranks them by size because
    /// that is the only tie-break available which is not a directional opinion. But size is not
    /// always what a trader cares about: the twelve-bar flag inside the eighty-bar triangle may be
    /// exactly the thing their setup is built on, and having the terminal insist on naming the
    /// triangle every time is the software substituting its ordering for their thesis.
    /// </para>
    ///
    /// <para>
    /// Choosing solves that without the application acquiring an opinion. The user says "this one",
    /// and it leads while they are on its bars. Nothing is scored, nothing is hidden — the others are
    /// still counted and still readable with the detail key.
    /// </para>
    ///
    /// <para>
    /// <b>The choice is sticky (Cody, 2026-11-13).</b> It began as a pin that also confined
    /// comma and period to the pinned formation's two edges, so after choosing a double top on
    /// TAO/USDT 1h the jump keys could reach nothing else ("I thought ; just cycled between the
    /// possible chart patterns"). The confinement went, and the choice was then dropped on the
    /// first bar the chosen formation was not on — so Home and End lost it, and he asked for the
    /// opposite: "it should be sticky until I intentionally change it. if I close the terminal and
    /// reopen it, that choice should stay selected. It should stay regardless of where I navigate
    /// to." Now only semicolon changes it, it is saved to settings.json per chart
    /// (<see cref="SettingsKeys.ChartFormationChoices"/>), and on bars the chosen formation is not
    /// on the size ranking reads as usual while the choice waits.
    /// </para>
    ///
    /// <para>
    /// The choice is held as a <see cref="ChartPattern.Identity"/> rather than a record, because the
    /// record is re-derived on every bar (the narrator projects each formation to the state it held
    /// at the bar being described) while the Identity is stable across those projections.
    /// </para>
    ///
    /// <para>
    /// It used to be the <see cref="ChartPattern.Key"/>, which is built from bar INDICES and so is
    /// not stable across a re-detection that moves them. A live feed at its growth cap sheds its
    /// oldest bar on every new one, and a history backfill prepends; either shifts every index on
    /// the chart, the pinned Key matched nothing afterwards, and the pin was silently gone — the
    /// next semicolon started over and said "2 of 2" again. Measured on the BTC/USDT daily
    /// snapshot at the 2,000-bar live cap. The Identity is the formation's kind and its two dates,
    /// which no index shift touches.
    /// </para>
    /// </summary>
    public interface IChartPatternFocus
    {
        /// <summary>Reorder so the pinned formation leads, if one is pinned and present.</summary>
        IReadOnlyList<ChartPattern> Apply(string chartKey, IReadOnlyList<ChartPattern> ranked);

        /// <summary>
        /// Choose the next formation in the ranked order (the previous one when
        /// <paramref name="forward"/> is false), wrapping around. Counted from the one currently
        /// read: the chosen formation if it is in <paramref name="ranked"/>, otherwise
        /// <paramref name="ranked"/>[<paramref name="reading"/>] — the formation a landing on the
        /// bar names (<see cref="ChartPatternNarrator.Landing"/>). Returns the newly chosen
        /// pattern, or null when there is nothing at this bar.
        /// </summary>
        ChartPattern? CycleAt(string chartKey, IReadOnlyList<ChartPattern> ranked, bool forward = true, int reading = 0);

        /// <summary>Clear the pin for this chart. Returns true if one was actually cleared.</summary>
        bool Clear(string chartKey);

        /// <summary>Whether this chart currently has a pinned formation.</summary>
        bool IsPinned(string chartKey);

        /// <summary>
        /// The pinned formation as it appears among <paramref name="candidates"/>, or null when
        /// nothing is pinned or the pinned shape is not in that set.
        ///
        /// <para>
        /// Null is not a reason to drop the choice: the user is only somewhere the chosen
        /// formation is not. (It once scoped the jump keys to the pinned formation's two edges,
        /// and later dropped the choice on the first bar off it; see the type's remarks.)
        /// </para>
        /// </summary>
        ChartPattern? PinnedIn(string chartKey, IEnumerable<ChartPattern> candidates);
    }

    public sealed class ChartPatternFocus : IChartPatternFocus
    {
        // Per chart, for the same reason the detection cache is per chart: a pin is a statement
        // about one instrument's structure and means nothing on another.
        private readonly Dictionary<string, (ChartPatternKind, DateTime, DateTime)> _pinned = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private readonly ISettingsManager? _settings;
        private bool _loaded;

        public ChartPatternFocus() { }

        /// <param name="settings">Where the choices are kept across restarts; none in tests that
        /// only need the in-memory behaviour.</param>
        public ChartPatternFocus(ISettingsManager? settings) { _settings = settings; }

        // Read on first use rather than in the constructor: on the hosted head the settings file is
        // resolved against the signed-in user, who is not known while DI is building.
        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            if (_settings?.GetSetting(SettingsKeys.ChartFormationChoices) is not JObject saved) return;
            foreach (var (chart, value) in saved)
            {
                // A choice that does not parse is skipped, never thrown: the worst it may cost is
                // the size ranking on that one chart.
                if (value is not JObject o
                    || !Enum.TryParse<ChartPatternKind>((string?)o["kind"], out var kind)
                    || o["start"]?.Type != JTokenType.Integer || o["end"]?.Type != JTokenType.Integer)
                    continue;
                _pinned[chart] = (kind, new DateTime((long)o["start"]!, DateTimeKind.Utc), new DateTime((long)o["end"]!, DateTimeKind.Utc));
            }
        }

        // Ticks, not a date string: Identity compares ticks, and a round trip through a formatted
        // date (or a reader that shifts it to local time) is how a saved choice would stop matching
        // the formation it names.
        private void Save()
        {
            if (_settings == null) return;
            var all = new JObject();
            foreach (var (chart, (kind, start, end)) in _pinned)
                all[chart] = new JObject { ["kind"] = kind.ToString(), ["start"] = start.Ticks, ["end"] = end.Ticks };
            _settings.SetSetting(SettingsKeys.ChartFormationChoices, all);
            _settings.SaveSettings();
        }

        public IReadOnlyList<ChartPattern> Apply(string chartKey, IReadOnlyList<ChartPattern> ranked)
        {
            if (ranked.Count <= 1) return ranked;

            lock (_gate)
            {
                EnsureLoaded();
                if (!_pinned.TryGetValue(chartKey, out var key)) return ranked;

                int i = -1;
                for (int n = 0; n < ranked.Count; n++)
                    if (ranked[n].Identity.Equals(key)) { i = n; break; }

                // A chosen formation that is not at this bar is not an error and must not clear the
                // choice: the user is simply somewhere else on the chart, and walking back should
                // find it still in force.
                if (i <= 0) return ranked;

                var reordered = new List<ChartPattern>(ranked.Count) { ranked[i] };
                for (int n = 0; n < ranked.Count; n++)
                    if (n != i) reordered.Add(ranked[n]);
                return reordered;
            }
        }

        public ChartPattern? CycleAt(string chartKey, IReadOnlyList<ChartPattern> ranked, bool forward = true, int reading = 0)
        {
            if (ranked.Count == 0) return null;

            lock (_gate)
            {
                EnsureLoaded();

                // Counted from the formation the readout is already naming. Starting from "none"
                // made the first press choose the leader and say "1 of 2": the formation the user
                // had just heard, so the key appeared to do nothing. With no choice at this bar
                // that is the landing's formation (the caller's reading), wherever the choice is.
                int current = reading >= 0 && reading < ranked.Count ? reading : 0;
                if (_pinned.TryGetValue(chartKey, out var key))
                    for (int n = 0; n < ranked.Count; n++)
                        if (ranked[n].Identity.Equals(key)) { current = n; break; }

                int next = ((forward ? current + 1 : current - 1) % ranked.Count + ranked.Count) % ranked.Count;
                _pinned[chartKey] = ranked[next].Identity;
                Save();
                return ranked[next];
            }
        }

        public bool Clear(string chartKey)
        {
            lock (_gate)
            {
                EnsureLoaded();
                if (!_pinned.Remove(chartKey)) return false;
                Save();
                return true;
            }
        }

        public bool IsPinned(string chartKey)
        {
            lock (_gate) { EnsureLoaded(); return _pinned.ContainsKey(chartKey); }
        }

        public ChartPattern? PinnedIn(string chartKey, IEnumerable<ChartPattern> candidates)
        {
            lock (_gate)
            {
                EnsureLoaded();
                if (!_pinned.TryGetValue(chartKey, out var key)) return null;
                foreach (var c in candidates)
                    if (c.Identity.Equals(key)) return c;
                return null;
            }
        }
    }
}

using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Input
{
    /// <summary>
    /// Decides where the <c>0</c> shortcut puts a new reference level.
    ///
    /// <para>
    /// ── The defect this exists to prevent ──────────────────────────────────────
    /// The command used to add a level at <b>literal zero</b> on whatever series held focus, and it
    /// was named "Zero line" because it was written for oscillators, where a zero crossing is the
    /// thing you want a line at. Press the same key with the <i>price</i> series focused and you get
    /// a level at 0 on a chart trading near 64,000.
    /// </para>
    ///
    /// <para>
    /// That is not merely useless. <c>ViewportRangeCalculator</c> expands the price range to cover
    /// every visible level on a main-pane series, so a level at zero drags the y-axis to the origin
    /// and compresses all price action into the top sliver of the pane. The level persists in the
    /// workspace, so the chart comes back broken at every launch — which is exactly how it was
    /// found: a maintainer screenshot of a BTC 4h chart whose axis ran 0 → 70,000, with the offending
    /// entry still sitting in <c>__last-session__.json</c> as
    /// <c>{"Name":"Zero","Value":0.0,"IsVisible":true}</c> on the <c>CANDLES</c> series.
    /// </para>
    ///
    /// <para>
    /// ── The rule ───────────────────────────────────────────────────────────────
    /// A reference level must be in the units of the pane it lands on. On a price pane there is no
    /// meaningful constant at all — so the level goes at <b>the price under the cursor</b>, which
    /// is both well-defined and what someone pressing "mark a level" on a price chart actually
    /// wants.
    /// </para>
    ///
    /// <para>
    /// ── The same defect, one level down (2026-09-06) ───────────────────────────
    /// "On an oscillator pane the meaningful constant is zero" was the original rule, and it is
    /// only true of oscillators that swing about zero. RSI runs 0–100 and swings about 50; so do
    /// Stochastic, Stoch RSI, MFI and the Ultimate Oscillator. Williams %R runs −100…0 and swings
    /// about −50. Pressing <c>0</c> on any of them put a line named "Zero" at the very floor of the
    /// pane — a value RSI does not visit, so a line that could never be crossed, could never fire
    /// its earcon, and could never be navigated to. Right units, wrong constant: exactly the shape
    /// of the price-pane bug that this class was created to fix.
    /// </para>
    ///
    /// <para>
    /// The pane's neutral is not guessed here. It is <see cref="ComponentConfig.ReferenceLevel"/>,
    /// which the indicator factory already populates for every component from provider metadata,
    /// then <c>StylingService.GetReferenceLevel</c> (RSI→50, Stoch→50, MACD→0, Williams %R→−50),
    /// then 0 for oscillator and zero-area display types. It is the field the audio layer already
    /// splits above/below waveforms on, so "the line this component swings about" was declared
    /// across the whole provider fleet before this key ever needed it.
    /// </para>
    /// </summary>
    public static class ReferenceLevelPlacement
    {
        /// <summary>Panes whose values are prices, and therefore have no meaningful zero.</summary>
        public static bool IsPricePane(string? pane) =>
            string.IsNullOrEmpty(pane) || pane.Equals("Main", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// How close an existing level has to be, as a fraction of the value, for the key to treat it
        /// as "the one you meant" and remove it.
        ///
        /// <para>
        /// Proportional rather than absolute because the terminal charts both BTC near 64,000 and
        /// sub-cent tokens; a fixed tolerance would be either uselessly tight on one or absurdly wide
        /// on the other. A quarter of a percent is comfortably inside one bar's range on any
        /// timeframe, so arrowing to a bar and pressing the key twice removes what it just added,
        /// while a level you placed on a different bar is left alone.
        /// </para>
        /// </summary>
        internal const double RemoveTolerance = 0.0025;

        /// <summary>
        /// Finds the level the key should remove, if pressing it again means "take that one back".
        ///
        /// <para>
        /// Only <b>user-defined</b> levels are candidates. A provider's overbought line is part of
        /// what the indicator is, and silently deleting it because the cursor happened to sit at 70
        /// would be a considerable surprise; those are managed in Properties.
        /// </para>
        /// </summary>
        public static LevelConfig? FindRemovable(IEnumerable<LevelConfig>? existing, string? pane,
            double cursorPrice, double paneNeutral = 0)
        {
            if (existing == null) return null;

            double target = IsPricePane(pane) ? cursorPrice : paneNeutral;
            if (!double.IsFinite(target)) return null;

            // At zero the proportional tolerance collapses, so fall back to an absolute band.
            double tolerance = Math.Abs(target) > 0 ? Math.Abs(target) * RemoveTolerance : 1e-9;

            return existing
                .Where(l => l.IsUserDefined && Math.Abs(l.Value - target) <= tolerance)
                .OrderBy(l => Math.Abs(l.Value - target))
                .FirstOrDefault();
        }

        /// <summary>
        /// A name unique within the series, so the audio tracker and the saved level preferences —
        /// both of which key on the name — cannot confuse two of a person's levels for one.
        /// </summary>
        internal static string UniqueName(IEnumerable<LevelConfig>? existing, string preferred)
        {
            var taken = new HashSet<string>(
                (existing ?? Enumerable.Empty<LevelConfig>()).Select(l => l.Name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            if (!taken.Contains(preferred)) return preferred;
            for (int i = 2; ; i++)
            {
                string candidate = $"{preferred} {i}";
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        /// <summary>
        /// The level to add, or <c>null</c> when one cannot be placed.
        ///
        /// <para>
        /// Returning null matters as much as returning a level. A key that silently does nothing is
        /// indistinguishable from a key that is not bound, so the caller must speak the reason —
        /// which is why <paramref name="reason"/> is always set.
        /// </para>
        /// </summary>
        /// <param name="pane">The focused series' pane.</param>
        /// <param name="cursorPrice">
        /// Close of the bar under the cursor, or NaN when there is no data. Only consulted for a
        /// price pane.
        /// </param>
        /// <param name="existing">Levels already on the series, for unique naming.</param>
        /// <param name="reason">What to say — the confirmation, or why nothing happened.</param>
        public static LevelConfig? For(string? pane, double cursorPrice,
            IEnumerable<LevelConfig>? existing, out string reason)
            => For(pane, cursorPrice, existing, paneNeutral: 0, out reason, out _);

        /// <summary>
        /// The placement decision, told what the pane's neutral value is.
        /// </summary>
        /// <param name="paneNeutral">
        /// The value this pane's series swings about — <c>ComponentConfig.ReferenceLevel</c>, or
        /// null when nothing declares one. Only consulted off the price pane.
        /// </param>
        /// <param name="isRefusal">
        /// True when nothing was placed <i>and</i> that is a refusal the user should hear as an
        /// error. False for "there is already one there", which is information, not a failure —
        /// the two used to be the same return value and so were spoken in the same alarmed voice.
        /// </param>
        public static LevelConfig? For(string? pane, double cursorPrice,
            IEnumerable<LevelConfig>? existing, double? paneNeutral,
            out string reason, out bool isRefusal)
        {
            isRefusal = false;

            if (!IsPricePane(pane))
            {
                // Already marked. A provider that declares its own midline — RSI's Midpoint at 50,
                // MACD's Zero — has said where the meaningful constant is, and stacking a second
                // line of our own on top of it would give the pane two lines at one value with two
                // names, which the crossing earcons would then both report.
                var declared = existing?.FirstOrDefault(l => l.EffectiveRole == LevelRole.Neutral);
                if (declared != null)
                {
                    reason = $"{declared.Name} already marks {Format(declared.Value)} on this pane.";
                    return null;
                }

                // Nothing declares one. Refusing is the honest outcome: this key exists to mark the
                // line a value swings about, and on a pane where no such line is defined there is
                // no correct place to put it. Zero was the old answer and it was wrong wherever the
                // pane did not straddle zero.
                if (paneNeutral == null || !double.IsFinite(paneNeutral.Value))
                {
                    reason = "Nothing on this pane declares a neutral line, so there is no level to add.";
                    isRefusal = true;
                    return null;
                }

                double neutral = paneNeutral.Value;
                // Named for what it IS. "Zero" when the constant is zero, "Midpoint" otherwise —
                // a line at 50 called "Zero" is the thing that made the original defect hard to
                // spot in a spoken chart, because the name agreed with the key and not the value.
                string neutralName = UniqueName(existing, neutral == 0 ? "Zero" : "Midpoint");
                reason = neutral == 0
                    ? $"{neutralName} line added, audible on crossing."
                    : $"{neutralName} added at {Format(neutral)}, audible on crossing.";
                return Audible(neutralName, neutral, LevelRole.Neutral);
            }

            // A price pane. Zero is never the answer here; the cursor's price is.
            if (!double.IsFinite(cursorPrice) || cursorPrice <= 0)
            {
                reason = "No price under the cursor to place a level at.";
                return null;
            }

            string name = UniqueName(existing, "Level");
            reason = $"{name} added at {Format(cursorPrice)}, audible on crossing.";
            return Audible(name, cursorPrice, LevelRole.None);
        }

        /// <summary>
        /// Backwards-compatible overload for callers that do not track existing levels.
        /// </summary>
        public static LevelConfig? For(string? pane, double cursorPrice, out string reason)
            => For(pane, cursorPrice, null, out reason);

        /// <summary>
        /// A level that can actually be heard.
        ///
        /// <para>
        /// <c>PlayEarcon</c> defaults to <c>false</c> on <see cref="LevelConfig"/>, which is right for
        /// providers declaring a dozen reference lines nobody asked for — but wrong here. Someone who
        /// deliberately marks a price on an audio-first terminal wants to know when price reaches it;
        /// that is the entire reason for placing it. A silent level would be a line only a sighted
        /// user could benefit from.
        /// </para>
        ///
        /// <para>
        /// <c>Both</c> rather than <c>Auto</c> because the intent is explicit: this is a price
        /// someone marked, and either direction of approach is worth hearing. Leaving it on Auto
        /// would work too, but only by accident of the name not matching "Overbought".
        /// </para>
        /// </summary>
        private static LevelConfig Audible(string name, double value, LevelRole role) => new()
        {
            Name = name,
            Value = value,
            ColorHex = "#888888",
            DashStyle = DashStyle.Dash,
            IsVisible = true,
            PlayEarcon = true,
            EarconVolume = 0.7f,
            CrossDirection = LevelCrossDirection.Both,
            Role = role,
            IsUserDefined = true,
        };

        /// <summary>
        /// Prices span nine orders of magnitude here, so a fixed precision either loses a sub-cent
        /// token entirely or reads out six meaningless zeros on an index.
        /// </summary>
        internal static string Format(double price)
        {
            string s = Math.Abs(price) >= 1
                ? price.ToString("N2", CultureInfo.InvariantCulture)
                : price.ToString("0.########", CultureInfo.InvariantCulture);
            return s;
        }
    }
}

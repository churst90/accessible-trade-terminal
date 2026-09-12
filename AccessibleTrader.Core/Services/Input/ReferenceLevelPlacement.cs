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
    /// which every component DECLARES through <c>IndicatorComponentMetadata.DefaultReferenceLevel</c>.
    /// It is the field the audio layer already splits above/below waveforms on, so "the line this
    /// component swings about" was stated across the whole provider fleet before this key needed
    /// it. Until 2026-09-12 a component that declared nothing was answered for — by a substring
    /// match on the indicator's code, then by the display type's 0.0 — and four bounded
    /// oscillators were answered wrongly. See <c>DeclaredNeutralTests</c>.
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
        /// The level the <c>0</c> key should TOGGLE instead of adding: the pane's own declared
        /// midline.
        ///
        /// <para>
        /// ── Why a toggle (2026-09-12) ──────────────────────────────────────────────
        /// Cody: <i>"0 should toggle the visibility/earcons of the 0 line, pressing it now doesn't
        /// seem to do much of anything."</i> He is right, and the reason is that the common case
        /// was the refusal. On RSI, Stochastic, MFI — anything whose provider declares its
        /// midline — pressing <c>0</c> said "Midpoint already marks 50.00 on this pane" and
        /// changed nothing, every time, forever. The key had exactly one outcome on the
        /// indicators people press it on, and that outcome was a sentence.
        /// </para>
        ///
        /// <para>
        /// So the declared midline stops being a reason to refuse and becomes the thing the key
        /// operates. On, the line is drawn and its crossing is heard; off, it is neither. That is
        /// the same promise the key already made where it added a level — "audible on crossing" —
        /// now extended to the line that was there before you arrived.
        /// </para>
        ///
        /// <para>
        /// Only a PROVIDER's midline. One you added yourself is removed instead, by
        /// <see cref="FindRemovable"/>, because for your own line "off" and "gone" are the same
        /// wish and removing it is the only way the keyboard has ever had to take one back.
        /// </para>
        /// </summary>
        public static LevelConfig? FindToggleable(IEnumerable<LevelConfig>? existing, string? pane)
        {
            if (existing == null || IsPricePane(pane)) return null;
            return existing.FirstOrDefault(l => l.EffectiveRole == LevelRole.Neutral && !l.IsUserDefined);
        }

        /// <summary>
        /// Whether a level is switched ON, as one idea. Visibility and the crossing earcon are two
        /// fields — Properties sets them separately — but a level that is drawn and silent is still
        /// "on" to someone looking at it, and one that is hidden but chimes is still "on" to
        /// someone listening. Either half counts, so a toggle from here always lands somewhere the
        /// user can perceive.
        /// </summary>
        public static bool IsAudible(LevelConfig level) => level.IsVisible || level.PlayEarcon;

        /// <summary>What to say when the key flips one. Says the value, because the name may not carry it.</summary>
        public static string ToggleReason(LevelConfig level, bool nowAudible) => nowAudible
            ? $"{level.Name} at {Format(level.Value)} shown, audible on crossing."
            : $"{level.Name} at {Format(level.Value)} hidden and silent.";

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
        /// True when nothing was placed because nothing ever could be here — this pane declares no
        /// neutral, so the key will refuse on it every time. False for "there is already one
        /// there", which is a fact about today and will change when the level is removed.
        ///
        /// <para><b>Neither is an error.</b> It said "should hear as an error" until 2026-09-11,
        /// and the error earcon is the one sound that ignores Shift+F3 (the silent-failure rule),
        /// so a routine "not applicable on this pane" was the only thing that could pierce a mute
        /// the user had deliberately set. The caller now speaks a refusal as <c>Boundary</c> —
        /// the key was understood and has nowhere to go, the same classification the anchor nudge
        /// already used — and "already marked" as <c>Info</c>.</para>
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
                //
                // The 0 KEY no longer reaches this branch: it asks FindToggleable first and flips
                // the declared line rather than being told about it. This is for the Properties
                // dialog's "Add level" button, which has checkboxes of its own for the line that
                // is already there and so wants the refusal, not a toggle.
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

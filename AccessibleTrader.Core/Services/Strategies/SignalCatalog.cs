using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Default <see cref="ISignalCatalog"/> implementation. Walks every registered
    /// <c>IIndicatorProvider</c> at startup, derives the <see cref="SignalKind"/> from
    /// each component's <c>DisplayType</c>, and caches the resulting flat list.
    ///
    /// <para>
    /// It publishes only what has been declared causal. This class is the reason the declaration
    /// exists: it is the one place that turns a chart component into something a strategy can be
    /// built on, and until 2026-08-21 it did that for every component of every provider with no
    /// allowlist and no gate — which is how Ichimoku's Chikou Span, a raw 26-bar future price,
    /// became a leaf a backtest could compare against close.
    /// </para>
    ///
    /// <para>
    /// It also publishes only what can be computed. The Add Indicator menu has always filtered on
    /// that (<see cref="Indicators.IndicatorComputability"/>) and this class did not, so an
    /// indicator the user could not put on a chart — PPO, HV, TMA, ZLEMA, EOM, none of which
    /// Skender 2.5.0 implements — was still offered as a strategy leaf, where it evaluated to NaN
    /// and therefore false on every bar for the life of the strategy, silently.
    /// </para>
    ///
    /// <para>
    /// Refused components stay resolvable through <see cref="GetById"/> and are listed in
    /// <see cref="Excluded"/>. They are withheld from <see cref="All"/> — the pickable list — but
    /// a strategy saved before this gate existed must be able to say why its leaf stopped working,
    /// and "unknown signal" would be a lie.
    /// </para>
    /// </summary>
    public class SignalCatalog : ISignalCatalog
    {
        private readonly IEnumerable<IIndicatorProvider> _providers;
        private List<SignalDescriptor> _all = new();
        private List<SignalDescriptor> _excluded = new();
        private Dictionary<string, string> _refusals = new();
        private Dictionary<string, SignalDescriptor> _byId = new();
        private Dictionary<string, List<SignalDescriptor>> _byIndicator = new();

        public IReadOnlyList<SignalDescriptor> All => _all;

        /// <summary>
        /// Signals a provider USED to declare, and why each went. A strategy or alert saved while
        /// one existed still names it; without this its leaf resolves to nothing and is false on
        /// every bar with no reason given — a condition that silently never fires, which is
        /// indistinguishable from a market that never got there. Listed here, the reason reaches
        /// <see cref="RefusalReason"/> and the evaluator says it (ConditionEvaluator.Resolve).
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> Retired = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            // Cody's 2026-10-17 report: hidden stops that said ±53, which the levels already say.
            // See CipherBProvider.ThresholdObKey.
            ["CIPHER_B.Adaptive OB"] = "Cipher B's Adaptive OB was removed on 2026-10-17; compare Wave Trend Fast with your overbought level (53 by default) instead",
            ["CIPHER_B.Adaptive OS"] = "Cipher B's Adaptive OS was removed on 2026-10-17; compare Wave Trend Fast with your oversold level (-53 by default) instead",

            // 2026-10-17: Cipher A rebuilt from measurements of real Market Cipher A (an EMA ribbon and
            // seven symbols). Its old all-WaveTrend components went; each message names the nearest
            // thing that still exists. See CipherAProvider.
            ["CIPHER_A.Buy Signal"] = "Cipher A's Buy Signal was removed on 2026-10-17 when Cipher A was rebuilt to match Market Cipher A; the same wave cross up from oversold is Cipher B's Oversold Crossover, and Cipher A's bullish symbols are now Green Dot, Blue Triangle and Yellow Diamond",
            ["CIPHER_A.Sell Signal"] = "Cipher A's Sell Signal was removed on 2026-10-17 when Cipher A was rebuilt to match Market Cipher A; the same wave cross down from overbought is Cipher B's Overbought Crossover, and Cipher A's wave cross down is now its Red Diamond",
            ["CIPHER_A.Bullish Divergence"] = "Cipher A's Bullish Divergence was removed on 2026-10-17 when Cipher A was rebuilt to match Market Cipher A, which draws no divergences; use Cipher B's Bullish Divergence",
            ["CIPHER_A.Bearish Divergence"] = "Cipher A's Bearish Divergence was removed on 2026-10-17 when Cipher A was rebuilt to match Market Cipher A, which draws no divergences; use Cipher B's Bearish Divergence",
            ["CIPHER_A.Overbought Bearish Divergence"] = "Cipher A's Overbought Bearish Divergence was removed on 2026-10-17 when Cipher A was rebuilt to match Market Cipher A; its Blood Diamond is now a Red Diamond and a Red X on the same candle, and the nearest divergence is Cipher B's Bearish Divergence",
            ["CIPHER_A.Manipulation"] = "Cipher A's Manipulation (gold X) was removed on 2026-10-17 when Cipher A was rebuilt to match Market Cipher A; Market Cipher A's Yellow X is a bearish warning, now Cipher A's Yellow X, and there is no replacement for the old oversold-with-positive-money-flow signal",
            ["CIPHER_A.Exhaustion"] = "Cipher A's Exhaustion was removed on 2026-10-17 when Cipher A was rebuilt to match Market Cipher A; Market Cipher A's Red X is now Cipher A's Red X (EMA 5 crossing below EMA 11), and there is no replacement for the old overbought-with-negative-money-flow signal",
            ["CIPHER_A.WT Momentum"] = "Cipher A's WT Momentum was removed on 2026-10-17 when Cipher A was rebuilt to match Market Cipher A; read Cipher B's Wave Trend instead",
            ["CIPHER_A.WT Momentum Gradient"] = "Cipher A's WT Momentum Gradient was removed on 2026-10-17 when Cipher A was rebuilt to match Market Cipher A; read Cipher B's Wave Trend instead (the gradient was Wave Trend clamped to ±53 and rescaled to 0–1)",

            // Cipher SR rebuilt to draw what Market Cipher SR draws (CipherSrProvider). Its pivot-bar
            // dots were look-ahead (drawn before the pivot could be known) and MCSR has no such marker.
            ["CIPHER_SR.Resistance"] = "Cipher SR's Resistance pivot dot was removed on 2026-10-17 (it was drawn before the pivot could be known, and Market Cipher SR has no such marker); use Resistance Zone, the held resistance level, instead",
            ["CIPHER_SR.Support"] = "Cipher SR's Support pivot dot was removed on 2026-10-17 (it was drawn before the pivot could be known, and Market Cipher SR has no such marker); use Support Zone, the held support level, instead",

            // 2026-10-21: Cipher C rebuilt after CF's Cycle Trading Indicator (CipherCProvider). Its old
            // Ehlers-oscillator components went; each message names the nearest thing that still exists.
            ["CIPHER_C.Top Single"] = "Cipher C's Top Single was removed on 2026-10-21 when Cipher C was rebuilt as a cycle-low indicator; there is no replacement for its cycle-top dots; the nearest is Left Translated Top, drawn over an early cycle top",
            ["CIPHER_C.Top Double"] = "Cipher C's Top Double was removed on 2026-10-21 when Cipher C was rebuilt as a cycle-low indicator; there is no replacement for its cycle-top dots; the nearest is Left Translated Top, drawn over an early cycle top",
            ["CIPHER_C.Top Triple"] = "Cipher C's Top Triple was removed on 2026-10-21 when Cipher C was rebuilt as a cycle-low indicator; there is no replacement for its cycle-top dots; the nearest is Left Translated Top, drawn over an early cycle top",
            ["CIPHER_C.Bottom Single"] = "Cipher C's Bottom Single was removed on 2026-10-21 when Cipher C was rebuilt as a cycle-low indicator; use DCL Confirmed, the bar a daily cycle low is confirmed",
            ["CIPHER_C.Bottom Double"] = "Cipher C's Bottom Double was removed on 2026-10-21 when Cipher C was rebuilt as a cycle-low indicator; use DCL Confirmed, the bar a daily cycle low is confirmed",
            ["CIPHER_C.Bottom Triple"] = "Cipher C's Bottom Triple was removed on 2026-10-21 when Cipher C was rebuilt as a cycle-low indicator; use DCL Confirmed, the bar a daily cycle low is confirmed; its value is the confluence, 1 to 3",
            ["CIPHER_C.Cycle Sine"] = "Cipher C's Cycle Sine was removed on 2026-10-21 when Cipher C was rebuilt as a cycle-low indicator; there is no oscillator any more; use Daily Timing Window, the daily cycle's day count",
            ["CIPHER_C.Lead Sine"] = "Cipher C's Lead Sine was removed on 2026-10-21 when Cipher C was rebuilt as a cycle-low indicator; there is no oscillator any more; use Daily Timing Window, the daily cycle's day count",
            ["CIPHER_C.Shallow Peak"] = "Cipher C's Shallow Peak was removed on 2026-10-21 when Cipher C was rebuilt as a cycle-low indicator; use Cycle Failed, a close under the cycle low after it was confirmed",
            ["CIPHER_C.Shallow Trough"] = "Cipher C's Shallow Trough was removed on 2026-10-21 when Cipher C was rebuilt as a cycle-low indicator; there is no replacement for the old bull-continuation dot; the nearest is DCL Confirmed at 3, a high-confluence daily cycle low",

            // 2026-10-22: Cipher C's 28 components cut to 15 (Cody: "Each component must hold value"). A number
            // that only explained another component is now that component's companion and is said with it.
            ["CIPHER_C.DCL Day"] = "Cipher C's DCL Day was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; use Daily Timing Window, whose value is the same day count",
            ["CIPHER_C.WCL Week"] = "Cipher C's WCL Week was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; use Weekly Timing Window, whose value is the same week count",
            ["CIPHER_C.In DCL Window"] = "Cipher C's In DCL Window was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; use Daily Timing Window, the day count, against the window's bounds (the preset's daily range; Alt+Shift+D says them)",
            ["CIPHER_C.In WCL Window"] = "Cipher C's In WCL Window was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; use Weekly Timing Window, the week count, against the preset's weekly range",
            ["CIPHER_C.DCL Window Start"] = "Cipher C's DCL Window Start was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; the window's bounds are the preset's daily range, said with Daily Timing Window",
            ["CIPHER_C.DCL Window End"] = "Cipher C's DCL Window End was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; the window's bounds are the preset's daily range, said with Daily Timing Window",
            ["CIPHER_C.WCL Window Start"] = "Cipher C's WCL Window Start was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; the window's bounds are the preset's weekly range, said with Weekly Timing Window",
            ["CIPHER_C.WCL Window End"] = "Cipher C's WCL Window End was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; the window's bounds are the preset's weekly range, said with Weekly Timing Window",
            ["CIPHER_C.Cycle Low Confluence"] = "Cipher C's Cycle Low Confluence was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; use DCL Confirmed (or WCL or YCL Confirmed), whose value is now the confluence, 1 to 3",
            ["CIPHER_C.Cycle Low Updated"] = "Cipher C's Cycle Low Updated was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; a confirmation that replaces a cancelled low now says so itself; use DCL Confirmed",
            ["CIPHER_C.Cycle Arc"] = "Cipher C's Cycle Arc was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; the arcs were a picture of where the cycle is, which Daily Timing Window says in words; use it",
            ["CIPHER_C.Cycle Arc Position"] = "Cipher C's Cycle Arc Position was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; use Daily Timing Window, the daily cycle's day count",
            ["CIPHER_C.Asset Preset"] = "Cipher C's Asset Preset was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; the preset is said by Alt+Shift+D on Cipher C; it was never a per-bar signal",
            ["CIPHER_C.DCL Count"] = "Cipher C's DCL Count was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; the cycle's length is now printed over each daily cycle low and said with it; use DCL",
            ["CIPHER_C.WCL Count"] = "Cipher C's WCL Count was removed on 2026-10-22 when Cipher C's components were cut to the ones that hold something to hear; the cycle's length is now said with each weekly cycle low; use WCL",
        };

        /// <inheritdoc/>
        public IReadOnlyList<SignalDescriptor> Excluded => _excluded;

        /// <inheritdoc/>
        /// <remarks>A signal on a whole indicator that was removed (Spider Lines, 2026-10-17) is
        /// answered from <see cref="Indicators.RetiredIndicators"/>: its provider is gone, so no
        /// component of it is enumerated above, and every one of them must still say why.</remarks>
        public string? RefusalReason(string id) =>
            _refusals.TryGetValue(id, out var why) ? why : Indicators.RetiredIndicators.SignalReason(id);

        public SignalCatalog(IEnumerable<IIndicatorProvider> providers)
        {
            _providers = providers;
            Refresh();
        }

        public SignalDescriptor? GetById(string id) =>
            _byId.TryGetValue(id, out var d) ? d : null;

        public IReadOnlyList<SignalDescriptor> GetForIndicator(string indicatorCode) =>
            _byIndicator.TryGetValue(indicatorCode, out var list) ? list : (IReadOnlyList<SignalDescriptor>)System.Array.Empty<SignalDescriptor>();

        public void Refresh()
        {
            var list = new List<SignalDescriptor>();
            var refused = new List<SignalDescriptor>();
            var reasons = new Dictionary<string, string>();

            foreach (var provider in _providers)
            {
                List<IndicatorMetadata> indicators;
                try { indicators = provider.GetIndicators(); }
                catch { continue; }

                foreach (var ind in indicators)
                {
                    // Asked once per indicator, not per component: it is reflection over the
                    // indicator library's exported methods.
                    string? notComputable = Indicators.IndicatorComputability.RefusalReason(provider, ind);

                    foreach (var comp in ind.Components)
                    {
                        // Build a stable ID. {INDICATOR_CODE}.{ComponentName} survives renames
                        // of friendly labels because the underlying registry code rarely changes.
                        string id = $"{ind.Code}.{comp.Name}";
                        var kind = ClassifyKind(comp.DisplayType.ToString());
                        string label = $"{ind.Name} — {comp.DisplayName ?? comp.Name}";
                        // A component that IS a bar column says which one. "Candles — Body" is the
                        // close, and nothing in that label said so to someone choosing the line
                        // "close crosses above the SMA" should cross.
                        if (!string.IsNullOrEmpty(comp.DataMapping))
                            label += $" ({comp.DataMapping})";

                        var descriptor = new SignalDescriptor(
                            Id: id,
                            IndicatorCode: ind.Code,
                            ComponentName: comp.Name,
                            Kind: kind,
                            DisplayLabel: label,
                            Causality: CausalityContract.Effective(ind, comp)
                        );

                        // Computability first: an indicator that cannot produce a number at all is
                        // a more basic refusal than "this component reads the future", and saying
                        // the causal thing about a leaf that is permanently NaN would be beside
                        // the point.
                        string? why = notComputable ?? CausalityContract.RefusalReason(ind, comp);
                        if (why == null)
                        {
                            list.Add(descriptor);
                        }
                        else
                        {
                            refused.Add(descriptor);
                            reasons[id] = $"{id} is not available as a strategy signal: {why}.";
                        }
                    }
                }
            }

            _all = list;
            _excluded = refused;
            foreach (var (id, why) in Retired) reasons.TryAdd(id, why);
            _refusals = reasons;
            // Both halves are addressable by ID. A refused leaf resolving to null would read as a
            // typo in the strategy spec; resolving to a descriptor that knows it is not causal
            // lets the evaluator refuse it out loud.
            //
            // First registration wins on a duplicate ID rather than throwing. Two providers can
            // legitimately answer to one indicator code — MACloudProvider has a subclass kept as a
            // name alias — and a ToDictionary here turns that into an exception during DI
            // construction, which takes the whole app down over a shadowed strategy leaf.
            _byId = new Dictionary<string, SignalDescriptor>();
            foreach (var d in list.Concat(refused))
                _byId.TryAdd(d.Id, d);
            _byIndicator = list.GroupBy(d => d.IndicatorCode)
                               .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// Map a component's display type string to a <see cref="SignalKind"/>.
        /// Marker shapes (Dot, Diamond, Cross, Arrow) become <see cref="SignalKind.MarkerFire"/>;
        /// numeric line/area types fall into <see cref="SignalKind.Line"/> or
        /// <see cref="SignalKind.Oscillator"/> depending on whether they live in a sub-pane.
        /// </summary>
        private static SignalKind ClassifyKind(string? displayType)
        {
            if (string.IsNullOrEmpty(displayType)) return SignalKind.Line;
            string t = displayType.Trim();
            if (t.Equals("Dot", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("ZeroDot", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Arrow", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Diamond", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("TriangleUp", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("TriangleDown", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Square", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Cross", System.StringComparison.OrdinalIgnoreCase))
                return SignalKind.MarkerFire;

            if (t.Equals("Cloud", System.StringComparison.OrdinalIgnoreCase) ||
                t.Equals("AreaFill", System.StringComparison.OrdinalIgnoreCase))
                return SignalKind.Cloud;

            return SignalKind.Line;
        }
    }
}

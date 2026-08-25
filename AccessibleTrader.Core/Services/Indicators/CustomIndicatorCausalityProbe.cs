using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>What the probe concluded about one component of a scripted indicator.</summary>
    /// <param name="Component">Component name, as the script reports it.</param>
    /// <param name="Declared">What the script claimed.</param>
    /// <param name="Measured">
    /// What it actually did. <see cref="ComponentCausality.Causal"/> when it survived both sweeps,
    /// <see cref="ComponentCausality.Lookahead"/> when a bar's value moved,
    /// <see cref="ComponentCausality.Undeclared"/> when the probe never saw a value for it and so
    /// established nothing either way.
    /// </param>
    /// <param name="Publishable">Whether this component may be offered to the strategy builder.</param>
    /// <param name="Finding">Human-readable detail, null when nothing was wrong.</param>
    public record CustomIndicatorComponentVerdict(
        string Component,
        ComponentCausality Declared,
        ComponentCausality Measured,
        bool Publishable,
        string? Finding);

    /// <summary>The probe's verdict on a whole scripted indicator.</summary>
    /// <param name="IndicatorId">The script's <c>Id</c>.</param>
    /// <param name="Components">One verdict per component, in <c>ComponentNames</c> order.</param>
    /// <param name="Failed">
    /// True when the indicator could not be probed at all — it threw, or returned arrays of the
    /// wrong shape. Nothing is publishable in that case.
    /// </param>
    /// <param name="Error">Why probing failed, null otherwise.</param>
    public record CustomIndicatorCausalityReport(
        string IndicatorId,
        IReadOnlyList<CustomIndicatorComponentVerdict> Components,
        bool Failed,
        string? Error)
    {
        /// <summary>Every finding worth showing the script's author, most serious first.</summary>
        public IReadOnlyList<string> Findings =>
            (Error is null ? Array.Empty<string>() : new[] { Error })
            .Concat(Components.Where(c => c.Finding != null).Select(c => c.Finding!))
            .ToList();

        /// <summary>True when the script may be offered to the strategy builder at all.</summary>
        public bool AnyPublishable => !Failed && Components.Any(c => c.Publishable);

        public CustomIndicatorComponentVerdict? For(string component) =>
            Components.FirstOrDefault(c => string.Equals(c.Component, component, StringComparison.Ordinal));
    }

    /// <summary>
    /// Proves — or refutes — a scripted indicator's causality by running it, at the moment it is
    /// registered.
    ///
    /// <para>
    /// Built-in providers declare their causality on metadata and <c>IndicatorCausalityTests</c>
    /// proves the declaration at build time. A script cannot be covered by a test that does not
    /// know it exists, and a script's self-declaration is worth even less than a provider's: it is
    /// written by whoever wanted the indicator, often ported from Pine where the distinction is not
    /// drawn at all, and there is nobody to review it. So for scripts the proof is empirical and it
    /// runs at registration, on the compiled instance itself.
    /// </para>
    ///
    /// <para>
    /// Two sweeps, the same two the built-in contract uses, for two different failures:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Prefix.</b> Run over the first k bars and over all of them; a component whose value
    /// at a shared bar differs read a bar that had not happened. This is the Chikou Span shape —
    /// correct plotting, catastrophic as a strategy leaf.</item>
    /// <item><b>Suffix.</b> Run over all the bars and over <c>bars.Skip(k)</c>; a component whose
    /// value at a shared bar differs rewrites its own past when the user scrolls back. Pine ports
    /// are especially prone to this, because <c>bar_index</c> transliterates to an array index and
    /// an array index moves when history is prepended.</item>
    /// </list>
    ///
    /// <para>
    /// The verdict is measurement first, declaration second. Measuring cannot prove a component
    /// causal on all possible data — only that it was causal on this — which is the same limit the
    /// built-in prefix sweep has always had. What it CAN do is refute: a component that moved is
    /// not causal, whatever it claimed, and claiming Causal while moving is the one combination
    /// that gets reported as an error rather than a note.
    /// </para>
    /// </summary>
    public static class CustomIndicatorCausalityProbe
    {
        /// <summary>
        /// Bars per probe run. Shorter than the build-time sweep's 1400: this runs while a user
        /// waits for a compile to finish, and every run may be a round trip to the sandbox worker
        /// process. Long enough for the recursive filters a script is likely to build to settle,
        /// and for a 200-bar window to be full twice over.
        /// </summary>
        public const int ProbeLength = 700;

        private static readonly int[] PrefixLengths = { 220, 330, 480, 640 };
        private static readonly int[] SuffixDrops = { 23, 91 };
        private const int SuffixWarmup = 350;

        /// <summary>
        /// Same tolerance and the same reason as the build-time suffix sweep: an EMA seeded at a
        /// different bar converges rather than matching, while anything pinned to array index 0
        /// does not converge at all. 1e-6 sits between the two populations.
        /// </summary>
        private const double Tolerance = 1e-6;

        /// <summary>
        /// Flavours 0 and 3 — one regular hourly series and the irregularly spaced one. The build
        /// sweep runs all four because it is covering fifty providers; here two is the trade
        /// against how long a user waits, and flavour 3 is not optional: a script that asks the
        /// data what timeframe it is on cannot be caught by a series whose every sample gives the
        /// same answer.
        /// </summary>
        private static readonly int[] Flavours = { 0, 3 };

        public static CustomIndicatorCausalityReport Probe(ICustomIndicator indicator)
        {
            ArgumentNullException.ThrowIfNull(indicator);

            var names = indicator.ComponentNames ?? Array.Empty<string>();
            var declared = indicator.Causality;
            var pars = indicator.DefaultParameters ?? new Dictionary<string, double>();

            // Anything the probe saw move, and the first explanation of why.
            var moved = new Dictionary<int, string>();
            var everValued = new HashSet<int>();

            foreach (int flavour in Flavours)
            {
                var full = CausalityProbeSeries.Bars(flavour, ProbeLength);

                double[][] whole;
                try { whole = Run(indicator, pars, full); }
                catch (Exception ex)
                {
                    return Failure(indicator.Id, names, declared,
                        $"The indicator threw while being checked, on {full.Count} ordinary bars: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }

                if (whole.Length != names.Length)
                    return Failure(indicator.Id, names, declared,
                        $"Calculate returned {whole.Length} component arrays but ComponentNames lists " +
                        $"{names.Length}. Every component must get an array, in the same order.");

                for (int c = 0; c < whole.Length; c++)
                {
                    if (whole[c] != null && whole[c].Length != full.Count)
                        return Failure(indicator.Id, names, declared,
                            $"Component '{Name(names, c)}' returned {whole[c].Length} values for " +
                            $"{full.Count} bars. Each array must be exactly as long as the input.");
                    if (whole[c] != null && Array.Exists(whole[c], v => !double.IsNaN(v)))
                        everValued.Add(c);
                }

                // ── Does a bar change when the FUTURE arrives? ────────────────────────────────
                foreach (int k in PrefixLengths)
                {
                    double[][] shortRun;
                    try { shortRun = Run(indicator, pars, full.Take(k).ToList()); }
                    catch (Exception ex)
                    {
                        return Failure(indicator.Id, names, declared,
                            $"The indicator threw on the first {k} bars but not on {full.Count}: " +
                            $"{ex.GetType().Name}: {ex.Message}. It has to work on a freshly loaded " +
                            $"chart, which is always the short case.");
                    }

                    Compare(shortRun, whole, names, moved, first: 0, shortOffset: 0, longOffset: 0,
                        limit: k,
                        describe: (name, i, a, b) =>
                            $"'{name}' reads {a:G6} at bar {i} when {k} bars are loaded and {b:G6} when " +
                            $"{full.Count} are. Bar {i} cannot depend on bar {k} or later — that is a " +
                            $"look-ahead, and a strategy reading it would trade on data that did not exist yet.");
                }

                // ── Does a bar change when OLDER bars arrive? ─────────────────────────────────
                foreach (int k in SuffixDrops)
                {
                    double[][] shortRun;
                    try { shortRun = Run(indicator, pars, full.Skip(k).ToList()); }
                    catch (Exception ex)
                    {
                        return Failure(indicator.Id, names, declared,
                            $"The indicator threw on the last {full.Count - k} bars but not on " +
                            $"{full.Count}: {ex.GetType().Name}: {ex.Message}.");
                    }

                    Compare(shortRun, whole, names, moved, first: SuffixWarmup, shortOffset: 0, longOffset: k,
                        limit: int.MaxValue,
                        describe: (name, i, a, b) =>
                            $"'{name}' reads {a:G6} at a bar before {k} older bars are prepended and " +
                            $"{b:G6} at the same bar after. Scrolling back would rewrite that bar on the " +
                            $"chart. Something is pinned to the start of the array — a bar_index, a " +
                            $"bucket, or a running total — where it should be pinned to the bar's date.");
                }
            }

            var verdicts = new List<CustomIndicatorComponentVerdict>(names.Length);
            for (int c = 0; c < names.Length; c++)
            {
                var claim = CausalityContract.Declared(declared, c);
                string name = Name(names, c);

                if (moved.TryGetValue(c, out var why))
                {
                    // It moved. The only question left is whether the script said it would.
                    string finding = claim == ComponentCausality.Causal
                        ? $"Component {why} It is declared Causal, and it is not — the declaration has " +
                          $"been overruled by what the code did."
                        : $"Component {why}";
                    verdicts.Add(new CustomIndicatorComponentVerdict(
                        name, claim, ComponentCausality.Lookahead, Publishable: false, finding));
                }
                else if (!everValued.Contains(c))
                {
                    // Never produced a number, so nothing was established. Same honesty as the
                    // built-in NotExercisedByTheseSeries list: silence is not evidence.
                    verdicts.Add(new CustomIndicatorComponentVerdict(
                        name, claim, ComponentCausality.Undeclared, Publishable: false,
                        $"Component '{name}' produced no value at all on either check series, so nothing " +
                        $"about it could be established. It will draw, but it is not offered to strategies."));
                }
                else if (claim == ComponentCausality.Lookahead)
                {
                    // Held still here, but the author says it reads ahead somewhere. Believe them:
                    // the probe can refute a claim of causality, never a claim of look-ahead.
                    verdicts.Add(new CustomIndicatorComponentVerdict(
                        name, claim, ComponentCausality.Lookahead, Publishable: false, null));
                }
                else
                {
                    verdicts.Add(new CustomIndicatorComponentVerdict(
                        name, claim, ComponentCausality.Causal, Publishable: true, null));
                }
            }

            return new CustomIndicatorCausalityReport(indicator.Id, verdicts, Failed: false, Error: null);
        }

        private static void Compare(double[][] shortRun, double[][] longRun, string[] names,
            Dictionary<int, string> moved, int first, int shortOffset, int longOffset, int limit,
            Func<string, int, double, double, string> describe)
        {
            if (shortRun.Length != longRun.Length) return;

            for (int c = 0; c < shortRun.Length; c++)
            {
                if (moved.ContainsKey(c)) continue;         // already explained once
                var a = shortRun[c];
                var b = longRun[c];
                if (a == null || b == null) continue;

                int len = Math.Min(Math.Min(a.Length - shortOffset, b.Length - longOffset), limit);
                for (int i = first; i < len; i++)
                {
                    double x = a[i + shortOffset], y = b[i + longOffset];
                    if (Same(x, y)) continue;
                    moved[c] = describe(Name(names, c), i + longOffset, x, y);
                    break;
                }
            }
        }

        private static bool Same(double a, double b)
        {
            if (double.IsNaN(a) && double.IsNaN(b)) return true;
            if (double.IsNaN(a) || double.IsNaN(b)) return false;
            return Math.Abs(a - b) <= Tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
        }

        private static double[][] Run(ICustomIndicator indicator, Dictionary<string, double> pars,
            List<Ohlcv> bars)
        {
            // A fresh dictionary per call: a script is free to mutate what it is handed, and one
            // that did would otherwise carry state from the long run into the short one and make
            // the comparison meaningless.
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars);
            return indicator.Calculate(span, new Dictionary<string, double>(pars))
                   ?? Array.Empty<double[]>();
        }

        private static string Name(string[] names, int index) =>
            index >= 0 && index < names.Length ? names[index] : $"#{index}";

        private static CustomIndicatorCausalityReport Failure(string id, string[] names,
            ComponentCausality[]? declared, string error)
        {
            var verdicts = names.Select((n, c) => new CustomIndicatorComponentVerdict(
                n, CausalityContract.Declared(declared, c), ComponentCausality.Undeclared,
                Publishable: false, Finding: null)).ToList();
            return new CustomIndicatorCausalityReport(id, verdicts, Failed: true, Error: error);
        }
    }
}

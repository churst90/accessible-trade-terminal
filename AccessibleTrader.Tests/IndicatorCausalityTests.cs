using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Does a component's value at a bar depend on bars that had not happened yet?
    ///
    /// <para>
    /// <c>SignalCatalog</c> publishes every component of every provider as a strategy leaf. There
    /// is no allowlist, so a component that plots a future value is not a chart-cosmetics question
    /// — it is a backtest-validity question. Ichimoku's Chikou Span holds <c>close[j + 26]</c> at
    /// bar j, which is the correct way to draw a lagging span and a catastrophic way to publish
    /// data: the condition "Chikou Span &gt; Close" returned a spectacular, entirely fake edge. Four
    /// more components had the same shape, each found and fixed once somewhere else in this
    /// codebase and never at the site next door.
    /// </para>
    ///
    /// <para>
    /// So every component declares itself (<see cref="ComponentCausality"/>) and this is the proof.
    /// Run each provider over the first k bars, run it over all of them, and require every
    /// component declared <see cref="ComponentCausality.Causal"/> to give the same answer on the
    /// shared prefix. Sweeping k matters more than the value of k: a marker only disagrees within
    /// the last few bars of the prefix, so one k probes one place in the series and finds nothing.
    /// </para>
    ///
    /// <para>
    /// Note what this catches beyond look-ahead proper. Any parameter derived from
    /// <c>data.Length</c> fails it — Cipher SR scaled its pivot window by the total loaded bar
    /// count, Value Deviation capped its profile at a third of the series, Pulse blanked a whole
    /// component when the series was short. In each case the same bar answered differently in a
    /// backtest and on a live chart, decided by how much history happened to be fetched.
    /// </para>
    /// </summary>
    public class IndicatorCausalityTests
    {
        /// <summary>
        /// Every built-in indicator provider, constructed reflectively so a provider added later is
        /// covered without anyone remembering to add it here.
        /// </summary>
        public static IEnumerable<object[]> Providers() =>
            ProviderTypes().Select(t => new object[] { t });

        // The set itself lives in IndicatorProviderFixture: SignalCatalogComputabilityTests needs
        // the same one, and two guards each enumerating "every provider" their own way is how one
        // of them quietly ends up covering fewer.
        private static IEnumerable<Type> ProviderTypes() => IndicatorProviderFixture.ProviderTypes();

        /// <summary>
        /// Builds a provider whatever its constructor asks for, substituting its interface
        /// dependencies. Requiring a parameterless constructor would have quietly excluded the three
        /// StrategyLab providers, which take an <c>ICrossSeriesCache</c> — and a contract that skips
        /// the providers feeding the research tooling is not much of a contract.
        /// </summary>
        private static IIndicatorProvider Create(Type type) => IndicatorProviderFixture.Create(type);

        // ── Synthetic series ──────────────────────────────────────────────────────────────────
        // The generator itself lives in Core, as CausalityProbeSeries: the registration-time probe
        // that sweeps a user's compiled script needs the same bars this sweeps the built-ins over,
        // and two generators would drift with only one of them being watched.

        private const int SeriesLength = CausalityProbeSeries.DefaultLength;

        internal static readonly int[] Flavours = CausalityProbeSeries.HourlyFlavours;

        /// <summary>
        /// Shared with the provider-specific causality pins (see <c>DivergenceConfirmLagTests</c>)
        /// so both are talking about the same price action.
        /// </summary>
        internal static List<Ohlcv> Bars(int flavour, int length = SeriesLength) =>
            CausalityProbeSeries.Bars(flavour, length);

        private static readonly int[] PrefixLengths = { 60, 80, 110, 140, 170, 200, 240, 280, 320, 360, 390 };

        private static Dictionary<string, object> Defaults(IndicatorMetadata m)
        {
            var d = new Dictionary<string, object>();
            foreach (var p in m.Parameters) d[p.Name] = p.DefaultValue;
            return d;
        }

        private static Dictionary<string, double[]> Run(IIndicatorProvider provider, IndicatorMetadata ind,
            Dictionary<string, object> pars, List<Ohlcv> bars)
        {
            var buffer = new IndicatorResultBuffer(new Dictionary<string, double[]>(), bars.Count);
            provider.Calculate(ind.Code, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars), pars, buffer);
            return buffer.GetResults();
        }

        private static bool Same(double a, double b) =>
            (double.IsNaN(a) && double.IsNaN(b)) || Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(a));

        // ── The contract ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void EveryComponentDeclaresItsCausality()
        {
            var undeclared = new List<string>();

            foreach (var type in ProviderTypes())
            {
                var provider = Create(type);
                List<IndicatorMetadata> indicators;
                try { indicators = provider.GetIndicators(); } catch { continue; }

                foreach (var ind in indicators)
                    foreach (var comp in ind.Components)
                        if (CausalityContract.Effective(ind, comp) == ComponentCausality.Undeclared)
                            undeclared.Add($"{ind.Code}.{comp.Name} ({type.Name})");
            }

            Assert.True(undeclared.Count == 0,
                "These components declare no causality, so SignalCatalog will refuse to publish them " +
                "and they will silently vanish from the strategy builder. Set Causality on the " +
                "component, or on its IndicatorMetadata to cover the whole indicator:\n  " +
                string.Join("\n  ", undeclared));
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public void ComponentsDeclaredCausalGiveTheSameAnswerOnAPrefix(Type providerType)
        {
            var provider = Create(providerType);
            List<IndicatorMetadata> indicators;
            try { indicators = provider.GetIndicators(); } catch { return; }

            var offenders = new List<string>();

            foreach (var ind in indicators)
            {
                var causal = ind.Components
                    .Where(c => CausalityContract.Effective(ind, c) == ComponentCausality.Causal)
                    .Select(c => c.Name)
                    .ToHashSet(StringComparer.Ordinal);
                if (causal.Count == 0) continue;

                var pars = Defaults(ind);

                foreach (int flavour in Flavours)
                {
                    var full = Bars(flavour);
                    Dictionary<string, double[]> whole;
                    try { whole = Run(provider, ind, pars, full); }
                    catch (Exception ex)
                    {
                        // A provider that cannot run on synthetic bars at all is a coverage gap, not
                        // a causality failure — the blind-spot test below is where that is tracked.
                        offenders.Add($"{ind.Code}: threw on the full series — {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    foreach (int k in PrefixLengths)
                    {
                        Dictionary<string, double[]> prefix;
                        try { prefix = Run(provider, ind, pars, full.Take(k).ToList()); }
                        catch (Exception ex)
                        {
                            offenders.Add($"{ind.Code}: threw on the first {k} bars but not on {SeriesLength} — " +
                                          $"{ex.GetType().Name}: {ex.Message}");
                            continue;
                        }

                        foreach (var (name, shortRun) in prefix)
                        {
                            if (!causal.Contains(name)) continue;   // companion array, or declared Lookahead
                            if (!whole.TryGetValue(name, out var longRun)) continue;

                            int len = Math.Min(k, Math.Min(longRun.Length, shortRun.Length));
                            for (int i = 0; i < len; i++)
                            {
                                if (Same(longRun[i], shortRun[i])) continue;
                                offenders.Add(
                                    $"{ind.Code}.{name} (series {flavour}): bar {i} reads " +
                                    $"{shortRun[i]:G6} when {k} bars are loaded and {longRun[i]:G6} when " +
                                    $"{SeriesLength} are. Bar {i} cannot depend on bar {k} or later.");
                                break;   // one line per component per k is enough to act on
                            }
                        }
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                $"{providerType.Name} has components declared Causal whose value at a bar changes " +
                $"when later bars arrive. Either the maths reads ahead, or a parameter is derived " +
                $"from data.Length, or the component should be declared Lookahead:\n  " +
                string.Join("\n  ", offenders.Distinct()));
        }

        // ── The other half of the contract: older bars arriving ───────────────────────────────

        /// <summary>
        /// A longer series for the suffix test than for the prefix one. The widest trailing window
        /// among the built-in defaults is a 500-bar percentile rank, and until that window is full
        /// a short run is genuinely entitled to a different answer — so the comparison has to start
        /// after it, and there has to be a useful stretch of series left afterwards to compare.
        /// </summary>
        private const int SuffixSeriesLength = 1400;

        /// <summary>
        /// How many bars of history the suffix run is allowed to spend warming up before its
        /// answers have to agree with the full run's. Everything before this index in the SHORT
        /// series is skipped: an indicator legitimately knows less at the left edge of its data
        /// than it does in the middle of a longer series, and that is warmup, not a defect.
        /// </summary>
        private const int SuffixWarmup = 700;

        /// <summary>How many bars are chopped off the FRONT — i.e. how much history a scroll-back
        /// prepend brings in. Several sizes because a bucketing bug (weeks aligned to index 0)
        /// disagrees for some offsets and agrees for others.</summary>
        private static readonly int[] SuffixDrops = { 17, 40, 91, 140 };

        /// <summary>The three hourly series plus the irregular one — see <see cref="Stamp"/> for
        /// why a guard about array anchoring needs a series whose bars are not evenly spaced.</summary>
        private static readonly int[] SuffixFlavours = CausalityProbeSeries.AllFlavours;

        /// <summary>
        /// Relative tolerance for the suffix comparison, four orders of magnitude looser than the
        /// prefix test's 1e-9, and the looseness is the point.
        ///
        /// <para>
        /// Every EMA and every Wilder average in this codebase is a recursive filter seeded at the
        /// first bar it is handed. Start the same filter 91 bars earlier and it carries a different
        /// seed, so the two runs never become bit-identical — they converge. That is not a defect
        /// and there is no fix for it short of not using EMAs. What IS a defect is a bucket, a
        /// sample window, or an accumulator pinned to array index 0, because that does not converge
        /// at all: it re-cuts, and the disagreement stays the same size forever.
        /// </para>
        ///
        /// <para>
        /// Measured over 700 bars of settling, those two populations separate by five orders of
        /// magnitude — every converging filter here lands under 5e-7 and every anchoring defect
        /// above 1e-2 — so 1e-6 splits them cleanly with room on both sides. The long-period
        /// exceptions (a 200-bar EMA has barely three time constants in 700 bars) are named in
        /// <see cref="NotStableWhenHistoryIsPrepended"/> rather than papered over by loosening this
        /// further.
        /// </para>
        /// </summary>
        private const double SuffixTolerance = 1e-6;

        private static bool SameWithinSuffixTolerance(double a, double b)
        {
            if (double.IsNaN(a) && double.IsNaN(b)) return true;
            if (double.IsNaN(a) || double.IsNaN(b)) return false;
            return Math.Abs(a - b) <= SuffixTolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
        }

        /// <summary>
        /// Components whose value at a bar changes when OLDER bars are prepended. Two populations,
        /// and the difference between them matters more than the list does.
        ///
        /// <para>
        /// <b>Inherent.</b> A cumulative sum has no start other than the start of the data, and a
        /// long recursive filter has not finished forgetting its seed after 700 bars. Nothing to
        /// fix; the entry records why.
        /// </para>
        ///
        /// <para>
        /// <b>DEFECT.</b> Something is pinned to array index 0 and re-cuts when the array start
        /// moves. Each of these is filed in docs/TODO.md under the prepend-causality section and
        /// each is a bar on the user's chart silently changing its answer during a scroll-back.
        /// They sit here so the guard is green on everything else — not because they are accepted.
        /// </para>
        /// </summary>
        private static readonly string[] NotStableWhenHistoryIsPrepended =
        {
            // ── Inherent: cumulative from the start of the data ──────────────────────────────
            "Adl.Adl",                  // running accumulation/distribution line, unanchored by definition
            "Adl.AdlSma",               // an SMA of the above, so it inherits the offset
            "Obv.Obv",                  // running on-balance volume, same
            "Vwap.Vwap",                // volume-weighted mean from the first bar held

            // ── Inherent: a long recursive filter still forgetting its seed at 700 bars ──────
            // Everything shorter than these HAS converged inside the tolerance and is guarded.
            "PULSE.AnchorMtf",          // Wilder RSI over weekly buckets — only ~100 weekly bars in 700
            "PULSE.AnchorSlope",        // slope of an EMA-smoothed anchor, so twice removed from its seed
            "PULSE.AnchorSlow",
            "SPIDER_LINES.EMA 144",
            "SPIDER_LINES.EMA 200",     // three time constants in 700 bars is not enough to reach 1e-6
            "SPIDER_LINES.Stacking Score",   // ranks those EMAs; flips wherever two sit inside the residue
            "REGIME.AboveEma200",            // boolean over a 200-EMA, same knife edge

            // ── DEFECT: anchored to array index 0, filed in docs/TODO.md ─────────────────────
            "CIPHER_S.Candle Phase",
            "LOUKAS_CYCLES.IC DC Count",     // counts cycles since bar 0, so prepending adds cycles to every count
            "LOUKAS_CYCLES.ICL Confirmed",
            "TOP_BOTTOM_DETECTOR.Distribution Confidence",
            "VALUE_DEVIATION.DeviationTier",
            "VALUE_DEVIATION.ResistanceDeep",
            "VALUE_DEVIATION.ResistanceMid",
            "VALUE_DEVIATION.ResistanceShallow",
            "VALUE_DEVIATION.SupportDeep",
            "VALUE_DEVIATION.SupportMid",
            "VALUE_DEVIATION.SupportShallow",
            "VALUE_DEVIATION.ValueHigh",
            "VALUE_DEVIATION.ValueLow",
            "VALUE_DEVIATION.ValuePoc",
        };

        /// <summary>
        /// The prefix test above only ever APPENDS bars: it asks whether bar i changes when the
        /// future arrives. That is half the question, and in this app it is the rarer half — the
        /// normal event here is a scroll-back, where the user pans left and two hundred OLDER bars
        /// arrive in front of everything already on the chart. If bar i answers differently after
        /// that, the chart rewrites its own past under the user, a backtest disagrees with the
        /// live chart it was run from, and neither is detectably wrong from the inside.
        ///
        /// <para>
        /// So: run the provider on all 400 bars, run it again on <c>bars.Skip(k)</c>, and require
        /// the two to agree on every shared bar past <see cref="SuffixWarmup"/>. The short run is
        /// the chart before the prepend; the long run is the same chart after it.
        /// </para>
        ///
        /// <para>
        /// This finds a class the prefix sweep structurally cannot: anything anchored to array
        /// index 0. A bucket built as <c>i / barsPerWeek</c> re-cuts every week when the array
        /// start moves; a sample window of <c>Math.Min(100, n - 1)</c> taken from the FRONT of the
        /// array reads different bars; a session accumulator that starts at index 0 rather than at
        /// a session boundary measures a truncated first session. All three are in this codebase,
        /// all three pass the prefix test, and all three change an existing bar's answer.
        /// </para>
        /// </summary>
        [Theory]
        [MemberData(nameof(Providers))]
        public void ComponentsDeclaredCausalGiveTheSameAnswerWhenOlderBarsArrive(Type providerType)
        {
            var provider = Create(providerType);
            List<IndicatorMetadata> indicators;
            try { indicators = provider.GetIndicators(); } catch { return; }

            var exempt = NotStableWhenHistoryIsPrepended.ToHashSet(StringComparer.Ordinal);
            var offenders = new List<string>();

            foreach (var ind in indicators)
            {
                var causal = ind.Components
                    .Where(c => CausalityContract.Effective(ind, c) == ComponentCausality.Causal)
                    .Select(c => c.Name)
                    .ToHashSet(StringComparer.Ordinal);
                if (causal.Count == 0) continue;

                var pars = Defaults(ind);

                foreach (int flavour in SuffixFlavours)
                {
                    var full = Bars(flavour, SuffixSeriesLength);
                    Dictionary<string, double[]> whole;
                    try { whole = Run(provider, ind, pars, full); }
                    catch { continue; }   // tracked by the prefix test and the blind-spot list

                    foreach (int k in SuffixDrops)
                    {
                        Dictionary<string, double[]> suffix;
                        try { suffix = Run(provider, ind, pars, full.Skip(k).ToList()); }
                        catch (Exception ex)
                        {
                            offenders.Add($"{ind.Code}: threw on the last {SuffixSeriesLength - k} bars but not " +
                                          $"on {SuffixSeriesLength} — {ex.GetType().Name}: {ex.Message}");
                            continue;
                        }

                        foreach (var (name, shortRun) in suffix)
                        {
                            if (!causal.Contains(name)) continue;
                            if (exempt.Contains($"{ind.Code}.{name}")) continue;
                            if (!whole.TryGetValue(name, out var longRun)) continue;

                            int len = Math.Min(shortRun.Length, longRun.Length - k);
                            for (int j = SuffixWarmup; j < len; j++)
                            {
                                if (SameWithinSuffixTolerance(longRun[j + k], shortRun[j])) continue;
                                offenders.Add(
                                    $"{ind.Code}.{name} (series {flavour}): the bar at full-series index " +
                                    $"{j + k} reads {shortRun[j]:G6} before {k} older bars are prepended and " +
                                    $"{longRun[j + k]:G6} after. Prepending history cannot change a bar that " +
                                    $"was already on the chart.");
                                break;
                            }
                        }
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                $"{providerType.Name} has components declared Causal whose value at a bar changes when " +
                $"OLDER bars arrive — a scroll-back rewrites the chart's past. Usually something is " +
                $"anchored to array index 0 (a bucket, a sample window, a session start) instead of to a " +
                $"bar date. If it is inherent, add it to NotStableWhenHistoryIsPrepended with a reason:\n  " +
                string.Join("\n  ", offenders.Distinct()));
        }

        /// <summary>
        /// The suffix guard compares two runs bar by bar, and NaN equals NaN — so a version of it
        /// that compared nothing but warmup padding would be just as green as this one. This says
        /// out loud how many real numbers it actually looked at, and it fails if the exemption list
        /// keeps naming a component that has since been fixed.
        /// </summary>
        [Fact]
        public void TheSuffixGuardComparesRealNumbersAndItsExemptionsAreStillEarned()
        {
            int compared = 0;
            var componentsSeen = new HashSet<string>(StringComparer.Ordinal);
            var stillUnstable = new HashSet<string>(StringComparer.Ordinal);

            foreach (var type in ProviderTypes())
            {
                var provider = Create(type);
                List<IndicatorMetadata> indicators;
                try { indicators = provider.GetIndicators(); } catch { continue; }

                foreach (var ind in indicators)
                {
                    var causal = ind.Components
                        .Where(c => CausalityContract.Effective(ind, c) == ComponentCausality.Causal)
                        .Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
                    if (causal.Count == 0) continue;

                    var pars = Defaults(ind);

                    foreach (int flavour in SuffixFlavours)
                    {
                        var full = Bars(flavour, SuffixSeriesLength);
                        Dictionary<string, double[]> whole;
                        try { whole = Run(provider, ind, pars, full); } catch { continue; }

                        foreach (int k in SuffixDrops)
                        {
                            Dictionary<string, double[]> suffix;
                            try { suffix = Run(provider, ind, pars, full.Skip(k).ToList()); } catch { continue; }

                            foreach (var (name, shortRun) in suffix)
                            {
                                if (!causal.Contains(name)) continue;
                                if (!whole.TryGetValue(name, out var longRun)) continue;

                                string id = $"{ind.Code}.{name}";
                                int len = Math.Min(shortRun.Length, longRun.Length - k);
                                for (int j = SuffixWarmup; j < len; j++)
                                {
                                    if (double.IsNaN(shortRun[j]) && double.IsNaN(longRun[j + k])) continue;
                                    compared++;
                                    componentsSeen.Add(id);
                                    if (!SameWithinSuffixTolerance(longRun[j + k], shortRun[j]))
                                        stillUnstable.Add(id);
                                }
                            }
                        }
                    }
                }
            }

            // Floors, not exact counts: a new indicator should raise these, never trip them. The
            // numbers are roughly half of what the sweep currently reaches, so ordinary additions
            // and retirements do not churn this file.
            Assert.True(compared > 500_000,
                $"The suffix guard only compared {compared} non-NaN values. It is passing because it " +
                $"is looking at warmup padding, not because the indicators are stable.");
            Assert.True(componentsSeen.Count > 150,
                $"Only {componentsSeen.Count} components produced a comparable value past bar " +
                $"{SuffixWarmup}. Either the synthetic series got shorter or the warmup got longer.");

            var stale = NotStableWhenHistoryIsPrepended.Except(stillUnstable).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.True(stale.Count == 0,
                "These are excused from the suffix guard but no longer need to be — whatever anchored " +
                "them to array index 0 has been fixed. Delete them from " +
                "NotStableWhenHistoryIsPrepended so the guard covers them again:\n  " +
                string.Join("\n  ", stale));
        }

        // ── The gate ──────────────────────────────────────────────────────────────────────────

        [Fact]
        public void TheCatalogPublishesOnlyWhatWasDeclaredCausal()
        {
            var catalog = new SignalCatalog(AllProviders());

            var leaked = catalog.All.Where(d => d.Causality != ComponentCausality.Causal).ToList();
            Assert.True(leaked.Count == 0,
                "SignalCatalog offered these as strategy signals despite their declaration:\n  " +
                string.Join("\n  ", leaked.Select(d => $"{d.Id} ({d.Causality})")));

            Assert.All(catalog.Excluded, d => Assert.NotNull(catalog.RefusalReason(d.Id)));
        }

        [Theory]
        // The five look-ahead criticals from the 2026-08-21 audit, each pinned by ID. Every one of
        // these was a live strategy leaf.
        [InlineData("ICHIMOKU.Chikou Span")]        // close[j + 26], published as a comparable line
        [InlineData("SWING_STRUCTURE.SwingHigh")]   // stamped Span bars before it could be known
        [InlineData("SWING_STRUCTURE.SwingLow")]
        [InlineData("CIPHER_SR.Resistance")]        // pivot dot, knowable PivotBars later
        [InlineData("CIPHER_SR.Support")]
        [InlineData("Dpo.Dpo")]                     // centred by definition
        public void KnownLookaheadComponentsAreNotOfferedAsSignals(string id)
        {
            var catalog = new SignalCatalog(AllProviders());

            Assert.DoesNotContain(catalog.All, d => d.Id == id);

            // Still resolvable, deliberately: a strategy saved before the gate existed has to be
            // able to say why its leaf stopped firing, and "unknown signal" would be a lie.
            var descriptor = catalog.GetById(id);
            Assert.NotNull(descriptor);
            Assert.Equal(ComponentCausality.Lookahead, descriptor!.Causality);
            Assert.Contains("would read the future", catalog.RefusalReason(id));
        }

        [Fact]
        public void TheCausalFormOfEachRefusedComponentIsStillAvailable()
        {
            // Refusing a leaf is only acceptable because the honest version of the same information
            // is published. Losing the ability to say "price is near resistance" would be a worse
            // outcome than the look-ahead was.
            var ids = new SignalCatalog(AllProviders()).All.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

            Assert.Contains("CIPHER_SR.Resistance Zone", ids);   // the carry-forward level, confirmation-gated
            Assert.Contains("CIPHER_SR.Support Zone", ids);
            Assert.Contains("SWING_STRUCTURE.LastSwingHigh", ids);
            Assert.Contains("SWING_STRUCTURE.LastSwingLow", ids);
            Assert.Contains("SWING_STRUCTURE.StructureState", ids);
            Assert.Contains("ICHIMOKU.Kijun-sen", ids);
        }

        private static List<IIndicatorProvider> AllProviders() => IndicatorProviderFixture.AllProviders();

        // ── The guard's own blind spots ───────────────────────────────────────────────────────

        /// <summary>
        /// Components that are declared Causal but that the two synthetic series never produce a
        /// single value for. The prefix test cannot fail for these, so their declaration rests on
        /// reading the code rather than on evidence, and that is worth stating out loud rather than
        /// counting as coverage.
        ///
        /// <para>
        /// Four reasons appear here, and only one of them is benign:
        /// </para>
        /// <list type="bullet">
        /// <item>Drawn elsewhere — CANDLES/PRICE/VOLUME/HEATMAP and the three profile indicators are
        /// filled by the orchestrator, not by the provider's Calculate.</item>
        /// <item>Needs data this harness has none of — everything sourced from outside the OHLCV
        /// series it is drawn on: a second symbol (BTC_STRENGTH, COMPARE), a snapshot file, or a
        /// cross-series cache that is a substitute here (COINMETRICS, COT_POSITIONING, FUNDING_RATE,
        /// OPEN_INTEREST, CROWDING_INDEX, FEAR_GREED, and the three StrategyLab providers). Their
        /// declarations are read off the code; the prefix test cannot reach them until the harness
        /// can feed them. Several of them are separately suspected of look-ahead in docs/TODO.md —
        /// CoinMetrics stamps a daily metric at the START of the day it summarises, and the CFTC
        /// release date is synthesised rather than sourced — which is exactly the kind of thing
        /// this test would find if it could see them.</item>
        /// <item>Genuinely rare — a marker that these 800 bars never fired.</item>
        /// <item>BROKEN, and this list is where it shows: ten Skender indicators resolve to no
        /// method at all (Bb, Kc, UltOsc, Ppo, Zlema, Tma, ChandelierExit, Hv, Eom, Mom — Bollinger
        /// Bands ships in the default demo set), and another handful declare component names their
        /// calculation never writes (Adx.Adl/Adh, Stoch.PercentK/D, Vortex.Vip/Vim, Chop.ChopIndex,
        /// Roc.RocP, UlcerIndex, Adl.Adl3, Trix.Signal). Those are separate findings in docs/TODO.md
        /// under "Ship-blockers — indicator computation"; when one is fixed, its entry here must be
        /// deleted and this test will say so.</item>
        /// </list>
        /// </summary>
        // Shrunk 2026-08-22: twenty-three of these were blind not because the synthetic series
        // avoided them but because the components produced NOTHING AT ALL — a misnamed Skender
        // method, a component name the result type does not expose, or an optional Nullable
        // parameter the binder could never pass. A component with no values cannot have its
        // causality checked either, so fixing the blanks is what made them testable.
        private static readonly string[] NotExercisedByTheseSeries =
        {
            "BNVISION_FUNDING.Funding",
            "BNVISION_FUNDING.FundingExtreme",
            "BNVISION_FUNDING.FundingZScore",
            "BNVISION_OI.Oi",
            "BNVISION_OI.OiDeltaPct",
            "BNVISION_OI.OiDeltaZScore",
            "BNVISION_OI.PriceOiAlign",
            "BTC_STRENGTH.BtcRatio",
            "BTC_STRENGTH.BtcRatioMomentum",
            "CANDLES.body",
            "CANDLES.lower_wick",
            "CANDLES.upper_wick",
            "CFTC_COT.NetExtreme",
            "CFTC_COT.NetPctOi",
            "CFTC_COT.NetZScore",
            "CIPHER_A.Bearish Divergence",
            "CIPHER_C.Bottom Double",
            "CIPHER_C.Shallow Peak",
            "CIPHER_C.Shallow Trough",
            "CIPHER_C.Top Double",
            "COINMETRICS.ActiveAddresses",
            "COINMETRICS.ActiveAddressesZ",
            "COINMETRICS.HashRate",
            "COINMETRICS.MVRV",
            "COINMETRICS.MVRVRegime",
            "COINMETRICS.MVRVZ",
            "COMPARE.Compare",
            "COMPARE_RATIO.Compare",
            "COT_POSITIONING.Crowded Long",
            "COT_POSITIONING.Crowded Short",
            "COT_POSITIONING.Net % of OI",
            "COT_POSITIONING.Positioning Z-Score",
            "CROWDING_INDEX.Crowding Score",
            "CROWDING_INDEX.Long Crowded",
            "CROWDING_INDEX.Short Crowded",
            "Eom.Eom",
            "FEAR_GREED.Extreme Fear",
            "FEAR_GREED.Extreme Greed",
            "FEAR_GREED.Sentiment",
            "FEAR_GREED.Sentiment Flip",
            "FUNDING_RATE.Extreme Long",
            "FUNDING_RATE.Extreme Short",
            "FUNDING_RATE.Funding Rate",
            "FUNDING_RATE.Sign Flip",
            "HEATMAP.Liquidity",
            "Hv.Hv",
            "LOUKAS_CYCLES.FY Day Count",
            "LOUKAS_CYCLES.FY Phase",
            "OPEN_INTEREST.OI Delta",
            "OPEN_INTEREST.OI Divergence",
            "OPEN_INTEREST.OI Spike",
            "OPEN_INTEREST.OI Value",
            "PRICE.line",
            "PULSE.GoldenDot",
            "PULSE.RedDot",
            "Ppo.Histogram",
            "Ppo.Ppo",
            "Ppo.Signal",
            "TOP_BOTTOM_DETECTOR.Bottom Confirmed",
            "TPO.Profile",
            "Tma.Tma",
            "VOLUME.Volume",
            "VPFR.Profile",
            "VPVR.Profile",
            "Zlema.Zlema",
        };

        [Fact]
        public void TheBlindSpotsOfThisGuardAreTheOnesWeKnowAbout()
        {
            var exercised = new HashSet<string>(StringComparer.Ordinal);
            var causal = new List<string>();

            foreach (var type in ProviderTypes())
            {
                var provider = Create(type);
                List<IndicatorMetadata> indicators;
                try { indicators = provider.GetIndicators(); } catch { continue; }

                foreach (var ind in indicators)
                {
                    foreach (var c in ind.Components)
                        if (CausalityContract.Effective(ind, c) == ComponentCausality.Causal)
                            causal.Add($"{ind.Code}.{c.Name}");

                    var pars = Defaults(ind);
                    foreach (int flavour in Flavours)
                    {
                        Dictionary<string, double[]> results;
                        try { results = Run(provider, ind, pars, Bars(flavour)); } catch { continue; }

                        foreach (var comp in ind.Components)
                            if (results.TryGetValue(comp.Name, out var arr) && arr.Any(v => !double.IsNaN(v)))
                                exercised.Add($"{ind.Code}.{comp.Name}");
                    }
                }
            }

            var blind = causal.Where(id => !exercised.Contains(id))
                              .OrderBy(x => x, StringComparer.Ordinal).ToList();
            var known = NotExercisedByTheseSeries.OrderBy(x => x, StringComparer.Ordinal).ToList();

            var newlyBlind = blind.Except(known).ToList();
            Assert.True(newlyBlind.Count == 0,
                "These components are declared Causal but produce no value on either synthetic " +
                "series, so nothing verifies the declaration. Either give the generator in this " +
                "file the price action they need, or add them to NotExercisedByTheseSeries with a " +
                "reason:\n  " + string.Join("\n  ", newlyBlind));

            var nowCovered = known.Except(blind).ToList();
            Assert.True(nowCovered.Count == 0,
                "These are listed as blind spots but the series now exercise them — the prefix test " +
                "is checking them for real. Delete them from NotExercisedByTheseSeries so the list " +
                "keeps meaning what it says:\n  " + string.Join("\n  ", nowCovered));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Xunit;

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
        // Three characters, because pivot and divergence markers are sparse: a rolling swing
        // series, a faster and more violent one, and a steady uptrend that alternates loud and
        // quiet swings. The third earns its place — it was the one that caught SwingStructure
        // reordering a bar that was both a pivot high and a pivot low. Deterministic (an xorshift
        // seeded per flavour) so a failure reproduces exactly and the blind-spot list below keeps
        // meaning what it says.

        private const int SeriesLength = 400;

        internal static readonly int[] Flavours = { 0, 1, 2 };

        /// <summary>
        /// Shared with the provider-specific causality pins (see <c>DivergenceConfirmLagTests</c>)
        /// so both are talking about the same price action.
        /// </summary>
        internal static List<Ohlcv> Bars(int flavour)
        {
            var bars = new List<Ohlcv>(SeriesLength);
            double price = 100;
            ulong s = flavour switch
            {
                0 => 0x9E3779B97F4A7C15UL,
                1 => 0xD1B54A32D192ED03UL,
                _ => 0xBF58476D1CE4E5B9UL,
            };
            double Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s % 10000) / 10000.0; }
            var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < SeriesLength; i++)
            {
                // Flavour 2: a steady rise whose swings alternate loud and quiet. WaveTrend
                // normalises by its own trailing deviation, so a quiet stretch that follows a loud
                // one prints muted oscillator peaks while price keeps making higher highs — which
                // is a plain bearish divergence rather than an overbought one.
                double amp = (i % 80) < 40 ? 3.4 : 0.7;
                double drift = flavour switch
                {
                    0 => Math.Sin(i / 11.0) * 1.1 + Math.Sin(i / 37.0) * 2.4 + Math.Sin(i / 91.0) * 3.6,
                    1 => Math.Sin(i / 6.0) * 2.6 + Math.Sin(i / 23.0) * 1.2 + (i % 130 < 65 ? 0.9 : -1.1),
                    _ => 0.5 + Math.Sin(i / 9.0) * amp,
                };
                double shock = (Next() - 0.5) * (flavour == 1 ? 5.0 : 2.2);
                double open = price;
                price = Math.Max(1.0, price + drift + shock);
                double close = price;
                double hi = Math.Max(open, close) + Next() * 1.4;
                double lo = Math.Min(open, close) - Next() * 1.4;
                // Volume rises with the size of the move so turning points carry the confluence
                // volume that pivot detectors require before they will call a pivot.
                double vol = 1000 + Math.Abs(drift + shock) * 900 + Next() * 3000;
                bars.Add(new Ohlcv(start.AddHours(i), open, hi, lo, close, vol));
            }
            return bars;
        }

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

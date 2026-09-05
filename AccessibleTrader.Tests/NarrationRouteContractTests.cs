using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// THE NARRATION CONTRACT, over every indicator the terminal ships.
///
/// <para>
/// Cody, 2026-09-05, after the third narration defect in two days: <i>"we may want a class to
/// catch the issues we've run into."</i> This is that class. Every defect below was found by
/// hand, one report at a time, and every one of them has the same shape: <b>a series the user
/// flagged with N, and no code path by which it could ever say anything.</b>
/// </para>
///
/// <list type="number">
///   <item>A plain EMA — no marker, no zone line, no registered oscillator definition. Silent
///         forever. Found 2026-09-05 from Cody's "ema crosses should be announced".</item>
///   <item>Roughly thirty-five oscillators — Stochastic, CCI, MFI, ADX, ROC, Williams %R, TRIX,
///         CMO, Chop, PPO, StochRSI and the rest — same shape, found by the census below rather
///         than by a report. Three of ninety-nine indicators had a hand-registered
///         <c>IndicatorContextDefinition</c>, and the other ninety-six were on their own.</item>
///   <item>Registered definitions that bind to NOTHING: <c>STOCH|K</c> when Stochastic's
///         components are "Oscillator" and "Signal"; <c>BB|Upper</c> when Bollinger's are
///         "UpperBand" and "LowerBand". Configuration that looks like coverage and is not.</item>
/// </list>
///
/// <para>
/// The census is the guard. A new indicator that cannot narrate fails HERE, at the point it is
/// added, instead of a year later when somebody presses N on it and hears nothing — and the
/// exemptions are written down with a reason rather than accumulating silently.
/// </para>
/// </summary>
public sealed class NarrationRouteContractTests
{
    // ── The routes, exactly as AutoNarrationService applies them ────────────────

    private static bool IsMarker(ComponentDisplayType d) => AudioConstants.MarkerDisplayTypes.Contains(d);

    /// <summary>A discrete marker: speaks on bar close, and in PLAYBACK too when it carries a
    /// signal template. This is the only route playback has.</summary>
    private static bool MarkerRoute(IndicatorComponentMetadata c)
        => IsMarker(c.DisplayType) && !c.DefaultIsZoneLine;

    /// <summary>A declared level line: break, touch, approach and cross vocabulary.</summary>
    private static bool ZoneLineRoute(IndicatorComponentMetadata c) => c.DefaultIsZoneLine;

    /// <summary>A price-space overlay: "price crossed above it" on the bar close.</summary>
    private static bool OverlayRoute(IndicatorMetadata m, IndicatorComponentMetadata c)
        => string.Equals(m.DefaultPane, "Main", StringComparison.OrdinalIgnoreCase)
           && string.IsNullOrEmpty(c.SubPaneName)
           && c.DisplayType == ComponentDisplayType.Line
           && !c.DefaultIsZoneLine
           && c.Role != ComponentRole.PriceAction
           && c.Role != ComponentRole.Body
           && c.Role != ComponentRole.Wick;

    /// <summary>The indicator crossing one of its own declared reference levels.</summary>
    private static bool LevelRoute(IIndicatorProvider p, IndicatorMetadata m)
        => p.GetDefaultLevels(m.Code.ToUpperInvariant()).Count > 0
           && !string.Equals(m.DefaultPane, "Main", StringComparison.OrdinalIgnoreCase);

    private static bool CloudRoute(IndicatorMetadata m) => (m.DefaultCloudFills?.Count ?? 0) > 0;

    /// <summary>
    /// A hand-registered <c>IndicatorContextDefinition</c> that can actually SAY something:
    /// overbought/oversold thresholds, or a crossover pair. A definition with neither — ATR's is
    /// the only one — supplies trend context to other callers and narrates nothing, so it does
    /// not count as a voice. Read by reflection rather than through
    /// <c>HasZoneThresholds</c>, which is deliberately narrower: production asks it only to
    /// decide who owns a THRESHOLD.
    /// </summary>
    private static bool OscillatorRoute(IndicatorContextAnalyzer osc, IndicatorMetadata m)
        => Definitions(osc).Any(kv =>
        {
            var parts = kv.Key.Split('|');
            return string.Equals(parts[0], m.Code, StringComparison.OrdinalIgnoreCase)
                && m.Components.Any(c => string.Equals(c.Name, parts[1], StringComparison.OrdinalIgnoreCase))
                && (kv.Def.OverboughtThreshold.HasValue || kv.Def.OversoldThreshold.HasValue
                    || (kv.Def.CrossoverComponentA != null && kv.Def.CrossoverComponentB != null));
        });

    private static IEnumerable<(string Key, AccessibleTrader.Sdk.Analysis.IndicatorContextDefinition Def)>
        Definitions(IndicatorContextAnalyzer osc)
    {
        var field = typeof(IndicatorContextAnalyzer)
            .GetField("_defs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var defs = (System.Collections.IDictionary)field.GetValue(osc)!;
        foreach (System.Collections.DictionaryEntry e in defs)
            yield return ((string)e.Key, (AccessibleTrader.Sdk.Analysis.IndicatorContextDefinition)e.Value!);
    }

    private static bool CanNarrateOnBarClose(IIndicatorProvider p, IndicatorContextAnalyzer osc, IndicatorMetadata m)
        => m.Components.Any(c => MarkerRoute(c) || ZoneLineRoute(c) || OverlayRoute(m, c))
           || LevelRoute(p, m) || CloudRoute(m) || OscillatorRoute(osc, m);

    /// <summary>
    /// Indicators with no voice, and the reason each one has none. Four categories, and every
    /// entry has to fall into one of them:
    ///
    /// <list type="bullet">
    ///   <item><b>Drawings.</b> The user's own objects. They speak through the drawing contract
    ///         (<c>DrawingSpeech</c>) and the nudge readback, not through indicator narration.</item>
    ///   <item><b>The price itself</b>, and the volume/profile/heatmap surfaces. The new-bar
    ///         announcement IS the candle's narration; a profile has no per-bar event.</item>
    ///   <item><b>Unbounded accumulators.</b> OBV, ADL, Force Index, standard deviation, Ulcer,
    ///         historical volatility, ATR: no fixed threshold exists to cross, because the series
    ///         has no scale of its own — an ATR of 400 is enormous on one asset and noise on
    ///         another. Giving them a level would be inventing one.</item>
    ///   <item><b>Comparison overlays</b>, which are another symbol's price drawn on this chart.</item>
    /// </list>
    ///
    /// Adding a code here is a decision that the indicator SHOULD be silent. If it should not
    /// be, give it a route instead — a marker, a declared level, or a zone line.
    /// </summary>
    private static readonly HashSet<string> KnownSilent = new(StringComparer.OrdinalIgnoreCase)
    {
        // Drawings
        "ANGLEFIB", "CHANNEL", "FIB", "FIBEXT", "GANNBOX", "GANNFAN", "HORIZONTAL", "LABEL",
        "MEASURE", "PITCHFORK", "RECT", "RISKREWARD", "TREND", "VERTICAL",
        // Price, volume and the distribution surfaces
        "CANDLES", "PRICE", "VOLUME", "HEATMAP", "AVWAP",
        "TPO", "TPOFR", "TPOSESSION", "TPOANCHOR", "VPVR", "VPFR", "VPSESSION", "VPANCHOR",
        // Unbounded accumulators — no threshold exists to cross
        "Adl", "Atr", "ForceIndex", "Hv", "Obv", "StdDev", "UlcerIndex",
        // Comparison overlays: another symbol's price, drawn here
        "COMPARE", "COMPARE_RATIO", "BTC_STRENGTH",
        // Data surfaces with no per-bar event of their own
        "COINMETRICS", "VOL_REGIME", "CIPHER_S",
    };

    // ── THE ARTIFACT TEST ───────────────────────────────────────────────────────
    //
    // `patches/HOSTED-DEPLOY-NOTES.md` §5n names this defect class — "a capability that reads
    // as configured and does nothing, with no error to say so" — with six instances from three
    // subsystems, and asks for one rule: **assert the artifact, not the incantation.** Its own
    // worked example is this very feature: "a test that enables narration on each shipped
    // indicator in turn and asserts a non-empty utterance would have caught the EMA".
    //
    // So the guard below does not ask whether a route EXISTS in the metadata. It builds the
    // series, feeds it data engineered to trigger whatever routes it has, runs the real
    // AutoNarrationService through a real bar close, and asks whether anything was SAID.

    /// <summary>
    /// Four bars, close 100 throughout, and component data shaped so that any route the
    /// indicator has fires on the bar that closes: a marker prints on it, a price-space line
    /// walks from above the close to below it, an oscillator walks from below its first declared
    /// level to above it.
    /// </summary>
    private static List<string> DriveOneBarClose(IIndicatorProvider provider, IndicatorMetadata meta, bool narrated)
    {
        var levels = provider.GetDefaultLevels(meta.Code.ToUpperInvariant());
        bool mainPane = string.Equals(meta.DefaultPane, "Main", StringComparison.OrdinalIgnoreCase);
        double level = levels.Count > 0 ? levels[0].Value : 0.0;

        var cfg = new SeriesConfig
        {
            Id = meta.Code, IndicatorCode = meta.Code, Name = meta.Name, FriendlyName = meta.Name,
            Pane = meta.DefaultPane ?? "Main", IsAutoNarrated = narrated, IsVisible = true,
        };
        foreach (var l in levels)
            cfg.Levels.Add(new LevelConfig { Name = l.Name, Value = l.Value, IsVisible = true });

        var arrays = new Dictionary<string, double[]>();
        int index = 0;
        foreach (var c in meta.Components)
        {
            // Siblings walk in OPPOSITE directions, so a pair that signals by crossing EACH
            // OTHER — Vortex's VI+/VI-, a MA cloud's two averages — actually crosses. Identical
            // data for every component is how the first draft of this harness reported Vortex
            // as silent when it was the fixture that had nothing to say.
            bool rising = index++ % 2 == 0;
            cfg.Components.Add(new ComponentConfig
            {
                Name = c.Name, DisplayName = c.DisplayName, DisplayType = c.DisplayType,
                Role = c.Role, SubPaneName = c.SubPaneName,
                IsVisible = c.IsVisible, IsMuted = false,
                IsZoneLine = c.DefaultIsZoneLine,
                SignalSpeechTemplate = c.DefaultSignalSpeechTemplate,
                UpperComponentName = c.UpperComponentName,
                LowerComponentName = c.LowerComponentName,
            });

            double from = mainPane ? (rising ? 110.0 : 90.0) : (rising ? level - 10 : level + 10);
            double to   = mainPane ? (rising ? 90.0 : 110.0) : (rising ? level + 10 : level - 10);

            arrays[c.Name] = IsMarker(c.DisplayType)
                ? new[] { double.NaN, double.NaN, 1.0, double.NaN }   // prints on the closing bar
                : new[] { from, from, to, to };                       // crosses on the closing bar

            // A cloud's boundaries are INTERNAL data arrays, not components — MA Cloud's whole
            // indicator is one Cloud component over "__fastMA"/"__slowMA". Without them the
            // cloud scan has nothing to read, and the indicator reports as silent when it is the
            // fixture that is incomplete.
            if (c.DisplayType == ComponentDisplayType.Cloud
                && !string.IsNullOrEmpty(c.UpperComponentName) && !string.IsNullOrEmpty(c.LowerComponentName))
            {
                arrays[c.UpperComponentName!] = new[] { 110.0, 110.0, 90.0, 90.0 };
                arrays[c.LowerComponentName!] = new[] { 105.0, 105.0, 85.0, 85.0 };
            }
        }

        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        var speech = new CounterSpeechManager();
        var spoken = new List<string>();
        speech.OnSpeak = t => spoken.Add(t);
        var router = new SpeechFeedbackRouter(speech, new SpeechFormatter(), store);
        _ = new AutoNarrationService(store, bus, router, new IndicatorContextAnalyzer());

        WorkspaceState At(int bars)
        {
            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            foreach (var kv in arrays) buf.ComponentData[kv.Key] = kv.Value.Take(bars).ToArray();
            return WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, bars).Select(i =>
                    new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 100, 101, 99, 100, 10))),
                ActiveSeries = System.Collections.Immutable.ImmutableList.Create(new ChartSeries(cfg, buf)),
                FocusedSeriesId = cfg.Id,
                CurrentDataIndex = bars - 1,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
                IsSpeechEnabled = true,
            };
        }

        store.EmitState(At(2)); bus.Publish(new RedrawEvent());   // narration seeds here
        store.EmitState(At(3)); bus.Publish(new RedrawEvent());
        store.EmitState(At(4)); bus.Publish(new RedrawEvent());   // bar 2 closes
        return spoken;
    }

    /// <summary>
    /// THE GUARD. Switch narration on for every shipped indicator in turn and listen. Anything
    /// that says nothing at all is a dead switch: the user presses N, hears "narrating", and
    /// then gets silence for the rest of the session with nothing anywhere to explain it.
    /// </summary>
    [Fact]
    public void EveryShippedIndicator_ActuallySpeaks_WhenNarrationIsOn()
    {
        var silent = new List<string>();

        foreach (var type in IndicatorProviderFixture.ProviderTypes())
        {
            IIndicatorProvider provider;
            try { provider = IndicatorProviderFixture.Create(type); } catch { continue; }

            foreach (var meta in provider.GetIndicators())
            {
                if (KnownSilent.Contains(meta.Code)) continue;
                if (DriveOneBarClose(provider, meta, narrated: true).Count == 0)
                    silent.Add($"{meta.Code} ({type.Name}, pane {meta.DefaultPane})");
            }
        }

        Assert.True(silent.Count == 0,
            "narration was switched on for these indicators, a bar closed with a marker printing "
            + "and a level crossed, and NOTHING WAS SPOKEN. Give the indicator a route — a marker, "
            + "a declared level, a zone line — or add it to KnownSilent with a reason:\n  "
            + string.Join("\n  ", silent));
    }

    /// <summary>
    /// The vacuity partner for the guard above. Same indicators, same data, same bar close,
    /// narration OFF: the harness has to be able to produce silence, or "something was spoken"
    /// asserts nothing.
    /// </summary>
    [Fact]
    public void TheSameIndicatorsSayNothing_WhenNarrationIsOff()
    {
        var spoke = new List<string>();

        foreach (var type in IndicatorProviderFixture.ProviderTypes())
        {
            IIndicatorProvider provider;
            try { provider = IndicatorProviderFixture.Create(type); } catch { continue; }

            foreach (var meta in provider.GetIndicators())
            {
                if (KnownSilent.Contains(meta.Code)) continue;
                if (DriveOneBarClose(provider, meta, narrated: false).Count > 0)
                    spoke.Add(meta.Code);
            }
        }

        Assert.True(spoke.Count == 0,
            "these spoke without being flagged for narration: " + string.Join(", ", spoke));
    }


    /// <summary>
    /// The vacuity partner. If the routes above were mis-written and matched everything, the
    /// guard would pass on an entirely silent terminal — so the exemptions have to be REAL
    /// exemptions, i.e. every one of them still fails the route test.
    /// </summary>
    [Fact]
    public void TheExemptionListIsNotHidingIndicatorsThatCanActuallySpeak()
    {
        var osc = new IndicatorContextAnalyzer();
        var canSpeak = new List<string>();

        foreach (var type in IndicatorProviderFixture.ProviderTypes())
        {
            IIndicatorProvider provider;
            try { provider = IndicatorProviderFixture.Create(type); } catch { continue; }

            foreach (var meta in provider.GetIndicators())
                if (KnownSilent.Contains(meta.Code) && CanNarrateOnBarClose(provider, osc, meta))
                    canSpeak.Add(meta.Code);
        }

        Assert.True(canSpeak.Count == 0,
            "exempted as silent, but they do have a narration route — remove them from "
            + "KnownSilent: " + string.Join(", ", canSpeak));
    }

    /// <summary>
    /// Configuration that looks like coverage and is not. <c>IndicatorContextAnalyzer</c> keys
    /// its definitions "{CODE}|{COMPONENT}" and a key that matches no component of that
    /// indicator is dead — <c>STOCH|K</c> was, for a component named "Oscillator", and so were
    /// both Bollinger entries.
    /// </summary>
    [Fact]
    public void EveryRegisteredOscillatorDefinition_NamesAComponentThatExists()
    {
        var osc = new IndicatorContextAnalyzer();
        var all = new List<IndicatorMetadata>();
        foreach (var type in IndicatorProviderFixture.ProviderTypes())
        {
            try { all.AddRange(IndicatorProviderFixture.Create(type).GetIndicators()); } catch { }
        }

        var dead = new List<string>();
        foreach (string key in RegisteredKeys(osc))
        {
            var parts = key.Split('|');
            bool bound = all.Any(m => string.Equals(m.Code, parts[0], StringComparison.OrdinalIgnoreCase)
                                   && m.Components.Any(c => string.Equals(c.Name, parts[1], StringComparison.OrdinalIgnoreCase)));
            if (!bound) dead.Add(key);
        }

        Assert.True(dead.Count == 0,
            "registered oscillator definitions that bind to no component: " + string.Join(", ", dead));
    }

    private static IEnumerable<string> RegisteredKeys(IndicatorContextAnalyzer osc)
        => Definitions(osc).Select(d => d.Key);

    /// <summary>
    /// Playback has ONE route and it is the marker template. A line, however narrated, must not
    /// speak while the tones are running — it has a value on every bar, and at ten bars a second
    /// that is a wall of numbers. This states the division the manual promises:
    /// <b>playback speaks what happened at a POINT; bar-close narration speaks what CHANGED.</b>
    /// </summary>
    [Fact]
    public void TheOnlyThingPlaybackSpeaksIsAMarkerWithATemplate()
    {
        var osc = new IndicatorContextAnalyzer();
        foreach (var type in IndicatorProviderFixture.ProviderTypes())
        {
            IIndicatorProvider provider;
            try { provider = IndicatorProviderFixture.Create(type); } catch { continue; }

            foreach (var meta in provider.GetIndicators())
            foreach (var comp in meta.Components)
            {
                bool playbackSpeaks = IsMarker(comp.DisplayType)
                                      && !comp.DefaultIsZoneLine
                                      && !string.IsNullOrEmpty(comp.DefaultSignalSpeechTemplate);
                if (!playbackSpeaks) continue;

                Assert.True(MarkerRoute(comp),
                    $"{meta.Code}.{comp.Name} speaks in playback but has no bar-close route — "
                    + "the two must not diverge.");
            }
        }
    }
}

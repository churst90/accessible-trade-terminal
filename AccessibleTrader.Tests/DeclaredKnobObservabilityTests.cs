using System.Collections.Immutable;
using System.Reflection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A knob nobody observes is a lie the dialog tells.</b>
///
/// <para>
/// This file exists because ONE defect shape accounts for a startling share of everything found
/// in this repo over the last fortnight, and it is invisible to both of the things that normally
/// find bugs. Reading the code does not catch it — the declaration reads perfectly. A conventional
/// test does not catch it — the test asserts that the declaration EXISTS, which it does. The
/// property is declared, stored, round-tripped through the workspace and rendered by the
/// Properties dialog as a working control, and nothing at the far end ever reads it.
/// </para>
///
/// <para>The instances, all of them real and all of them shipped:</para>
/// <list type="bullet">
///   <item><c>UsePolarityColoring</c> had no renderer consumer at all. MFI declared teal primary,
///   red secondary, <c>ColorSource.Value</c> and <c>ColorBaseline = 50</c> — four fields that
///   together say "red below the midline, green above" — and drew solid teal, because
///   Line/Oscillator went through <c>RenderLine</c>, which painted everything with one colour.</item>
///   <item><c>DefaultReferenceLevel</c> sat behind a <c>??</c> chain whose third step returned a
///   non-null type default, making step four unreachable for every oscillator.</item>
///   <item><c>DefaultPane</c> was read as <c>meta.DefaultPane ?? GetPane()</c> when
///   <c>DefaultPane</c> is non-nullable, so the fallback was dead code.</item>
///   <item>Three indicator bool parameters parsed as the wrong type and reached no provider, so
///   Cipher SR's Adaptive Break, Cipher S's Adaptive Smoothing and Spider Lines' Fast Mode were
///   checkboxes that did nothing. That one has a test of its own —
///   <see cref="IndicatorBoolParameterReachabilityTests"/> — and this file is its generalisation.</item>
/// </list>
///
/// <para>
/// <b>The method, taken from that file and widened to the whole model.</b> Its docstring states
/// the rule: <i>"these tests assert on what the provider OBSERVED, by flipping the switch and
/// requiring the output to move. A test that only asked 'does the helper parse a double' would
/// have stayed green through the original bug — the helper it asked was the one that already
/// worked."</i> So: enumerate every knob on <see cref="ComponentConfig"/> BY REFLECTION, and for
/// each one flip it and require at least one of the four channels this application actually
/// delivers through to come out different.
/// </para>
///
/// <para>
/// <b>The four channels are the whole product surface.</b> Pixels (what a sighted reviewer sees),
/// the sonification point (what the chart sounds like), the navigation readout (what is said when
/// you arrow onto it) and the bar-close narration scan (what is said unprompted). A knob that
/// moves none of them is either dead or wired to a consumer this app does not have.
/// </para>
///
/// <para>
/// <b>Why reflection and not a list.</b> A hard-coded list of knobs is a second place every new
/// property must be added, which is the same failure this file is about — see the hand-written
/// clone in the restore path that dropped N and M on every restart. The knob list comes from the
/// type, so a property added to <see cref="ComponentConfig"/> tomorrow is swept tomorrow, and the
/// only way to opt one out is to name it in <see cref="Unobservable"/> with a reason.
/// </para>
///
/// <para>
/// <b>Why a matrix of shapes.</b> Most knobs are shape-specific: <c>MarkerAnchor</c> means nothing
/// to a line, <c>IsAreaFill</c> means nothing to an arrow. A single fixture would report a dozen
/// false positives. A knob counts as observed if it moves an output in AT LEAST ONE shape — which
/// is also exactly how the MFI defect presented, since the shape where the knob was unread
/// (Line/Oscillator) was not the shape where it worked (Histogram/ZeroArea).
/// </para>
/// </summary>
public sealed class DeclaredKnobObservabilityTests
{
    private const int W = 240, H = 180;

    /// <summary>
    /// The floor. A reflection sweep that stops finding properties passes vacuously, which is the
    /// pathology <c>MIN_PROVIDER_CLAIMS</c> was added to <c>check_doc_drift.py</c> for: a rephrase
    /// that hides a claim from the regex must fail loudly, not quietly reduce coverage.
    /// ComponentConfig carried 50 settable knobs when this was written.
    /// </summary>
    private const int MinKnobsSwept = 40;

    /// <summary>
    /// Knobs that genuinely cannot move any of the four channels, each with the reason it cannot.
    /// <b>Every entry here is a claim that should be re-read before it is trusted</b> — a pinned
    /// exemption with a recorded reason is worth exactly as much as the reason.
    /// </summary>
    private static readonly Dictionary<string, string> Unobservable = new()
    {
        ["IsUserStyled"] =
            "Not a knob: the flag that records THAT the user picked a colour by hand, so the theme "
            + "does not overwrite it on a theme switch. Its effect is on the next theme change, "
            + "not on any frame rendered from one config.",

        // The four below ARE read, by a consumer that is real and traced — just not by one of
        // these four channels. Each names its reader, so the claim can be checked rather than
        // trusted. If a reader here is ever deleted, this line becomes the lie instead.
        ["PlaybackLayer"] =
            "Read by AudioSequencer.LayerVolume (Background 60%, Midground 80%, Foreground 100%) "
            + "when a whole series is PLAYED. These channels observe one point at a time, which is "
            + "navigation, not playback — the mix is a property of the sequence.",

        ["DecayMs"] =
            "Read by AudioSequencer and NavigationSonifier to set a note's DURATION. AudioPoint "
            + "carries frequency, timbre and volume; how long the note lasts is decided by the "
            + "player, so it cannot appear in a single sonification point.",

        ["SubPaneHeightRatio"] =
            "Read by ChartRenderer when it allocates the sub-pane strips (clamped 0.05-0.40). "
            + "That is pane LAYOUT, which happens above DataLayer — this channel hands the layer a "
            + "rectangle that is already decided.",

        ["DataMapping"] =
            "Read by IndicatorStateMapper (which OHLCV field an overlay tracks), by "
            + "IndicatorOrchestrator (whether a series needs recomputation) and by "
            + "TactileCanvasCoordinator.MapOhlcvField (the Braille channel). The first two run "
            + "before these channels see anything, and the third is a FIFTH output this sweep does "
            + "not model. Worth revisiting if the tactile canvas ever becomes a channel here.",
    };

    // ── The four channels ────────────────────────────────────────────────────

    /// <summary>What the four observation channels produced for one configuration.</summary>
    private readonly record struct Observation(
        string Pixels, string Audio, string SpeechX, string SpeechY, string Narration);

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    /// <summary>
    /// The REAL styling service. A substitute would answer every colour question with a default
    /// and every colour knob would come back "unobserved" — the test would be measuring its own
    /// mock. Its three collaborators are not consulted by <c>GetPaint</c>, which is the only
    /// member <see cref="DataLayer"/> calls.
    /// </summary>
    private static IStylingService Styling() => new StylingService(
        Substitute.For<IComponentRoleMapper>(),
        Substitute.For<ISonificationProfileProvider>(),
        Substitute.For<IPaneAssignmentService>());

    private static List<Ohlcv> Bars()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(24);
        for (int i = 0; i < 24; i++)
        {
            // A shape that crosses the midline of a 0..100 pane in both directions, so knobs that
            // only bite on a crossing (ColorBaseline, UsePolarityColoring, the above/below
            // waveforms, the zero-area split) have a crossing to bite on.
            double o = 50 + 30 * Math.Sin(i / 2.0);
            double c = 50 + 30 * Math.Sin((i + 1) / 2.0);
            bars.Add(new Ohlcv(start.AddHours(i), o, Math.Max(o, c) + 4, Math.Min(o, c) - 4, c, 1000 + i * 7));
        }
        return bars;
    }

    private static readonly List<Ohlcv> TheBars = Bars();

    /// <summary>
    /// The component values: the same midline-crossing shape the bars have. The offset separates
    /// the band companions so an upper/lower pair is a real band with the reading inside it.
    /// </summary>
    private static double[] Values(double offset = 0)
        => TheBars.Select(b => b.Close + offset).ToArray();

    private static ChartSeries BuildSeries(ComponentDisplayType shape, Action<ComponentConfig> variant,
        Action<ComponentConfig> configure, out ComponentConfig comp)
    {
        bool isCandle = shape == ComponentDisplayType.Candle;
        string canonicalName = isCandle ? "body" : "value";
        var config = new SeriesConfig
        {
            Id = isCandle ? "candles" : "ind",
            Name = isCandle ? "candles" : "Indicator",
            IndicatorCode = isCandle ? "candles" : "TESTIND",
            Pane = isCandle ? "Main" : "Indicator",
            IsVisible = true,
            IsAutoNarrated = true,
        };

        comp = new ComponentConfig
        {
            Name = canonicalName,
            DisplayName = isCandle ? "body" : "Value",
            DisplayType = shape,
            DataMapping = "close",
            IsVisible = true,
            IsEnabled = true,
            IsMuted = false,
            Volume = 1f,
            ColorHex = "#00FF00",
            ColorHexSecondary = "#FF0000",
            ColorBaseline = 50,
            Thickness = 3f,
            BaseFrequency = 440,
            BullishFrequency = 660,
            BearishFrequency = 220,
            FreqMultiplier = 1.0,
            Waveform = "sine",
            AboveReferenceWaveform = "sine",
            BelowReferenceWaveform = "sine",
            // A real reference, so "above" and "below" are distinguishable states. Left null,
            // every reference-relative knob is dead by construction and reads as unread.
            ReferenceLevel = 50,
            // ON, because the level-subscription filter is consulted INSIDE the boundary-click
            // branch. With the click switched off that branch never runs, and
            // SubscribedLevelNames — the knob whose absence the 51st pass spent a session on —
            // reads as wired to nothing.
            TriggerBoundaryClick = true,
            EnvelopeType = "Sustain",
            SpeechTemplate = "{name}, {type}, {value}",
            // Live, so DeviationNorm has a mapping that reads it. Under AmplitudeMapping.None it
            // is dead by design, and "dead by design in this fixture" is not a finding.
            AmplitudeMapping = AmplitudeMapping.ReferenceDeviation,
            // Both halves of the pair, naming the two companion components below. The scanner's
            // band logic bails unless BOTH are set, so a fixture that sets neither would report
            // both names unobserved for a reason that is the fixture's fault.
            UpperComponentName = "upper",
            LowerComponentName = "lower",
        };
        variant(comp);     // the cell
        configure(comp);   // the knob under test — applied last, so a perturbation always wins
        config.Components.Add(comp);

        // TWO COMPANION COMPONENTS, and they are not decoration.
        //
        // With a single-component series, flipping IsAutoNarrated changes nothing and it is
        // RIGHT that it changes nothing: a component narrates when its series narrates and
        // either no component is singled out or this one is, so with one component the two
        // answers always agree. That is A2e's E04 exactly — "a fallback masks the rule it is a
        // fallback for", found because every existing test had one component. A selection rule
        // is only under test when at least two candidates compete.
        foreach (string companion in new[] { "upper", "lower" })
        {
            config.Components.Add(new ComponentConfig
            {
                Name = companion,
                DisplayName = companion,
                DisplayType = ComponentDisplayType.Line,
                DataMapping = "close",
                IsVisible = true,
                IsEnabled = true,
                ColorHex = companion == "upper" ? "#4444FF" : "#FF8800",
                Thickness = 1f,
            });
        }

        // A level, so level-adjacent knobs (SubscribedLevelNames) have something to subscribe to.
        config.Levels.Add(new LevelConfig
        {
            Name = "Midpoint", Value = 50, IsVisible = true, ColorHex = "#8888FF", Thickness = 1f,
            // PlayEarcon, because the boundary-click branch is gated on it — and that same
            // branch is the one that consults SubscribedLevelNames. Left at its default the
            // gate is shut, and BOTH knobs report as read by nothing.
            PlayEarcon = true,
        });

        // THE BUFFER IS KEYED BY THE CANONICAL NAMES, not by whatever the component is called
        // after perturbation — and that is deliberate. Name IS the data key: the provider fills
        // the buffer from its metadata and the config takes its name from the same metadata, so
        // in production they always agree. Keying the buffer off the live config would make them
        // agree here too, and "Name" would come back unobserved — when in fact renaming a
        // component is the single most consequential edit on this type. One of the two wick
        // defects was exactly this: "a renamed field left the sonifier's idea of a component's
        // identity pointing at nothing", and it survived every release until a user heard it.
        var buffer = new SeriesDataBuffer { SeriesId = config.Id };
        buffer.ComponentData[canonicalName] = Values();
        buffer.ComponentData["upper"] = Values(offset: 8);
        buffer.ComponentData["lower"] = Values(offset: -8);
        return new ChartSeries(config, buffer);
    }

    // ── Channel 1: pixels ────────────────────────────────────────────────────

    private static string RenderPixels(ChartSeries series)
    {
        using var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Black);
            var theme = Themes().Current;
            var rect = SKRect.Create(0, 0, W, H);
            var ctx = new RenderContext(
                canvas, rect, TheBars, 0, TheBars.Count, 0, 100, false,
                rect.Width / TheBars.Count, 1f,
                string.IsNullOrEmpty(series.Pane) ? "Main" : series.Pane, 0, theme);

            new DataLayer(Styling()).Render(ctx, new[] { series });

            // A sub-pane component is skipped by the main pass; render the sub-pane pass too, so
            // SubPaneName is observed as a MOVE rather than as a disappearance that also happens
            // to be what an invisible component looks like.
            foreach (var sub in series.Components
                         .Select(c => c.SubPaneName)
                         .Where(n => !string.IsNullOrEmpty(n)).Distinct())
            {
                new DataLayer(Styling()).Render(ctx with { SubPaneFilter = sub }, new[] { series });
            }
        }

        // The pixel bytes, hashed. Exact-colour assertions are brittle across Skia versions; a
        // hash of "did the frame change at all" is not — it only ever answers the question this
        // file asks.
        var bytes = bmp.GetPixelSpan().ToArray();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    // ── Channel 2: the sonification point ────────────────────────────────────

    private static string RenderAudio(ChartSeries series, ComponentConfig comp)
    {
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var parts = new List<string>();
        // Two bars, one above the baseline and one below it, because half the audio knobs
        // (BullishFrequency, BelowReferenceWaveform, the polarity split) are only distinguishable
        // when the value is on the side they describe. A selection rule is only under test when
        // at least two candidates compete.
        var vals = Values();
        // 6 is the crossing: the series passes down through the reference at 50 between bars
        // 5 and 6. Sampling only bars that sit on one side of it means no crossing-gated knob
        // can fire, however correctly it is wired.
        foreach (int i in new[] { 3, 6, 9 })
        {
            var bar = TheBars[i];
            // prevVal IS PASSED, and it has to be. TriggerBoundaryClick only fires when the
            // strategy can see the previous value to compare against — `if (comp.Trigger-
            // BoundaryClick && prevVal.HasValue)`. Omitting it (the parameter is optional)
            // made the whole boundary-click branch unreachable, and the knob reported as
            // read by nothing.
            parts.Add(strategy.CreateAudioPoint(series, comp, vals[i], bar,
                relativeIndex: i, viewportWidth: TheBars.Count,
                viewportRange: (0.0, 100.0), chartVolume: 1f,
                prevVal: vals[i - 1]).ToString());
        }
        return string.Join(" | ", parts);
    }

    // ── Channels 3 and 4: what is said ───────────────────────────────────────

    private static WorkspaceState State(ChartSeries series) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(TheBars),
        ActiveSeries = ImmutableList.Create(series),
        FocusedSeriesId = series.Id,
        FocusedComponentIndex = 0,
        CurrentDataIndex = 9,
        LastInteractionContext = InteractionContext.Component,
        SpeakTimestamps = false,
        TimestampReadLocation = "None",
        ReadColumnHeaders = true,
        // NOT "ValueOnly". That order suppresses the name and the speech template, so the
        // readout was the bare number "21.23" for every configuration and DisplayName and
        // SpeechTemplate both reported as unread — a property of the fixture, not of the code.
        SpeechOrder = "HeaderValue",
        IsSpeechEnabled = true,
        NarrateSignalsOnBarClose = true,
    };

    private static string RenderSpeech(ChartSeries series, bool isYMove)
    {
        var state = State(series);
        return new SpeechFormatter().FormatPointFeedback(
            state, isXMove: !isYMove, isYMove: isYMove, series, TheBars[9], "");
    }

    private static string RenderNarration(ChartSeries series)
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());

        // SEED ON A SHORTER HISTORY THAN THE ONE SCANNED, and this is the whole trick.
        //
        // Seed records `barCount - 1` as the exclusive lower bound of "already known". Seeding
        // and scanning the SAME state therefore gives scanFrom = 23 against closedBound = 22,
        // `scanFrom > closedBound`, and every series is skipped — correctly, because nothing has
        // happened since the seed. The first draft of this file did exactly that and the
        // narration channel returned "" for every configuration, which silently took four knobs
        // (SubscribedLevelNames, IsZoneLine, UsesGradientSpeech, the band component names) with
        // it and reported them as unread. A channel that is identical for every input is not
        // evidence of anything; see the vacuity floor on the knob count.
        var seedState = State(series) with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(TheBars.Take(SeedBars)),
            CurrentDataIndex = SeedBars - 1,
        };
        scanner.Seed(series, seedState);

        return scanner.ScanAll(new[] { series }, State(series),
            closedBound: TheBars.Count - 2, isBarClose: true) ?? "";
    }

    /// <summary>How much history the scanner is told it already knows. The rest is news.</summary>
    private const int SeedBars = 12;

    private static Observation Observe(ComponentDisplayType shape, Action<ComponentConfig> variant,
        Action<ComponentConfig> configure)
    {
        var series = BuildSeries(shape, variant, configure, out var comp);
        return new Observation(
            RenderPixels(series),
            RenderAudio(series, comp),
            RenderSpeech(series, isYMove: false),
            RenderSpeech(series, isYMove: true),
            RenderNarration(series));
    }

    // ── Perturbation ─────────────────────────────────────────────────────────

    /// <summary>
    /// A different, legal value for a knob of this type. The rule is "meaningfully different",
    /// not "any different": a 1% nudge to a frequency that the engine quantises would read as
    /// unobserved and blame the wrong party.
    /// </summary>
    private static object? Perturb(PropertyInfo prop, object? current)
    {
        Type t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

        if (t.IsEnum)
        {
            // Any member other than the current one. Deterministic (first differing member in
            // declaration order) so a failure reproduces.
            foreach (object? v in Enum.GetValues(t))
                if (v != null && (current == null || !v.Equals(current))) return v;
            return current;
        }

        if (t == typeof(bool)) return !(bool)(current ?? false);
        if (t == typeof(int)) return (int)(current ?? 0) + 500;

        if (t == typeof(float))
        {
            // Named, because several of these are ratios and a "just add 3" perturbation would
            // hand the layout a height ratio of 3.5 and the mixer a volume of 4 — illegal values
            // whose effect is a clamp, which is indistinguishable from not being read.
            float f = (float)(current ?? 0f);
            return prop.Name switch
            {
                "SubPaneHeightRatio" => f == 0.5f ? 0.25f : 0.5f,
                "Volume" => f == 0.25f ? 0.75f : 0.25f,
                "NoiseAmount" => f == 0.8f ? 0.4f : 0.8f,
                "Thickness" => f == 9f ? 5f : 9f,
                _ => f == 0.5f ? 0.25f : 0.5f,
            };
        }

        if (t == typeof(double)) return (double)(current ?? 0d) / 2.0 + 37.0;

        if (t == typeof(string))
        {
            string s = (string?)current ?? "";
            return prop.Name switch
            {
                "ColorHex" or "ColorHexSecondary" => s == "#FF00FF" ? "#00FFFF" : "#FF00FF",
                "Waveform" or "AboveReferenceWaveform" or "BelowReferenceWaveform"
                    => s == "square" ? "triangle" : "square",
                "EnvelopeType" => s == "Ping" ? "Sustain" : "Ping",
                "DataMapping" => s == "open" ? "high" : "open",
                "SpeechTemplate" => "{name} perturbed {value}",
                "SignalSpeechTemplate" => "{name} fired at {value}",
                "SubPaneName" => "strip",
                // A REAL registered patch id. An invented one would be looked up, missed, and
                // ignored — and "the registry had nothing for it" is indistinguishable from
                // "nothing reads this field", which is the finding this file is trying to make.
                "SoundPatchId" or "BullishSoundPatchId" or "BearishSoundPatchId"
                    => s == "crystal_bell" ? "detuned_pair_bell" : "crystal_bell",
                // Swap the two halves of the band rather than inventing a name: a name with no
                // component behind it is looked up, missed and skipped, which again reads as
                // "nothing observed it".
                "UpperComponentName" or "LowerComponentName" => s == "upper" ? "lower" : "upper",
                _ => s + "-perturbed",
            };
        }

        if (t == typeof(List<ColorRule>))
            return new List<ColorRule>
            {
                new() { Condition = ColorCondition.AboveLevel, ColorHex = "#FF00FF", Level = 50 },
            };

        if (typeof(IReadOnlyList<string>).IsAssignableFrom(t))
            // The subscription must EXCLUDE the fixture's level, not include it. Null means "no
            // declaration, so every level", and ["Midpoint"] also includes Midpoint — the two
            // agree, the perturbation is a no-op, and the knob reports as read by nothing. A
            // list naming a level this series does not have is the state that actually differs.
            return current is IReadOnlyList<string> { Count: > 0 }
                ? (IReadOnlyList<string>)new[] { "Midpoint" }
                : new[] { "SomeOtherLevel" };

        throw new InvalidOperationException(
            $"No perturbation defined for {prop.Name} ({prop.PropertyType}). Add one — a knob with "
            + "no perturbation is a knob this sweep silently stopped covering.");
    }

    // ── The sweep ────────────────────────────────────────────────────────────

    /// <summary>
    /// The shapes a component can take. Chosen to reach every arm of <see cref="DataLayer"/>'s
    /// dispatch that a knob could plausibly be read in, plus the candle body, which is the one
    /// component whose colours come from the theme rather than from itself.
    /// </summary>
    private static readonly ComponentDisplayType[] ShapeAxis =
    {
        ComponentDisplayType.Candle,
        ComponentDisplayType.Line,
        ComponentDisplayType.Oscillator,
        ComponentDisplayType.Histogram,
        ComponentDisplayType.ZeroArea,
        ComponentDisplayType.Dot,
        ComponentDisplayType.StepLine,
        ComponentDisplayType.Arrow,
        // Cloud, because UpperComponentName/LowerComponentName are read for this shape and no
        // other: the scanner's band logic opens with `if (comp.DisplayType != Cloud) continue`.
        ComponentDisplayType.Cloud,
    };

    /// <summary>
    /// THE SECOND AXIS, and it is not padding. Half the audio knobs are mutually exclusive by
    /// construction, so no single configuration can observe them all:
    ///
    /// <list type="bullet">
    ///   <item><c>Waveform</c> is used only when <c>ReferenceLevel</c> is NULL — with a reference
    ///   set, the above/below pair supersedes it. Adding a reference to the fixture made the
    ///   above/below waveforms observable and turned <c>Waveform</c> dead in the same move.</item>
    ///   <item><c>BaseFrequency</c> survives only under <c>PitchMapping.None</c>; Value, Price,
    ///   Direction and PriceDirection all overwrite it.</item>
    ///   <item><c>BullishFrequency</c>/<c>BearishFrequency</c> exist only under Direction and
    ///   PriceDirection.</item>
    /// </list>
    ///
    /// A knob is observed if it moves an output in at least one cell of shape x audio.
    /// </summary>
    private static readonly (string Name, Action<ComponentConfig> Tweak)[] AudioAxis =
    {
        ("value-pitch, reference at 50", _ => { }),   // the fixture's own defaults
        ("no reference, pitch None", c => { c.ReferenceLevel = null; c.PitchMapping = PitchMapping.None; }),
        ("direction pitch", c => c.PitchMapping = PitchMapping.Direction),
    };

    private static IEnumerable<(ComponentDisplayType Shape, string Audio, Action<ComponentConfig> Tweak)> Cells()
        => from s in ShapeAxis from a in AudioAxis select (s, a.Name, a.Tweak);

    [Fact]
    public void EveryDeclaredComponentKnob_MovesSomethingTheUserCanSeeHearOrBeTold()
    {
        var knobs = typeof(ComponentConfig)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name)
            .ToList();

        Assert.True(knobs.Count >= MinKnobsSwept,
            $"The reflection sweep found only {knobs.Count} settable knobs on ComponentConfig, "
            + $"below the floor of {MinKnobsSwept}. Either the model shrank a great deal or the "
            + "sweep has stopped seeing it — and a sweep that sees nothing passes vacuously.");

        // The unperturbed observation is the same for every knob, so take it once per cell.
        var cells = Cells().ToList();
        var baseline = cells.Select(c => Observe(c.Shape, c.Tweak, _ => { })).ToList();

        var unobserved = new List<string>();

        foreach (var knob in knobs)
        {
            if (Unobservable.ContainsKey(knob.Name)) continue;

            bool moved = false;
            for (int ci = 0; ci < cells.Count && !moved; ci++)
            {
                var (shape, _, tweak) = cells[ci];
                Observation after;
                try
                {
                    // Perturb away from the value the FIXTURE holds, not from the type's default.
                    // The fixture deliberately sets colours, thickness, frequencies and waveforms
                    // to non-default values so the channels have something to show; perturbing
                    // from the default could land on the value the fixture already has, and a
                    // no-op perturbation reads as "nothing observed it" — a false finding that
                    // blames production for a defect in this file.
                    BuildSeries(shape, tweak, _ => { }, out var fixtureComp);
                    object? current = knob.GetValue(fixtureComp);
                    object? perturbed = Perturb(knob, current);

                    Assert.False(Equals(current, perturbed),
                        $"Perturbation of {knob.Name} produced the value the fixture already holds "
                        + $"({current ?? "null"}). That is a no-op, and a no-op reads as 'nothing "
                        + "observed it'. Fix Perturb, not production code.");

                    after = Observe(shape, tweak, c => knob.SetValue(c, perturbed));
                }
                catch (InvalidOperationException)
                {
                    throw;   // a missing perturbation is a defect in this file, not a finding
                }
                catch (Xunit.Sdk.XunitException)
                {
                    throw;   // so is a no-op perturbation
                }
                catch
                {
                    // A knob whose new value throws somewhere downstream has unmistakably been
                    // read by something. That is an observation, and a loud one.
                    moved = true;
                    break;
                }

                if (baseline[ci] != after) moved = true;
            }

            if (!moved) unobserved.Add(knob.Name);
        }

        Assert.True(unobserved.Count == 0,
            "These declared knobs changed NOTHING a user can see, hear or be told, in any of the "
            + $"{cells.Count} shape x audio cells swept:\n  "
            + string.Join("\n  ", unobserved)
            + "\n\nEach one is rendered by the Properties dialog as a working control and stored in "
            + "the workspace. Either wire it to a consumer, or name it in Unobservable with the "
            + "reason it cannot move any channel. Do not delete this assertion.");
    }

    /// <summary>
    /// The exemption list is not a place to put a knob you did not want to fix. Every name in it
    /// must still BE a knob — an exemption naming a property that no longer exists is a line
    /// nobody has read since the property was deleted, and it makes the list look shorter than
    /// the real exposure.
    /// </summary>
    [Fact]
    public void EveryExemptedKnob_StillExists()
    {
        var names = typeof(ComponentConfig)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name).ToHashSet();

        var stale = Unobservable.Keys.Where(k => !names.Contains(k)).ToList();
        Assert.True(stale.Count == 0,
            "Unobservable names knobs that ComponentConfig no longer has: " + string.Join(", ", stale));
    }
}


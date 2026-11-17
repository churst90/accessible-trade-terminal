using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B's Wave Gap and Money Flow are waves by default, histograms by choice.</b>
///
/// <para>Cody, 2026-10-30: "The wt histogram jumps to os/ob levels even though it doesn't have
/// them … histograms shouldn't have over bought/over sold levels, so we need to pick one display
/// type"; then "the money flow I think does show the waves plus the red/green bar, so perhaps a
/// toggle here would be best, with the waves being the default view … it's a yellow filled area so
/// this should read correctly as well. whatever scale thee two are set to should be what is read
/// out". Both were Histograms pitched by Direction: two fixed notes (360/140 Hz and 300/100 Hz)
/// that leap more than an octave at every zero crossing, the low ones under the blue waves'
/// whole scale — the "jump" Cody heard.</para>
///
/// <para>Now each has a "Read as" setting. The first form, a wave, is sounded on the pane's own
/// ±100 scale (Value pitch, no levels); the second, a histogram, is read, navigated and sounded as
/// one (pitched by side, bar grit). The PICTURE is the same in both — Cody, 2026-10-30: "the
/// toggle … shouldn't change what is visually shown, only what component type the screen reader
/// sees and navigates on … hiding the money flow should hide both" — the filled area, drawn
/// exactly as the histogram area was, and for Money Flow the red/green Money Flow Bar along the
/// bottom, which is hidden with it. The WT Histogram is named "Wave Gap" (it is the fast wave
/// less the slow one; Market Cipher B calls it "VWAP", which it is not); its component name stays
/// "WT Histogram", so saved charts, alerts and strategies that name it still find it.</para>
/// </summary>
public sealed class CipherBWaveFormTests
{
    private static readonly string[] Parts = { CipherBProvider.CompWtHistogram, CipherBProvider.CompMoneyFlowWave };

    private static StylingService Styling() =>
        new(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());

    private static IndicatorModelFactory Factory(IIndicatorPreferencesService? prefs = null) =>
        new(Styling(), prefs ?? new MockIndicatorPreferencesService());

    private static IndicatorMetadata Meta() => new CipherBProvider().GetIndicators()[0];

    private static ChartSeries Default(IndicatorModelFactory? f = null) =>
        (f ?? Factory()).CreateSeriesFromMetadata(Meta(), "Cipher B", Meta().DefaultPane, new List<(string, string)>(), null);

    private static ComponentConfig Part(ChartSeries s, string name) => s.Components.Single(c => c.Name == name);

    private static double Pitch(ChartSeries s, ComponentConfig c, double v) =>
        new DefaultSonificationStrategy(new SoundPatchRegistry())
            .CreateAudioPoint(s, c, v, new Ohlcv(DateTime.UtcNow, 10, 11, 9, 10, 1), 0, 10, (-100, 100), 1f).Frequency;

    private static void IsWave(ComponentConfig c)
    {
        Assert.Equal(ComponentDisplayType.Oscillator, c.DisplayType);
        Assert.True(c.IsAreaFill && c.AreaBySign, $"{c.Name}: not a filled area by sign");
        Assert.Equal(PitchMapping.Value, c.PitchMapping);
        Assert.Equal(new[] { "Zero" }, c.SubscribedLevelNames);   // zero only: no ±53/±60 (the waves')
    }

    private static void IsHistogram(ComponentConfig c)
    {
        Assert.Equal(ComponentDisplayType.Histogram, c.DisplayType);
        Assert.True(c.IsAreaFill && c.AreaBySign, $"{c.Name}: the histogram form is not drawn as the wave");
        Assert.Equal(PitchMapping.Direction, c.PitchMapping);
        Assert.Equal(new[] { "Zero" }, c.SubscribedLevelNames);   // zero only: no ±53/±60 (the waves')
    }

    [Fact]
    public void Both_are_waves_by_default_and_the_histogram_is_named_the_wave_gap()
    {
        var s = Default();
        foreach (var p in Parts) IsWave(Part(s, p));
        Assert.Equal("Wave Gap", Part(s, CipherBProvider.CompWtHistogram).DisplayName);
        Assert.Equal("WT Histogram", Part(s, CipherBProvider.CompWtHistogram).Name);
        Assert.All(Meta().Components.Where(m => Parts.Contains(m.Name)),
            m => Assert.Equal(ComponentDisplayType.Histogram, m.OtherForm?.DisplayType));
    }

    /// <summary>
    /// The wave's pitch is its value on the pane's scale: it rises with the value and does not
    /// leap at zero. The histogram form still has two notes, one per side, as a histogram should.
    /// </summary>
    [Fact]
    public void The_wave_is_pitched_by_its_value_on_the_pane_scale_without_a_leap_at_zero()
    {
        var s = Default();
        foreach (var p in Parts)
        {
            var c = Part(s, p);
            var f = new[] { -80.0, -20, -1, 1, 20, 80 }.Select(v => Pitch(s, c, v)).ToArray();
            for (int i = 1; i < f.Length; i++)
                Assert.True(f[i] > f[i - 1], $"{p}: pitch does not rise with the value ({string.Join(", ", f.Select(x => x.ToString("F0")))})");
            Assert.True(f[3] / f[2] < 1.05, $"{p}: {f[2]:F0} Hz at -1, {f[3]:F0} Hz at +1: a leap at zero");
        }
        var h = ComponentForms.InOtherForm(Factory(), Meta());
        foreach (var p in Parts)
        {
            var c = Part(h, p);
            Assert.Equal(c.BullishFrequency * c.FreqMultiplier, Pitch(h, c, 20));
            Assert.Equal(c.BearishFrequency * c.FreqMultiplier, Pitch(h, c, -20));
        }
    }

    [Fact]
    public void A_chart_saved_as_histograms_is_built_as_histograms()
    {
        var s = ComponentForms.InOtherForm(Factory(), Meta());
        foreach (var p in Parts)
        {
            IsHistogram(Part(s, p));
            Assert.Equal(ComponentDisplayType.Histogram, Part(s, p).UserDisplayType);
        }
    }

    [Fact]
    public void Histograms_saved_as_the_indicators_default_reach_a_new_chart()
    {
        var prefs = Substitute.For<IIndicatorPreferencesService>();
        prefs.GetPreferences("CIPHER_B").Returns(Parts.Select(p => new ComponentPreference
            { Name = p, UserDisplayType = ComponentDisplayType.Histogram }).ToList());
        var s = Default(Factory(prefs));
        foreach (var p in Parts)
        {
            IsHistogram(Part(s, p));
            Assert.Equal(ComponentDisplayType.Histogram, Part(s, p).UserDisplayType);
        }
    }

    // ── Each form keeps its own sound (Cody, 2026-11-17) ────────────────────────────────────
    // Sound defaults saved in one form used to reach the other: one set of fields, applied to
    // whichever form the chart was read as.

    private static IIndicatorPreferencesService Saved(params ComponentPreference[] p)
    {
        var prefs = Substitute.For<IIndicatorPreferencesService>();
        prefs.GetPreferences("CIPHER_B").Returns(p.ToList());
        return prefs;
    }

    private static ComponentPreference WaveSound(string part) =>
        new() { Name = part, Waveform = "square", BaseFrequency = 777, NoiseAmount = 0.31f };

    private static IndicatorComponentMetadata MetaOf(string part) => Meta().Components.Single(m => m.Name == part);

    [Fact]
    public void A_sound_saved_as_the_wave_does_not_reach_a_chart_reading_the_histogram()
    {
        var factory = Factory(Saved(Parts.Select(WaveSound).ToArray()));
        var wave = Default(factory);
        var hist = ComponentForms.InOtherForm(factory, Meta());
        var plainHist = ComponentForms.InOtherForm(Factory(), Meta());
        foreach (var p in Parts)
        {
            Assert.Equal(("square", 777.0, 0.31f), (Part(wave, p).Waveform, Part(wave, p).BaseFrequency, Part(wave, p).NoiseAmount));
            var h = Part(hist, p); var d = Part(plainHist, p);
            Assert.Equal((d.Waveform, d.BaseFrequency, d.NoiseAmount), (h.Waveform, h.BaseFrequency, h.NoiseAmount));
        }
    }

    [Fact]
    public void Each_form_gets_the_sound_saved_for_it()
    {
        var factory = Factory(Saved(Parts.Select(p => new ComponentPreference
        {
            Name = p, UserDisplayType = ComponentDisplayType.Histogram,
            Waveform = "sawtooth", BaseFrequency = 333, NoiseAmount = 0.2f,
            OtherFormSound = new() { DisplayType = ComponentDisplayType.Oscillator, Waveform = "square", BaseFrequency = 777 },
        }).ToArray()));
        var hist = Default(factory);   // the saved default form is the histogram
        foreach (var p in Parts)
        {
            Assert.Equal(("sawtooth", 333.0, 0.2f), (Part(hist, p).Waveform, Part(hist, p).BaseFrequency, Part(hist, p).NoiseAmount));
            var w = Part(hist, p).Clone();
            w.UserDisplayType = ComponentDisplayType.Oscillator;
            factory.ApplyForm(Meta(), w);   // the Properties switch applies the wave's saved sound
            Assert.Equal(("square", 777.0), (w.Waveform, w.BaseFrequency));
            Assert.Equal(Part(Default(), p).NoiseAmount, w.NoiseAmount);   // not saved for the wave: its default
        }
    }

    [Fact]
    public void Saving_in_one_form_keeps_the_other_forms_sound()
    {
        foreach (var p in Parts)
        {
            var mc = MetaOf(p);
            var wave = WaveSound(p);
            var hist = new ComponentPreference { Name = p, UserDisplayType = ComponentDisplayType.Histogram, Waveform = "sawtooth" };
            FormSounds.KeepOtherForm(hist, wave, mc);
            Assert.Equal((ComponentDisplayType.Oscillator, "square", 777.0, 0.31f),
                (hist.OtherFormSound!.DisplayType, hist.OtherFormSound.Waveform, hist.OtherFormSound.BaseFrequency, hist.OtherFormSound.NoiseAmount));

            // Saved again as the histogram: the wave's sound is still kept.
            var hist2 = new ComponentPreference { Name = p, UserDisplayType = ComponentDisplayType.Histogram, Waveform = "triangle" };
            FormSounds.KeepOtherForm(hist2, hist, mc);
            Assert.Equal("square", hist2.OtherFormSound!.Waveform);

            // Back to the wave: the histogram's sound is kept, and the wave's is the new one.
            var wave2 = new ComponentPreference { Name = p, UserDisplayType = ComponentDisplayType.Oscillator, Waveform = "sine" };
            FormSounds.KeepOtherForm(wave2, hist2, mc);
            Assert.Equal((ComponentDisplayType.Histogram, "triangle"), (wave2.OtherFormSound!.DisplayType, wave2.OtherFormSound.Waveform));
            Assert.Equal("sine", FormSounds.For(wave2, mc, ComponentDisplayType.Oscillator)!.Waveform);
            Assert.Equal("triangle", FormSounds.For(wave2, mc, ComponentDisplayType.Histogram)!.Waveform);
        }
    }

    /// <summary>The Properties dialog's switch, both ways: the picture the user set is kept, and
    /// back to the wave is the wave as built.</summary>
    [Fact]
    public void Switching_the_form_rebuilds_its_sound_both_ways_and_keeps_the_picture()
    {
        var factory = Factory();
        var built = Default(factory);
        foreach (var p in Parts)
        {
            var c = Part(built, p).Clone();
            c.UserDisplayType = ComponentDisplayType.Histogram;
            c.ColorHex = "#FF123456"; c.Thickness = 5f; c.UserIsAreaFill = false;   // the user's picture
            c.FreqMultiplier = 1.25;                                                // and pitch
            factory.ApplyForm(Meta(), c);
            IsHistogram(c);
            Assert.Equal(("#FF123456", 5f, (bool?)false, 1.25), (c.ColorHex, c.Thickness, c.UserIsAreaFill, c.FreqMultiplier));
            var asHist = Part(ComponentForms.InOtherForm(factory, Meta()), p);
            Assert.Equal((asHist.BullishFrequency, asHist.BearishFrequency, asHist.SpeechTemplate, asHist.Waveform),
                         (c.BullishFrequency, c.BearishFrequency, c.SpeechTemplate, c.Waveform));
            c.ColorHex = Part(built, p).ColorHex; c.Thickness = Part(built, p).Thickness; c.UserIsAreaFill = null;
            c.FreqMultiplier = Part(built, p).FreqMultiplier;

            c.UserDisplayType = ComponentDisplayType.Oscillator;
            factory.ApplyForm(Meta(), c);
            IsWave(c);
            var w = Part(built, p);
            Assert.Equal((w.ColorHex, w.ColorHexSecondary, w.FillColorHex, w.Waveform, w.BaseFrequency, w.SpeechTemplate, w.PlaybackLayer),
                         (c.ColorHex, c.ColorHexSecondary, c.FillColorHex, c.Waveform, c.BaseFrequency, c.SpeechTemplate, c.PlaybackLayer));
        }
    }

    /// <summary>What may differ between the forms: the type (its word, its navigation), the
    /// sound, the speech and the choice itself. Every other field — all of the picture — is the
    /// first form's.</summary>
    private static readonly HashSet<string> FormFields = new()
    {
        nameof(ComponentConfig.DisplayType), nameof(ComponentConfig.UserDisplayType),
        nameof(ComponentConfig.Waveform), nameof(ComponentConfig.AboveReferenceWaveform),
        nameof(ComponentConfig.BelowReferenceWaveform), nameof(ComponentConfig.EnvelopeType),
        nameof(ComponentConfig.AmplitudeMapping), nameof(ComponentConfig.PitchMapping),
        nameof(ComponentConfig.BaseFrequency), nameof(ComponentConfig.BullishFrequency),
        nameof(ComponentConfig.BearishFrequency),
        nameof(ComponentConfig.TriggerBoundaryClick), nameof(ComponentConfig.NoiseAmount),
        nameof(ComponentConfig.PlaybackLayer), nameof(ComponentConfig.DeviationNorm),
        nameof(ComponentConfig.SubscribedLevelNames), nameof(ComponentConfig.SpeechTemplate),
        nameof(ComponentConfig.SignalSpeechTemplate), nameof(ComponentConfig.DecayMs),
        nameof(ComponentConfig.SoundPatchId),
    };

    [Fact]
    public void Only_the_type_sound_and_speech_differ_between_the_forms()
    {
        var wave = Default();
        var hist = ComponentForms.InOtherForm(Factory(), Meta());
        foreach (var p in Parts)
        {
            var a = JObject.FromObject(Part(wave, p));
            var b = JObject.FromObject(Part(hist, p));
            var differ = a.Properties().Select(x => x.Name)
                .Where(n => !JToken.DeepEquals(a[n], b[n])).ToList();
            Assert.Contains(nameof(ComponentConfig.DisplayType), differ);
            Assert.DoesNotContain(differ, n => !FormFields.Contains(n));
        }
    }

    // ── The picture ────────────────────────────────────────────────────────────────

    private const int W = 300, H = 400, N = 40;

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    /// <summary>The whole ±100 pane, Money Flow Bar (−92 to −100) included; the other tests'
    /// frames are the same on it as on the data's own range.</summary>
    private static Dictionary<string, (double, double)> Ranges(ChartSeries s) =>
        new() { [string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane] = (-100, 100) };

    private static byte[] Render(ChartSeries s)
    {
        var renderer = new ChartRenderer(Themes(), Styling(), new PaneLayoutService(),
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, N).Select(i => new Ohlcv(start.AddDays(i), 10, 11, 9, 10, 1)).ToList();
        using var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, bars, new[] { s }, cursorIndex: -1, viewportStart: 0, viewportLength: N,
                viewportRange: (-100, 100), paneRanges: Ranges(s), rightMarginBars: 0);
        return bmp.Bytes;
    }

    /// <summary>Cipher B with only <paramref name="p"/> showing, on values that cross zero both
    /// ways; Money Flow keeps its Money Flow Bar.</summary>
    private static ChartSeries Only(ChartSeries s, string p, Action<ComponentConfig>? change = null)
    {
        foreach (var c in s.Components) c.IsVisible = c.Name == p;
        s.CloudFills.Clear();
        change?.Invoke(Part(s, p));
        s.Data.ComponentData[p] = Enumerable.Range(0, N).Select(i => 45 * Math.Sin(i / 3.0)).ToArray();
        return s;
    }

    /// <summary>
    /// The wave is drawn pixel for pixel as the histogram area was before 2026-10-30, on values that
    /// cross zero both ways; only its sound and its name changed.
    /// </summary>
    [Fact]
    public void The_wave_is_drawn_exactly_as_the_old_histogram_area()
    {
        foreach (var p in Parts)
        {
            var wave = Render(Only(Default(), p));
            var old = Render(Only(Default(), p, c => { c.DisplayType = ComponentDisplayType.Histogram; c.AreaBySign = false; }));
            var blank = Render(Only(Default(), p, c => c.IsVisible = false));
            Assert.False(wave.SequenceEqual(blank), $"{p}: nothing drawn");
            Assert.True(wave.SequenceEqual(old), $"{p}: the wave is not drawn as the histogram area was");
        }
    }

    /// <summary>
    /// "Read as" changes nothing on screen: the same frame in both forms, filled or not (unfilled,
    /// both are the wave's line), loaded either way or switched in Properties.
    /// </summary>
    [Fact]
    public void Both_forms_draw_the_same_picture_filled_or_not()
    {
        var factory = Factory();
        foreach (var p in Parts)
        foreach (bool? fill in new bool?[] { null, false })
        {
            var wave = Render(Only(Default(factory), p, c => c.UserIsAreaFill = fill));
            var hist = Render(Only(ComponentForms.InOtherForm(factory, Meta()), p, c => c.UserIsAreaFill = fill));
            var switched = Render(Only(Default(factory), p, c =>
            {
                c.UserIsAreaFill = fill;
                c.UserDisplayType = ComponentDisplayType.Histogram;
                factory.ApplyForm(Meta(), c);
                Assert.Equal(ComponentDisplayType.Histogram, c.DisplayType);
            }));
            var blank = Render(Only(Default(factory), p, c => c.IsVisible = false));
            Assert.False(wave.SequenceEqual(blank), $"{p}, fill {fill}: nothing drawn");
            Assert.True(hist.SequenceEqual(wave), $"{p}, fill {fill}: the histogram form is drawn differently");
            Assert.True(switched.SequenceEqual(wave), $"{p}, fill {fill}: switching the form changed the picture");
        }
    }

    /// <summary>
    /// The Money Flow Bar is part of Money Flow's picture: shown with it, and gone when Money Flow
    /// is hidden, switched off, undrawn or left out — in either form.
    /// </summary>
    [Fact]
    public void Hiding_money_flow_hides_its_bar_too()
    {
        var mf = CipherBProvider.CompMoneyFlowWave;
        ChartSeries NoBar(ChartSeries s) { s.Config.ZoneBands.Clear(); return s; }
        var factory = Factory();
        foreach (var form in new Func<ChartSeries>[] { () => Default(factory), () => ComponentForms.InOtherForm(factory, Meta()) })
        {
            Assert.Contains(form().Config.ZoneBands, b => b.SignComponentName == mf && b.IsVisible);
            Assert.False(Render(Only(form(), mf)).SequenceEqual(Render(NoBar(Only(form(), mf)))), "the bar is not drawn");

            // Each way of hiding it, against the same frame with no bar at all.
            foreach (var (how, hide) in new (string, Action<ChartSeries>)[]
            {
                ("hidden", s => Part(s, mf).IsVisible = false),
                ("switched off", s => Part(s, mf).IsEnabled = false),
                ("undrawn", s => Part(s, mf).IsDrawn = false),
                ("left out", s => { var c = Part(s, mf); s.Config.Components.Remove(c); s.Config.ExcludedComponents.Add(c); }),
            })
            {
                var with = Only(form(), mf); hide(with);
                var without = NoBar(Only(form(), mf)); hide(without);
                Assert.True(Render(with).SequenceEqual(Render(without)), $"{how}: the bar is still drawn");
            }
        }
    }
}

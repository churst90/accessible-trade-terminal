using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B's Wave Gap and Money Flow sound like RSI and answer only to zero.</b>
///
/// <para>Cody, 2026-10-31: "the wave gap and the money flow have reversed sounds and the sounds
/// aren't the oscillator sound triangle above and sine below like the rsi. and why do I hear over
/// sold and over bought when I do control left/right on the wave gap?" Both wave forms declared
/// their own waveforms — the Wave Gap square above zero and sawtooth below, Money Flow the
/// reverse — where every other oscillator takes the profile's voice: a sine with a square tinge
/// above its midline and triangle warmth below. And both subscribed to no level, which means
/// "every line in the pane", so Ctrl+Left/Right on either jumped to the waves' ±53 and ±60.</para>
///
/// <para>The ±53/±60 lines are right on the two waves: they are Market Cipher B's overbought and
/// oversold lines for the wave trend (its "Over Bought/Over Sold Level" inputs). The Wave Gap
/// and Money Flow have none; their one line is zero.</para>
/// </summary>
public sealed class CipherBWaveGapVoiceTests
{
    private static readonly string[] Parts = { CipherBProvider.CompWtHistogram, CipherBProvider.CompMoneyFlowWave };

    private static IndicatorModelFactory Factory() =>
        new(new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());

    private static IndicatorMetadata Meta() => new CipherBProvider().GetIndicators()[0];

    private static ChartSeries Waves() =>
        Factory().CreateSeriesFromMetadata(Meta(), "Cipher B", Meta().DefaultPane, new List<(string, string)>(), null);

    // With the pane's lines, as a chart has them (the factory leaves levels to the series service).
    private static ChartSeries Levelled(ChartSeries s)
    {
        foreach (var d in new CipherBProvider().GetDefaultLevels("CIPHER_B"))
            s.Levels.Add(SeriesManagementService.FromDescriptor(d, d.Value));
        return s;
    }

    private static AudioPoint Sound(ChartSeries s, ComponentConfig c, double v, double? prev = null) =>
        new DefaultSonificationStrategy(new SoundPatchRegistry())
            .CreateAudioPoint(s, c, v, new Ohlcv(DateTime.UtcNow, 10, 11, 9, 10, 1), 0, 10, (-100, 100), 1f, prev);

    private static (string, float, float, float, float, float) Timbre(AudioPoint a) =>
        (a.Waveform, a.SquareMix, a.TriangleMix, a.SawMix, a.SubSawMix, a.NoiseAmount);

    [Fact]
    public void Both_waves_have_RSIs_voice_on_each_side_of_their_midline()
    {
        var s = Levelled(Waves());
        var rsi = s.Components.Single(c => c.Name == CipherBProvider.CompMcbRsi);
        foreach (var p in Parts)
        {
            var c = s.Components.Single(x => x.Name == p);
            Assert.Equal((rsi.Waveform, rsi.AboveReferenceWaveform, rsi.BelowReferenceWaveform),
                         (c.Waveform, c.AboveReferenceWaveform, c.BelowReferenceWaveform));
            // 20 either side of the midline (RSI's 50, their zero): inside every line of both.
            Assert.Equal(Timbre(Sound(s, rsi, 70)), Timbre(Sound(s, c, 20)));
            Assert.Equal(Timbre(Sound(s, rsi, 30)), Timbre(Sound(s, c, -20)));
            Assert.NotEqual(Timbre(Sound(s, c, 20)), Timbre(Sound(s, c, -20)));   // the two sides differ
        }
    }

    [Fact]
    public void They_click_at_zero_only_and_stay_smooth_past_the_waves_lines()
    {
        var s = Levelled(Waves());
        foreach (var p in Parts)
        {
            var c = s.Components.Single(x => x.Name == p);
            Assert.True(Sound(s, c, 5, prev: -5).TriggerClick, $"{p}: no click crossing zero");
            Assert.False(Sound(s, c, 56, prev: 50).TriggerClick, $"{p}: clicked crossing the waves' 53");
            Assert.False(Sound(s, c, -63, prev: -57).TriggerClick, $"{p}: clicked crossing the waves' -60");
            Assert.Equal(Sound(s, c, 20).NoiseAmount, Sound(s, c, 70).NoiseAmount);   // no zone roughness
            Assert.Equal(Sound(s, c, -20).NoiseAmount, Sound(s, c, -70).NoiseAmount);
        }
    }

    // Rises into the waves' +53 at 2 and +60 at 3, then crosses zero at 5. Read against the
    // waves' lines, Ctrl+Right from 0 stops at 2, "overbought"; against zero, at 5.
    private static readonly double[] Path = { 10, 40, 55, 65, 30, -10, -20, -30 };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ctrl_right_on_them_jumps_to_the_zero_cross_not_the_waves_lines(bool histograms)
    {
        foreach (var p in Parts)
        {
            var built = Levelled(histograms ? ComponentForms.InOtherForm(Factory(), Meta()) : Waves());
            var buffer = new SeriesDataBuffer { SeriesId = built.Id };
            buffer.ComponentData[p] = Path;
            var s = new ChartSeries(built.Config, buffer);

            var bus = new EventBus();
            var store = new WorkspaceStore(bus, new MockViewportRangeCalculator(),
                new MockViewportNavigationService(), new MockVolumeStateService());
            var start = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            store.Dispatch(new UpdateSettingsAction(st => st with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, Path.Length)
                    .Select(i => new Ohlcv(start.AddDays(i), 100, 101, 99, 100, 1000))),
                ActiveSeries = ImmutableList.Create(s),
                FocusedSeriesId = s.Id,
                FocusedComponentIndex = s.Components.ToList().FindIndex(c => c.Name == p),
                CurrentDataIndex = 0,
            }));
            var spoken = new List<FeedbackRequestEvent>();
            bus.Subscribe<FeedbackRequestEvent>(spoken.Add);

            new IndicatorCrossingEngine(store, bus).HandleCrossJump(SystemCommand.NavRightJump);

            Assert.True(store.State.CurrentDataIndex == 5,
                $"{p} ({(histograms ? "histogram" : "wave")}): landed on {store.State.CurrentDataIndex}, said " +
                string.Join(" | ", spoken.Select(f => f.Message)));
            Assert.DoesNotContain(spoken, f => f.Message?.Contains("ver", StringComparison.OrdinalIgnoreCase) == true
                                               && f.Message.Contains("bought", StringComparison.OrdinalIgnoreCase));
        }
    }
}

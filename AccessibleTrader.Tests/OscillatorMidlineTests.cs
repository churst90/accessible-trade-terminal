using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>An oscillator is "above" or "below" by its OWN midline, not by the middle of the pane.</b>
///
/// <para>Cody, 2026-10-17, on Cipher B's 0–100 RSI and Stochastic: "doesn't follow the rule … above 0,
/// … below … are we patching a deeper problem with indicators with components with different bounds?"
/// Yes: the bright-above / warm-below timbre and the above/below sound patch split at the middle of the
/// pane's VISIBLE RANGE, while the above/below waveform split at the component's declared reference.
/// They agreed only where the two coincided. Cipher B's two lines (midline 50, pane middle 0) were
/// always "above"; the 28 auto-fit oscillators (Momentum, ROC, CCI, TRIX, PPO, funding, COT …) drifted
/// as the pane re-fitted.</para>
///
/// <para>Swept over every built-in oscillator component with a declared reference, built by the real
/// factory, in a pane deliberately shifted so its middle sits FAR above the midline: a value just
/// above its midline is below the pane's middle, and must still sound "above" — timbre and waveform.</para>
/// </summary>
public sealed class OscillatorMidlineTests
{
    private static readonly Ohlcv Bar = new(DateTime.UtcNow, 100, 110, 95, 105, 1000);

    private static IEnumerable<(ChartSeries S, ComponentConfig C, double Mid)> Oscillators()
    {
        var f = new IndicatorModelFactory(new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());
        foreach (var p in IndicatorProviderFixture.AllProviders())
        foreach (var m in p.GetIndicators())
        {
            ChartSeries s;
            try { s = f.CreateSeriesFromMetadata(m, m.Name, m.DefaultPane, new(), null); } catch { continue; }
            foreach (var c in s.Components)
                if (c.DisplayType == ComponentDisplayType.Oscillator && c.ReferenceLevel is double mid)
                    yield return (s, c, mid);
        }
    }

    [Fact]
    public void Every_oscillator_sounds_above_or_below_by_its_own_midline_wherever_the_pane_sits()
    {
        var all = Oscillators().ToList();
        Assert.True(all.Count >= 40, $"only {all.Count} oscillators with a midline swept; the sweep is not reaching the fleet");
        Assert.Contains(all, x => x.S.IndicatorCode == "CIPHER_B" && x.C.DisplayName == "RSI");

        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var wrong = new List<string>();
        foreach (var (s, c, mid) in all)
        {
            double step = Math.Max(Math.Abs(mid) * 0.05, 0.01);
            var pane = (mid - 2 * step, mid + 40 * step);   // pane middle ≈ mid + 19 steps
            var above = strategy.CreateAudioPoint(s, c, mid + step, Bar, 5, 10, pane, 1f);
            var below = strategy.CreateAudioPoint(s, c, mid - step, Bar, 5, 10, pane, 1f);

            if (!(above.SquareMix > 0)) wrong.Add($"{s.Name} / {c.DisplayName ?? c.Name}: just above its midline {mid} has no bright (square) timbre");
            if (below.SquareMix != 0 || !(below.TriangleMix > 0)) wrong.Add($"{s.Name} / {c.DisplayName ?? c.Name}: just below its midline {mid} is not the warm (triangle) timbre");
            if (above.Waveform != c.AboveReferenceWaveform || below.Waveform != c.BelowReferenceWaveform)
                wrong.Add($"{s.Name} / {c.DisplayName ?? c.Name}: waveform disagrees with the timbre about the side");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}

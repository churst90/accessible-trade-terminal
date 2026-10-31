using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A bar's grit is its size against the biggest bar of its own kind in view (Cody,
/// 2026-10-30: "keep the money flow and wt histogram sonified as a histogram, but do double check
/// the grittiness as it's hard to hear").</b>
///
/// <para>Grit was the bar's size against the PANE's peak. Cipher B's pane is fixed at ±100 and
/// its two bars are far smaller than that: on Bitstamp's Bitcoin daily history the WT Histogram's
/// median bar is 5.5 (grit 0.016 of 0.30; nine in ten under 0.045) and Money Flow's 17.6 (0.053);
/// on Coinbase's 15-minute bars 6.7 (0.020) and 12.5 (0.037). Nothing in view reached the top, so
/// every bar was close to a clean tone. Volume was never hit: its pane fits its bars.</para>
///
/// <para>Measured through the strategy's own entry points with real Cipher B output: the playback
/// path (<see cref="DefaultSonificationStrategy.MapComponentToAudio"/>) and the arrowing path
/// (<see cref="NavigationSonifier.CreateAudioPoint"/>), each over every 100-bar view.</para>
/// </summary>
public sealed class CipherBBarGritTests
{
    private const int View = 100;

    public static TheoryData<string> Charts => new() { "bitstamp_BTC_USDT_1d.csv", "coinbase_BTC_USD_15m_2022-01-20_31.csv" };

    private static ChartSeries CipherB(List<Ohlcv> bars)
    {
        var factory = new IndicatorModelFactory(
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());
        var meta = new CipherBProvider().GetIndicators()[0];
        // Read as histograms: grit is the bars' texture. Read as waves (their first form,
        // 2026-10-30) they are pitched by value instead (CipherBWaveFormTests).
        var s = ComponentForms.InOtherForm(factory, meta);
        Assert.All(new[] { CipherBProvider.CompMoneyFlowWave, CipherBProvider.CompWtHistogram },
            n => Assert.Equal(ComponentDisplayType.Histogram, s.Components.Single(c => c.Name == n).DisplayType));
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", bars.ToArray(), new Dictionary<string, object>(), new IndicatorResultBuffer(results, bars.Count));
        foreach (var (k, v) in results) s.Data.ComponentData[k] = v;
        return s;
    }

    [Theory]
    [MemberData(nameof(Charts))]
    public void Cipher_Bs_bars_take_grit_on_their_own_scale_when_played_and_arrowed(string chart)
    {
        var bars = CipherSrMarketCipherFidelityTests.Csv(chart);
        var s = CipherB(bars);
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var nav = new NavigationSonifier(Substitute.For<IAudioDriver>(), strategy, new SoundPatchRegistry());
        var pane = ViewportRangeCalculator.EmptyPaneRange("Pane_CIPHER_B");   // the declared ±100
        Assert.Equal((-100.0, 100.0), pane);

        foreach (var name in new[] { CipherBProvider.CompMoneyFlowWave, CipherBProvider.CompWtHistogram })
        {
            int ci = s.Components.FindIndex(c => c.Name == name);
            Assert.True(ci >= 0, $"{name} is not a component of Cipher B");
            var data = s.GetComponentData(name);
            var played = new List<double>();
            int views = 0;
            for (int start = 200; start + View <= bars.Count; start += View)
            {
                double peak = 0;
                for (int i = start; i < start + View; i++) if (!double.IsNaN(data[i])) peak = Math.Max(peak, Math.Abs(data[i]));
                if (peak <= 0) continue;
                views++;
                float top = 0;
                for (int i = start; i < start + View; i++)
                {
                    if (double.IsNaN(data[i])) continue;
                    var p = strategy.MapComponentToAudio(s, ci, i, bars, i - start, View, pane, 1f);
                    var a = nav.CreateAudioPoint(s, ci, bars[i], i - start, View, pane, i);
                    double want = 0.30 * Math.Abs(data[i]) / peak;
                    Assert.True(Math.Abs(p.SubSawMix - want) < 1e-4, $"{chart} {name} bar {i} played with grit {p.SubSawMix:F4}, its size gives {want:F4}");
                    Assert.True(Math.Abs(a.SubSawMix - want) < 1e-4, $"{chart} {name} bar {i} arrowed with grit {a.SubSawMix:F4}, its size gives {want:F4}");
                    top = Math.Max(top, p.SubSawMix);
                    played.Add(p.SubSawMix);
                }
                Assert.Equal(0.30f, top, 4);   // the biggest bar in view carries full weight
            }
            Assert.True(views >= 2, $"{chart}: only {views} views");
            played.Sort();
            // Old rule: medians 0.016-0.053 and nine in ten of the histogram's bars under 0.045.
            Assert.True(played[played.Count / 2] >= 0.06, $"{chart} {name}: median grit {played[played.Count / 2]:F3}");
        }
    }
}

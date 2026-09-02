using AccessibleTrader.Core.Services.Theming;
using SkiaSharp;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins <see cref="WcagContrast"/> to the numbers the guideline publishes. The 2026-09-01
    /// audit's headline was that nothing in the app had ever computed a contrast ratio; the
    /// pairs below are the ones that tell a real ratio from every proxy the app used instead.
    /// </summary>
    public class WcagContrastTests
    {
        private static SKColor Hex(string h) => SKColor.Parse(h);

        [Fact]
        public void Black_on_white_is_21_and_a_colour_on_itself_is_1()
        {
            Assert.Equal(21.0, WcagContrast.Ratio(SKColors.Black, SKColors.White), 2);
            Assert.Equal(1.0, WcagContrast.Ratio(Hex("#3a4048"), Hex("#3a4048")), 6);
        }

        [Fact]
        public void The_audits_headline_pair_measures_2_44()
        {
            // #0000ff on #000000: squared Euclidean distance 65,025 against the old editor's
            // 12,000 threshold, so it was waved through. It is 2.44:1 — below every floor.
            double r = WcagContrast.Ratio(Hex("#0000ff"), Hex("#000000"));
            Assert.Equal(2.44, r, 2);
            Assert.False(WcagContrast.Passes(Hex("#0000ff"), Hex("#000000"), WcagContrast.LargeTextMinimum));
        }

        [Fact]
        public void The_boundary_pair_is_on_the_right_side_only_with_the_gamma_curve()
        {
            // The canonical WebAIM boundary: #767676 on white is 4.54:1 and passes; #777777 is
            // 4.48:1 and does not. A gamma-less luminance puts both on the same side of 4.5, so
            // this is the case that tells the sRGB transfer curve from its absence.
            Assert.Equal(4.54, WcagContrast.Ratio(Hex("#767676"), SKColors.White), 2);
            Assert.Equal(4.48, WcagContrast.Ratio(Hex("#777777"), SKColors.White), 2);
            Assert.True(WcagContrast.Passes(Hex("#767676"), SKColors.White, WcagContrast.TextMinimum));
            Assert.False(WcagContrast.Passes(Hex("#777777"), SKColors.White, WcagContrast.TextMinimum));
        }

        [Fact]
        public void The_ratio_does_not_care_which_colour_is_on_top()
        {
            Assert.Equal(WcagContrast.Ratio(Hex("#123456"), Hex("#fedcba")),
                         WcagContrast.Ratio(Hex("#fedcba"), Hex("#123456")), 10);
        }

        [Fact]
        public void A_translucent_foreground_is_measured_as_the_colour_the_eye_sees()
        {
            // Half-alpha white over black composites to #808080, which is 5.32:1 on black —
            // not the 21:1 that ignoring alpha would report.
            var half = new SKColor(255, 255, 255, 128);
            Assert.Equal(5.32, WcagContrast.Ratio(half, SKColors.Black), 1);
        }

        [Fact]
        public void MostContrasting_picks_by_ratio_not_by_order()
        {
            Assert.Equal(SKColors.White, WcagContrast.MostContrasting(SKColors.Black, Hex("#0c0f14"), SKColors.White));
            Assert.Equal(Hex("#0c0f14"), WcagContrast.MostContrasting(SKColors.White, SKColors.White, Hex("#0c0f14")));
        }

        [Fact]
        public void Format_writes_the_ratio_the_way_the_guideline_does()
        {
            Assert.Equal("4.50:1", WcagContrast.Format(4.5));
            Assert.Equal("21.00:1", WcagContrast.Format(21));
        }
    }
}

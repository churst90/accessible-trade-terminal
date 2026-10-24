using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Cipher A's declared shape since the 2026-10-17 rebuild from Market Cipher A: what each
    /// component is drawn as, where, and in which colour — the colours being the ones measured on
    /// real Market Cipher A charts. The rules themselves are pinned in <see cref="CipherAProviderTests"/>.
    /// </summary>
    public class CipherAMetadataTests
    {
        private static IndicatorMetadata Meta() => new CipherAProvider().GetIndicators().Single();
        private static IndicatorComponentMetadata Get(string name) => Meta().Components.Single(c => c.Name == name);

        [Fact]
        public void CipherA_IsOfferedOnThePricePaneUnderItsOldCode()
        {
            var m = Meta();
            Assert.Equal("CIPHER_A", m.Code);
            Assert.Equal("Main", m.DefaultPane);
            Assert.False(m.IsDeprecated);
            Assert.Equal(ComponentCausality.Causal, m.Causality);
        }

        [Theory]
        [InlineData(CipherAProvider.CompGreenDot,      ComponentDisplayType.Dot)]
        [InlineData(CipherAProvider.CompBlueTriangle,  ComponentDisplayType.TriangleUp)]
        [InlineData(CipherAProvider.CompYellowDiamond, ComponentDisplayType.Diamond)]
        [InlineData(CipherAProvider.CompRedX,          ComponentDisplayType.Cross)]
        [InlineData(CipherAProvider.CompRedDiamond,    ComponentDisplayType.Diamond)]
        [InlineData(CipherAProvider.CompBloodDiamond,  ComponentDisplayType.Diamond)]
        [InlineData(CipherAProvider.CompYellowX,       ComponentDisplayType.Cross)]
        public void EachSymbolIsDrawnAsMarketCipherADrawsIt(string name, ComponentDisplayType shape)
        {
            var c = Get(name);
            Assert.Equal(shape, c.DisplayType);
            Assert.Equal(ComponentRole.Signal, c.Role);
            Assert.False(string.IsNullOrWhiteSpace(c.DefaultSignalSpeechTemplate));
            // The narration names the symbol the way Market Cipher A's users do.
            Assert.StartsWith(name.ToLowerInvariant().Split(' ')[0], c.DefaultSignalSpeechTemplate!.ToLowerInvariant());
        }

        /// <summary>The Blood Diamond is the big one: it stands for a Red Diamond and a Red X together.</summary>
        [Fact]
        public void TheBloodDiamondIsTheBiggestDiamond()
        {
            Assert.True(Get(CipherAProvider.CompBloodDiamond).DefaultThickness > Get(CipherAProvider.CompRedDiamond).DefaultThickness);
            Assert.True(Get(CipherAProvider.CompBloodDiamond).DefaultThickness > Get(CipherAProvider.CompYellowDiamond).DefaultThickness);
        }

        /// <summary>
        /// Each ribbon line is a two-colour line against its neighbour: Market Cipher A 2.5.0's uptrend
        /// ladder from #0066CC (EMA 5) to #E6F2FF (EMA 33), grey #505662 otherwise, all width 2 (the
        /// study's style table, 2026-10-23). The last line is blue BELOW its sibling, so its colours are
        /// the other way round.
        /// </summary>
        [Fact]
        public void EachRibbonLineIsColouredAgainstItsNeighbour()
        {
            string[] ladder = { "#0066CC", "#1A8CFF", "#4DA6FF", "#80BFFF", "#99CCFF", "#99CCFF", "#CCE6FF", "#E6F2FF" };
            for (int k = 0; k < 8; k++)
            {
                var c = Get(CipherAProvider.RibbonLine(k));
                Assert.Equal(ComponentDisplayType.Line, c.DisplayType);
                bool last = k == 7;
                Assert.Equal(CipherAProvider.RibbonLine(last ? 6 : k + 1), c.DefaultColorAboveComponent);
                Assert.Equal(last ? ladder[k] : "#505662", c.DefaultColorHex);
                Assert.Equal(last ? "#505662" : ladder[k], c.DefaultColorHexSecondary);
                Assert.Equal(2.0f, c.DefaultThickness);
                Assert.False(c.DefaultIsZoneLine);   // a moving average has nothing to break or test
                Assert.True(c.DefaultDrawOrder < 0);  // under the symbols
            }
        }

        [Fact]
        public void TheYellowDiamondIsTheQuietestSymbol()
        {
            // It fires on many candles in a row in a trend; it sits in the background, short.
            var yd = Get(CipherAProvider.CompYellowDiamond);
            Assert.Equal(PlaybackLayer.Background, yd.DefaultPlaybackLayer);
            Assert.True(Meta().Components.Where(c => c.DefaultEnvelopeType == "Ping").All(c => c.DefaultDecayMs >= yd.DefaultDecayMs));
        }

        /// <summary>
        /// Each symbol in Market Cipher A 2.5.0's own colour and size (its style table, 2026-10-23;
        /// #AARRGGBB where the original is see-through: the Yellow Diamond at 30%, the Red Diamond at 55%).
        /// Sizes: the original's "tiny" is 8, its "small" 12.
        /// </summary>
        [Theory]
        [InlineData(CipherAProvider.CompYellowDiamond, "#4DFDD835", 8f)]
        [InlineData(CipherAProvider.CompBlueTriangle,  "#4259F4",   12f)]
        [InlineData(CipherAProvider.CompGreenDot,      "#00E676",   8f)]
        [InlineData(CipherAProvider.CompRedX,          "#F23645",   8f)]
        [InlineData(CipherAProvider.CompYellowX,       "#FDD835",   12f)]
        [InlineData(CipherAProvider.CompRedDiamond,    "#8CF23645", 8f)]
        [InlineData(CipherAProvider.CompBloodDiamond,  "#F23645",   12f)]
        public void EachSymbolHasMarketCipherAsColourAndSize(string name, string hex, float size)
        {
            var c = Get(name);
            Assert.Equal(hex, c.DefaultColorHex);
            Assert.Equal(size, c.DefaultThickness);
            Assert.NotEqual(false, c.DefaultIsEnabled);   // every symbol is included by default
        }

        /// <summary>The original's Ribbon 9 and pivot lines exist but are switched off; here they are left
        /// out until the user includes them, in the original's colours.</summary>
        [Theory]
        [InlineData(CipherAProvider.CompRibbon9,   "#FDD835",   2f)]
        [InlineData(CipherAProvider.CompPivotHigh, "#CC4DA6FF", 1f)]
        [InlineData(CipherAProvider.CompPivotLow,  "#CC4DA6FF", 1f)]
        public void RibbonNineAndThePivotsAreLeftOutUntilIncluded(string name, string hex, float width)
        {
            var c = Get(name);
            Assert.Equal(ComponentDisplayType.Line, c.DisplayType);
            Assert.False(c.DefaultIsEnabled);
            Assert.Equal(hex, c.DefaultColorHex);
            Assert.Equal(width, c.DefaultThickness);
        }

        /// <summary>The two shaded bands: EMA 5 to EMA 33 at 10%, EMA 5 to EMA 11 at 15%.</summary>
        [Fact]
        public void TheRibbonIsShadedAsMarketCipherAShadesIt()
        {
            var fills = Meta().DefaultCloudFills!;
            Assert.Equal(2, fills.Count);
            Assert.Contains(fills, f => f.UpperComponentName == "EMA 5" && f.LowerComponentName == "EMA 33" && f.BullishColorHex == "#1AB3D9FF" && f.BearishColorHex == "#1AB3D9FF" && f.IsVisible);
            Assert.Contains(fills, f => f.UpperComponentName == "EMA 5" && f.LowerComponentName == "EMA 11" && f.BullishColorHex == "#2680B3FF" && f.BearishColorHex == "#2680B3FF" && f.IsVisible);
            Assert.All(fills, f => Assert.Null(f.Sonification));
        }
    }
}

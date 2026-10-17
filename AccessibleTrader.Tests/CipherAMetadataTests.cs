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
        /// Each ribbon line is a two-colour line against its neighbour: MCA's uptrend ladder from
        /// #1573D4 (EMA 5) to #DFECFB (EMA 33), grey #5A6372 otherwise. The last line is blue BELOW its
        /// sibling, so its colours are the other way round.
        /// </summary>
        [Fact]
        public void EachRibbonLineIsColouredAgainstItsNeighbour()
        {
            string[] ladder = { "#1573D4", "#3096FF", "#57ABFF", "#85C2FF", "#9BCDFF", "#B3D9FF", "#C9E5FF", "#DFECFB" };
            for (int k = 0; k < 8; k++)
            {
                var c = Get(CipherAProvider.RibbonLine(k));
                Assert.Equal(ComponentDisplayType.Line, c.DisplayType);
                bool last = k == 7;
                Assert.Equal(CipherAProvider.RibbonLine(last ? 6 : k + 1), c.DefaultColorAboveComponent);
                Assert.Equal(last ? ladder[k] : "#5A6372", c.DefaultColorHex);
                Assert.Equal(last ? "#5A6372" : ladder[k], c.DefaultColorHexSecondary);
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
    }
}

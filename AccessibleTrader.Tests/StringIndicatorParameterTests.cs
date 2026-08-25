using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Guards the string-parameter path end to end — the sibling of
    /// <see cref="BoolIndicatorParameterTests"/>, and the same class of bug one type further on.
    ///
    /// <para>
    /// <c>SeriesConfig.Parameters</c> was <c>Dictionary&lt;string, double&gt;</c>, and
    /// <c>IndicatorModelFactory</c> admitted a parameter only if <c>TryParseParamValue</c>
    /// accepted it. Every <c>typeof(string)</c> parameter in the catalogue was therefore
    /// structurally unreachable: the UI collected "ETH/USD", "Fixed", "Daily", "EMA", the
    /// config dropped it with no error, and the provider read its hardcoded default. Seven
    /// parameters across five indicators, and with them <c>COMPARE</c> / <c>COMPARE_RATIO</c>
    /// (blank forever — <c>BuildRequest</c> returns null on an empty symbol), Cipher B's
    /// Percentile threshold mode and its four feeding parameters, MA Cloud's MA-type selector,
    /// and Pivot Levels' period.
    /// </para>
    ///
    /// <para>
    /// These tests go red if the routing in <c>IndicatorModelFactory.ApplyParameters</c>, the
    /// merge in <c>ChartSeries.BuildParameterMap</c>, or the workspace round-trip through
    /// <c>SeriesConfig.StringParameters</c> is removed.
    /// </para>
    /// </summary>
    public class StringIndicatorParameterTests
    {
        [Theory]
        [InlineData("ETH/USD")]
        [InlineData("Fixed")]
        [InlineData("Daily")]
        [InlineData("EMA")]
        [InlineData("Bitstamp")]
        public void NonNumericValuesAreRejectedByTheNumericParser(string raw)
        {
            // The numeric parser is not at fault and is not what changed — it still refuses
            // these. What changed is that the caller no longer throws them away.
            Assert.False(IndicatorModelFactory.TryParseParamValue(raw, out _));
        }

        [Fact]
        public void ApplyParametersRoutesEachValueToTheDictionaryThatCanHoldIt()
        {
            var config = new SeriesConfig { IndicatorCode = "COMPARE" };

            IndicatorModelFactory.ApplyParameters(config, new List<(string, string)>
            {
                ("Period", "14"),
                ("Deviations", "2.5"),
                ("AdaptiveBreak", "true"),
                ("Symbol", "ETH/USD"),
                ("Market", "Crypto"),
                ("Provider", "Bitstamp"),
            });

            Assert.Equal(14.0, config.Parameters["Period"]);
            Assert.Equal(2.5, config.Parameters["Deviations"]);
            Assert.Equal(1.0, config.Parameters["AdaptiveBreak"]);

            Assert.Equal("ETH/USD", config.StringParameters["Symbol"]);
            Assert.Equal("Crypto", config.StringParameters["Market"]);
            Assert.Equal("Bitstamp", config.StringParameters["Provider"]);

            // No value lands in both, and nothing was silently discarded.
            Assert.Empty(config.Parameters.Keys.Intersect(config.StringParameters.Keys));
            Assert.Equal(6, config.Parameters.Count + config.StringParameters.Count);
        }

        [Fact]
        public void BuildParameterMapCarriesBothDictionariesToTheProvider()
        {
            var config = new SeriesConfig { IndicatorCode = "COMPARE" };
            IndicatorModelFactory.ApplyParameters(config, new List<(string, string)>
            {
                ("Period", "20"),
                ("Symbol", "ETH/USD"),
            });

            var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
            var map = series.BuildParameterMap();

            // The provider contract is Dictionary<string, object>: a double stays a double and
            // a string arrives as a string, which is what ReadString on the provider side needs.
            Assert.Equal(20.0, Assert.IsType<double>(map["Period"]));
            Assert.Equal("ETH/USD", Assert.IsType<string>(map["Symbol"]));
        }

        [Fact]
        public void AnEmptyStringIsNotStoredAsAParameter()
        {
            // An unset optional knob (SymbolCompare's Provider defaults to "") must stay absent
            // rather than being stored as "", so the provider's own fallback still applies.
            var config = new SeriesConfig();
            IndicatorModelFactory.ApplyParameters(config, new List<(string, string)>
            {
                ("Provider", "   "),
                ("Symbol", ""),
            });

            Assert.Empty(config.StringParameters);
            Assert.Empty(config.Parameters);
        }
    }
}

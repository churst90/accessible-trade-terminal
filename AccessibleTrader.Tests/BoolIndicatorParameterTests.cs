using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Guards the boolean-parameter path end to end.
    ///
    /// <para>
    /// Before this was fixed, bool indicator parameters were silently non-functional across the
    /// whole app. <c>SeriesManagementService.FormatParam</c> renders a bool as "true"/"false",
    /// and <c>IndicatorModelFactory</c> admitted a parameter only if <c>double.TryParse</c>
    /// accepted it — which it never does for those words. So the value was dropped on the way
    /// into <c>SeriesConfig.Parameters</c>, the provider fell back to its hardcoded default, and
    /// the user's setting did nothing. No error, no warning; the knob simply had no effect. That
    /// affected Cipher SR's AdaptiveBreak and Cipher B's anchor suppression as well.
    /// </para>
    /// </summary>
    public class BoolIndicatorParameterTests
    {
        [Theory]
        [InlineData("true", 1.0)]
        [InlineData("True", 1.0)]
        [InlineData("false", 0.0)]
        [InlineData("False", 0.0)]
        public void BoolWordsParseToOneAndZero(string raw, double expected)
        {
            Assert.True(IndicatorModelFactory.TryParseParamValue(raw, out double v));
            Assert.Equal(expected, v);
        }

        [Theory]
        [InlineData("14", 14.0)]
        [InlineData("0.5", 0.5)]
        [InlineData("-3", -3.0)]
        public void NumericValuesStillParse(string raw, double expected)
        {
            Assert.True(IndicatorModelFactory.TryParseParamValue(raw, out double v));
            Assert.Equal(expected, v);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("not a number")]
        public void UnparseableValuesAreRejectedRatherThanDefaultingToZero(string? raw)
        {
            // Returning false matters: a silent 0 would look like a deliberate "off".
            Assert.False(IndicatorModelFactory.TryParseParamValue(raw, out _));
        }

        [Fact]
        public void FormatParamAndParseRoundTripABool()
        {
            // The two halves live in different classes; this pins that they still agree.
            string formatted = InvokeFormatParam(true);
            Assert.True(IndicatorModelFactory.TryParseParamValue(formatted, out double v));
            Assert.Equal(1.0, v);

            formatted = InvokeFormatParam(false);
            Assert.True(IndicatorModelFactory.TryParseParamValue(formatted, out v));
            Assert.Equal(0.0, v);
        }

        private static string InvokeFormatParam(object value)
        {
            var m = typeof(SeriesManagementService).GetMethod("FormatParam",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(m);
            return (string)m!.Invoke(null, new[] { value })!;
        }

        [Fact]
        public void ValueDeviationDeclaresItsSwitchesAsBooleans()
        {
            // If these ever revert to numeric, the UI silently goes back to a 0/1 spinner.
            var meta = new ValueDeviationProvider().GetIndicators().Single();
            var switches = meta.Parameters
                .Where(p => p.Name is "InvertForMomentum" or "RequireMomentumTurn")
                .ToList();

            Assert.Equal(2, switches.Count);
            Assert.All(switches, p => Assert.Equal(typeof(bool), p.DataType));
            // Every switch must explain what each state MEANS — a bare "invert" tells the user
            // nothing about which way round it is.
            Assert.All(switches, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
        }

        [Fact]
        public void InvertSwitchNamesBothMarketTypesSoTheStateIsUnambiguous()
        {
            var meta = new ValueDeviationProvider().GetIndicators().Single();
            var invert = meta.Parameters.Single(p => p.Name == "InvertForMomentum");

            Assert.Contains("OFF", invert.Description);
            Assert.Contains("ON", invert.Description);
            Assert.Contains("equit", invert.Description, System.StringComparison.OrdinalIgnoreCase);
            // Bitcoin was the only crypto that actually measured significant, so the description
            // must name it specifically rather than implying the setting is validated for crypto
            // as a category — nine other coins showed nothing either way.
            Assert.Contains("BITCOIN", invert.Description, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UNKNOWN", invert.Description, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}

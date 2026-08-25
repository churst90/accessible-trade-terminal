using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;

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
        public void ValueDeviationDeclaresItsSwitchAsABoolean()
        {
            // If this ever reverts to numeric, the UI silently goes back to a 0/1 spinner.
            var meta = new ValueDeviationProvider().GetIndicators().Single();
            var toggle = meta.Parameters.Single(p => p.Name == "RequireMomentumTurn");

            Assert.Equal(typeof(bool), toggle.DataType);
            // A checkbox gives no clue which way round it is, so the description must say what
            // BOTH states mean.
            Assert.Contains("ON", toggle.Description);
            Assert.Contains("OFF", toggle.Description);
        }

        [Fact]
        public void ValueDeviationHasNoInvertMode()
        {
            // The invert switch only made sense while the marks were framed as buy/sell entries.
            // The indicator describes support and resistance ZONES, and a reversal is a reversal
            // whichever way the asset trends — so there is nothing to invert. It was also never
            // validated: no crypto reading held up in either direction.
            var meta = new ValueDeviationProvider().GetIndicators().Single();
            Assert.DoesNotContain(meta.Parameters, p =>
                p.Name.Contains("Invert", System.StringComparison.OrdinalIgnoreCase));
        }
    }
}

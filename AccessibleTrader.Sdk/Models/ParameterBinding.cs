namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Puts a series' parameter-bound levels and bands where their parameters say: a level whose
    /// <see cref="LevelConfig.ValueParameter"/> is set takes that parameter's value, and a fixed band
    /// with an <see cref="ZoneBandConfig.AnchorParameter"/> moves by the parameter's change since
    /// its <see cref="ZoneBandConfig.AnchorValue"/>, keeping its height.
    ///
    /// <para>One number, one meaning: in Market Cipher B the "Over Sold Level 1" input is both the
    /// −60 line and the buy dot's threshold, and "Trigger 2" is both the dotted −53 line and where
    /// the money-flow bar sits (2026-10-31, read from MCB 2.4.0 by changing each input on
    /// TradingView). Without a binding the line and the calculation could disagree.</para>
    ///
    /// <para>Applied wherever levels or parameters are set: a new series, a restored one, Reset,
    /// and each setting typed in Properties. Idempotent. A parameter the series does not hold leaves its level
    /// or band alone.</para>
    /// </summary>
    public static class ParameterBinding
    {
        public static void Apply(SeriesConfig config) =>
            Apply(config.Levels, config.ZoneBands, config.Parameters);

        public static void Apply(IEnumerable<LevelConfig> levels, IEnumerable<ZoneBandConfig> bands,
            IReadOnlyDictionary<string, double> parameters)
        {
            foreach (var level in levels)
                if (!string.IsNullOrEmpty(level.ValueParameter)
                    && parameters.TryGetValue(level.ValueParameter, out double v) && !double.IsNaN(v))
                    level.Value = v;

            foreach (var band in bands)
            {
                if (string.IsNullOrEmpty(band.AnchorParameter) || !band.IsFixedMode || double.IsNaN(band.AnchorValue)) continue;
                if (!parameters.TryGetValue(band.AnchorParameter, out double a) || double.IsNaN(a)) continue;
                double shift = a - band.AnchorValue;
                if (shift == 0) continue;
                band.FixedTop    += shift;
                band.FixedBottom += shift;
                band.AnchorValue  = a;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Regime Filter — exposes the textbook 200-period MA regime as a leaf-comparable
    /// signal for strategies. Two components, both shaped as <c>Close − MA(close, 200)</c>:
    ///
    ///   • <see cref="CompAboveSma"/>  — <c>Close − SMA(200)</c>. Mebane Faber 2007
    ///     "A Quantitative Approach to Tactical Asset Allocation" used SMA, not EMA —
    ///     slower means fewer whipsaws and is the form that has been published and
    ///     replicated for nearly 20 years.
    ///   • <see cref="CompAboveEma"/>  — <c>Close − EMA(200)</c>. ~30% faster reaction,
    ///     more whipsaws in chop, included for direct comparison.
    ///
    /// Strategies leaf on these as <c>REGIME.AboveSma200 GreaterThan 0</c> (price above
    /// the MA = bull regime). The first 199 bars are NaN so the leaf evaluator's standard
    /// NaN-skip behavior keeps warmup honest.
    ///
    /// Promoted from StrategyLab (was a synthetic harness-only provider) to Core on
    /// 2026-04-09 after the rolling-window walk-forward stress test identified
    /// <c>BULL pulse + Close > SMA200</c> as the most robust cell on BTC daily across
    /// 10 rolling 1500-bar windows: 70% windows positive, 4 windows passing strict CI,
    /// mean +0.43R. The textbook Faber filter outperformed every Pulse v1-v12 confluence
    /// stack we built. The lesson: filter restraint beats stacked confluence.
    /// </summary>
    public sealed class RegimeProvider : IIndicatorProvider
    {
        private const int Period = 200;
        public const string Code = "REGIME";
        public const string CompAboveSma = "AboveSma200";
        public const string CompAboveEma = "AboveEma200";
        public const string CompRegimeState = "RegimeState";

        public string Name => "Regime Filter";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Name = "Regime Filter (200 MA)",
                Category = "Overlays",
                DefaultPane = "Main",
                Description = "Mebane Faber 200-period MA regime — Close − SMA/EMA. >0 means bull regime.",
                Parameters = new List<IndicatorParameterMetadata>(),
                Components = new List<IndicatorComponentMetadata>
                {
                    new()
                    {
                        Name = CompAboveSma, DisplayName = "Close − SMA(200)",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Close minus S M A two hundred {value:F2}.",
                    },
                    new()
                    {
                        Name = CompAboveEma, DisplayName = "Close − EMA(200)",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Close minus E M A two hundred {value:F2}.",
                    },
                    // Ternary regime state: +1 bull, -1 bear, 0 neutral/warmup.
                    // Strategies can use a single GreaterThan/LessThan leaf against this instead
                    // of doing arithmetic on AboveSma/AboveEma. Visible so the oscillator is
                    // readable when opened in the indicator browser.
                    new()
                    {
                        Name = CompRegimeState, DisplayName = "Regime State",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = true,
                        DefaultColorHex = "#9E9E9E", DefaultThickness = 1.5f,
                        DefaultReferenceLevel = 0.0,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Regime {value:F0}.",
                    },
                }
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            var sma   = buffer.GetComponentSpan(CompAboveSma);
            var ema   = buffer.GetComponentSpan(CompAboveEma);
            var state = buffer.GetComponentSpan(CompRegimeState);

            // SMA — rolling sum, O(n).
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += (double)data[i].Close;
                if (i >= Period) sum -= (double)data[i - Period].Close;
                double deviation = i < Period - 1 ? double.NaN : (double)data[i].Close - sum / Period;
                sma[i] = deviation;
                // Ternary state derived from the SMA deviation — +1 bull, -1 bear, 0 warmup/flat.
                state[i] = double.IsNaN(deviation) ? 0.0 : (deviation > 0 ? 1.0 : (deviation < 0 ? -1.0 : 0.0));
            }

            // EMA — recursive, seeded with the SMA at index Period-1. Same convention as
            // every other EMA in the codebase to keep cross-indicator math consistent.
            double k = 2.0 / (Period + 1);
            double emaVal = 0;
            for (int i = 0; i < n; i++)
            {
                if (i < Period - 1) { ema[i] = double.NaN; continue; }
                if (i == Period - 1)
                {
                    double s = 0;
                    for (int j = 0; j < Period; j++) s += (double)data[j].Close;
                    emaVal = s / Period;
                }
                else
                {
                    emaVal = (double)data[i].Close * k + emaVal * (1 - k);
                }
                ema[i] = (double)data[i].Close - emaVal;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => Period + 1;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return string.Empty;
            if (!calculatedResults.TryGetValue(CompAboveSma, out var smaArr)) return string.Empty;
            if (index >= smaArr.Length) return string.Empty;
            double s = smaArr[index];
            if (double.IsNaN(s)) return "Regime: warming up.";
            return s > 0 ? $"Regime: bull (close above 200 SMA by {s:F0})." : $"Regime: bear (close below 200 SMA by {-s:F0}).";
        }

        public string GetSpeechFact(string code, string componentName, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters) => "";
    }
}

using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// BTC strength — metadata-only stub. The actual BtcRatio / BtcRatioMomentum
    /// values are produced by <c>WorkspaceFactory.ProjectBtcStrength</c> in the
    /// research lab (it loads a sibling BTC snapshot and computes the asset-vs-BTC
    /// log-ratio + 14-bar momentum). For live use the values would come from a
    /// cross-series cache hookup; that's a follow-up.
    ///
    /// Why this stub exists: the SignalCatalog only knows about indicators that
    /// register via <see cref="IIndicatorProvider.GetIndicators"/>. Without this
    /// stub, condition leaves like <c>"BTC_STRENGTH.BtcRatioMomentum > 0"</c>
    /// fail to resolve in the catalog and silently evaluate false. Same pattern
    /// as CANDLES / PRICE / VOLUME (CoreIndicatorProvider).
    /// </summary>
    public sealed class BtcStrengthProvider : IIndicatorProvider
    {
        public string Name => "BTC Strength";

        public const string Code             = "BTC_STRENGTH";
        public const string CompBtcRatio     = "BtcRatio";
        public const string CompBtcMomentum  = "BtcRatioMomentum";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = Code,
                Causality = ComponentCausality.Causal,
                Name        = "BTC Strength",
                Category    = "Cross-Asset",
                DefaultPane = "Pane_BTC_STRENGTH",
                Description =
                    "Asset-vs-BTC relative strength. BtcRatio = log(asset_close / btc_close) " +
                    "at the same bar timestamp; BtcRatioMomentum = current ratio − ratio 14 " +
                    "bars ago. Positive momentum = asset gaining vs BTC; negative = losing. " +
                    "Used as a contrarian gate for altcoin reversal strategies.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompBtcRatio, DisplayName = "BTC Ratio (log)",
                            DisplayType = ComponentDisplayType.Oscillator,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFB300", DefaultThickness = 1.5f,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultReferenceLevel = 0.0,
                            SpeechTemplate = "BTC ratio {value:F3}.",
                            IsVisible = true },
                    new() { Name = CompBtcMomentum, DisplayName = "BTC Ratio Momentum",
                            DisplayType = ComponentDisplayType.Oscillator,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF7043", DefaultThickness = 1.5f,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultReferenceLevel = 0.0,
                            SpeechTemplate = "BTC momentum {value:F3}.",
                            IsVisible = true },
                },
                Parameters = new List<IndicatorParameterMetadata>(),
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            // No-op: actual values are supplied by WorkspaceFactory.ProjectBtcStrength
            // (research lab) or — in future — by a cross-series cache hookup. Fill with
            // NaN so consumers don't see uninitialised buffer memory if the projection
            // path isn't taken.
            int n = data.Length;
            var rSpan = buffer.GetComponentSpan(CompBtcRatio);
            var mSpan = buffer.GetComponentSpan(CompBtcMomentum);
            for (int i = 0; i < n; i++)
            {
                if (i < rSpan.Length) rSpan[i] = double.NaN;
                if (i < mSpan.Length) mSpan[i] = double.NaN;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 14;

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompBtcRatio    => $"BTC ratio {value:F3}.",
                CompBtcMomentum => $"BTC momentum {value:F3}.",
                _ => null
            };
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters) =>
            "BTC Strength: cross-asset relative-strength gauge.";
    }
}

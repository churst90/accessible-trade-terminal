using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using SkiaSharp;

namespace AccessibleTrader.Core.Services
{
    public interface IStylingService
    {
        SKPaint GetPaint(ComponentConfig component, float density);
        string GetDefaultColor(string indicatorCode, string componentName = "", ComponentDisplayType displayType = ComponentDisplayType.Line);
        string GetSecondaryColor(string indicatorCode, string componentName = "", ComponentDisplayType displayType = ComponentDisplayType.Line);
        SonificationProfile GetSonificationProfile(ComponentDisplayType displayType, ComponentRole role = ComponentRole.None, string componentName = "");
        float GetDefaultThickness(ComponentDisplayType displayType);
        ComponentDisplayType GetDisplayType(string indicatorCode, string componentName = "");
        ComponentRole GetComponentRole(string indicatorCode, string componentName);
        ColorSource GetColorSource(string indicatorCode, string componentName);
        string GetPane(string indicatorCode);
        string GetCategory(string indicatorCode);
        double? GetReferenceLevel(string indicatorCode, string componentName, ComponentDisplayType displayType);
        List<(string Name, double Value)> GetLevelComponents(string indicatorCode);
        bool GetIsAreaFill(string indicatorCode, string componentName, ComponentDisplayType displayType);
        bool GetUsePolarityColoring(string indicatorCode, string componentName, ComponentDisplayType displayType);
        string GetSpeechTemplate(string indicatorCode, string componentName, ComponentDisplayType displayType);
        double GetColorBaseline(string indicatorCode, string componentName);
    }

    public class StylingService : IStylingService
    {
        private readonly IComponentRoleMapper _roleMapper;
        private readonly ISonificationProfileProvider _profileProvider;
        private readonly IPaneAssignmentService _paneService;

        public StylingService(
            IComponentRoleMapper roleMapper,
            ISonificationProfileProvider profileProvider,
            IPaneAssignmentService paneService)
        {
            _roleMapper = roleMapper;
            _profileProvider = profileProvider;
            _paneService = paneService;
        }

        public SKPaint GetPaint(ComponentConfig component, float density)
        {
            SKColor color = SKColors.White;
            if (!SKColor.TryParse(component.ColorHex, out color)) color = SKColors.White;

            var paint = new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, component.Thickness) * density,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };

            if (component.DashStyle != DashStyle.Solid)
            {
                float[]? intervals = component.DashStyle switch
                {
                    DashStyle.Dash => new float[] { 10 * density, 5 * density },
                    DashStyle.Dot => new float[] { 2 * density, 5 * density },
                    DashStyle.DashDot => new float[] { 10 * density, 5 * density, 2 * density, 5 * density },
                    _ => null
                };
                if (intervals != null) paint.PathEffect = SKPathEffect.CreateDash(intervals, 0);
            }
            else if (component.DisplayType == ComponentDisplayType.Level ||
                     component.Role == ComponentRole.Boundary ||
                     component.Role == ComponentRole.Median)
            {
                // Default dash for levels if not explicitly set to something else
                paint.PathEffect = SKPathEffect.CreateDash(new float[] { 5 * density, 5 * density }, 0);
                paint.StrokeWidth = 1 * density;
            }

            return paint;
        }

        public ComponentRole GetComponentRole(string indicatorCode, string componentName)
            => _roleMapper.GetComponentRole(indicatorCode, componentName);

        public ColorSource GetColorSource(string indicatorCode, string componentName)
            => _roleMapper.GetColorSource(indicatorCode, componentName);

        public ComponentDisplayType GetDisplayType(string indicatorCode, string componentName = "")
        {
            // All custom providers (Cipher A/B/SR, SpiderLines, EmaFill) and Skender indicators
            // declare DisplayType in IndicatorComponentMetadata. The factory applies it directly.
            // This fallback path is only reached by the legacy CreateComponentConfig(code, name)
            // public method (used by non-metadata callers such as LevelConfig rendering).
            return _roleMapper.GetDisplayType(indicatorCode, componentName);
        }

        public double GetColorBaseline(string indicatorCode, string componentName)
        {
            // MFI ColorBaseline is now declared in SkenderIndicatorProvider's component metadata.
            // This method returns 0.0 (zero-crossing) for all remaining indicators.
            return 0.0;
        }

        public string GetPane(string indicatorCode) => _paneService.GetPane(indicatorCode);
        public string GetCategory(string indicatorCode) => _paneService.GetCategory(indicatorCode);

        public SonificationProfile GetSonificationProfile(ComponentDisplayType displayType, ComponentRole role = ComponentRole.None, string componentName = "")
        {
            if (displayType == ComponentDisplayType.Heatmap)
            {
                return new SonificationProfile(
                    Waveform: "sawtooth",
                    AboveWaveform: "sawtooth",
                    BelowWaveform: "sawtooth",
                    AmplitudeMapping: AmplitudeMapping.None,
                    PitchMapping: PitchMapping.Value,
                    BaseFrequency: 440,
                    FreqMultiplier: 1.0,
                    TriggerBoundaryClick: false,
                    EnvelopeType: "Sustain"
                );
            }
            return _profileProvider.GetProfile(displayType, role, componentName);
        }

        public string GetDefaultColor(string indicatorCode, string componentName = "", ComponentDisplayType displayType = ComponentDisplayType.Line)
        {
            // All custom providers (Cipher A/B/SR, SpiderLines, EmaFill) declare colors in
            // IndicatorComponentMetadata.DefaultColorHex — the factory applies them directly.
            // This method provides role/type-based fallbacks for Skender reflection-generated indicators.

            var role = _roleMapper.GetComponentRole(indicatorCode, componentName);
            var type = _roleMapper.GetDisplayType(indicatorCode, componentName);

            if (type == ComponentDisplayType.Heatmap)
                return "#FFA500"; // Orange for heatmap liquidity

            if (displayType == ComponentDisplayType.Level || role == ComponentRole.Boundary || role == ComponentRole.Median)
                return "#808080";

            return role switch
            {
                ComponentRole.PriceAction => "#26A69A",  // industry-standard bullish green (TradingView default)
                ComponentRole.Wick        => "#FFFFFF",
                ComponentRole.Volume      => "#00FF00",
                ComponentRole.Signal      => "#FFD700",
                ComponentRole.Histogram   => "#00FF00",
                _                         => "#FFFFFF"
            };
        }

        public string GetSecondaryColor(string indicatorCode, string componentName = "", ComponentDisplayType displayType = ComponentDisplayType.Line)
        {
            // All custom providers declare secondary colors in IndicatorComponentMetadata.DefaultColorHexSecondary.
            // This method provides role-based fallbacks for Skender reflection-generated indicators.
            if (_roleMapper.GetColorSource(indicatorCode, componentName) == ColorSource.PriceAction) return "#FF0000";
            var role = _roleMapper.GetComponentRole(indicatorCode, componentName);
            if (role == ComponentRole.Histogram) return "#FF0000";
            return "#FF4500";
        }

        public string GetSpeechTemplate(string indicatorCode, string componentName, ComponentDisplayType displayType)
            => "{name}, {type}, {value}";

        public double? GetReferenceLevel(string indicatorCode, string componentName, ComponentDisplayType displayType)
        {
            // Skender indicators are reflection-generated — their component metadata has no static
            // DefaultReferenceLevel field to set. These hard-codes provide the correct midpoint for
            // above/below audio waveform splitting and amplitude mapping on those indicators.
            // Custom providers (Cipher A/B/SR) declare DefaultReferenceLevel in metadata instead.
            string code = indicatorCode.ToUpper();
            if (code.Contains("RSI"))   return 50;
            if (code.Contains("MACD"))  return 0;
            if (code.Contains("STOCH")) return 50;
            return null;
        }

        public List<(string Name, double Value)> GetLevelComponents(string indicatorCode)
            => new();

        // This is called only when IndicatorComponentMetadata.DefaultIsAreaFill is null.
        // Custom providers should declare these explicitly in metadata.
        public bool GetIsAreaFill(string indicatorCode, string componentName, ComponentDisplayType displayType)
            => displayType is ComponentDisplayType.Area or ComponentDisplayType.Oscillator;

        // This is called only when IndicatorComponentMetadata.DefaultUsePolarityColoring is null.
        // Custom providers should declare these explicitly in metadata.
        public bool GetUsePolarityColoring(string indicatorCode, string componentName, ComponentDisplayType displayType)
        {
            var role = _roleMapper.GetComponentRole(indicatorCode, componentName);
            return role == ComponentRole.Histogram || role == ComponentRole.Signal;
        }

        public float GetDefaultThickness(ComponentDisplayType displayType)
            => displayType switch
            {
                ComponentDisplayType.Candle       => 4.0f,
                // Marker shapes need a visible size; 4 px = 8 px diameter circle / 8 px diamond at 1× density.
                ComponentDisplayType.Dot          => 4.0f,
                ComponentDisplayType.Diamond      => 4.0f,
                ComponentDisplayType.TriangleUp   => 4.0f,
                ComponentDisplayType.TriangleDown => 4.0f,
                ComponentDisplayType.Square       => 4.0f,
                ComponentDisplayType.Cross        => 3.0f,
                _                                 => 2.0f,
            };
    }
}

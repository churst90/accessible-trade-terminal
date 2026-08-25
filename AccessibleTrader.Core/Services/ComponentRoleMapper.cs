using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services;

/// <summary>
/// Maps indicator code + component name combinations to ComponentRole, ColorSource, and DisplayType.
/// Extracted from StylingService to isolate the role registry dictionary.
/// </summary>
public sealed class ComponentRoleMapper : IComponentRoleMapper
{
    private static readonly Dictionary<string, ComponentRole> RoleRegistry = new(StringComparer.OrdinalIgnoreCase)
    {
        { "PRICE.CLOSE",         ComponentRole.PriceAction },
        { "PRICE.OPEN",          ComponentRole.PriceAction },
        { "PRICE.HIGH",          ComponentRole.Wick },
        { "PRICE.LOW",           ComponentRole.Wick },
        { "PRICE.BODY",          ComponentRole.PriceAction },

        { "CANDLES.CLOSE",       ComponentRole.PriceAction },
        { "CANDLES.OPEN",        ComponentRole.PriceAction },
        { "CANDLES.HIGH",        ComponentRole.Wick },
        { "CANDLES.LOW",         ComponentRole.Wick },
        { "CANDLES.BODY",        ComponentRole.PriceAction },
        { "CANDLES.UPPER WICK",  ComponentRole.Wick },
        { "CANDLES.LOWER WICK",  ComponentRole.Wick },

        { "VOLUME.VOLUME",       ComponentRole.Volume },

        { "MACD.MACD",           ComponentRole.Signal },
        { "MACD.SIGNAL",         ComponentRole.Signal },
        { "MACD.HISTOGRAM",      ComponentRole.Histogram },

        { "RSI.RSI",             ComponentRole.Signal },
        { "RSI.OVERBOUGHT",      ComponentRole.Boundary },
        { "RSI.OVERSOLD",        ComponentRole.Boundary },
        { "STOCH.K",             ComponentRole.Signal },
        { "STOCH.D",             ComponentRole.Signal },
        { "MACD.ZERO",           ComponentRole.Median },
        { "HEATMAP.LIQUIDITY",   ComponentRole.Level },
        // Custom providers (Cipher A/B/SR, SpiderLines, EmaFill) declare Role directly in
        // IndicatorComponentMetadata — the factory applies it and never consults this mapper.
        // Only Skender reflection-generated indicators and core series (Price/Candles/Volume)
        // need entries here.
    };

    public ComponentRole GetComponentRole(string indicatorCode, string componentName)
    {
        string key = $"{indicatorCode.ToUpper()}.{componentName.ToUpper()}";
        if (RoleRegistry.TryGetValue(key, out var role)) return role;

        string comp = componentName.ToUpper();
        if (comp.Contains("UPPER") || comp.Contains("HIGH") || comp.Contains("TOP"))    return ComponentRole.Boundary;
        if (comp.Contains("LOWER") || comp.Contains("LOW")  || comp.Contains("BOTTOM")) return ComponentRole.Boundary;
        if (comp.Contains("SIGNAL"))                                                     return ComponentRole.Signal;
        if (comp.Contains("HISTOGRAM") || comp.Contains("BAR"))                         return ComponentRole.Histogram;
        if (comp.Contains("CENTER") || comp.Contains("MEDIAN"))                         return ComponentRole.Median;
        if (comp.Contains("BODY") || comp.Contains("CLOSE") || comp.Contains("OPEN"))   return ComponentRole.PriceAction;

        return ComponentRole.None;
    }

    public ColorSource GetColorSource(string indicatorCode, string componentName)
    {
        string code = indicatorCode.ToUpper();
        if (code.Contains("VOLUME")) return ColorSource.PriceAction;

        var role = GetComponentRole(indicatorCode, componentName);
        if (role == ComponentRole.Histogram) return ColorSource.Value;
        if (role == ComponentRole.Body || role == ComponentRole.Wick || role == ComponentRole.PriceAction)
            return ColorSource.PriceAction;

        return ColorSource.Value;
    }

    public ComponentDisplayType GetDisplayType(string indicatorCode, string componentName)
    {
        var role = GetComponentRole(indicatorCode, componentName);
        string code = indicatorCode.ToUpper();

        if (code.Contains("HEATMAP")) return ComponentDisplayType.Heatmap;
        if (role == ComponentRole.Histogram || role == ComponentRole.Volume) return ComponentDisplayType.Bar;
        if (role == ComponentRole.Wick) return ComponentDisplayType.Wick;
        if (role == ComponentRole.PriceAction)
            return code.Contains("CANDLES") ? ComponentDisplayType.Candle : ComponentDisplayType.Line;

        if (role == ComponentRole.Signal &&
            (code.Contains("RSI") || code.Contains("STOCH") || code.Contains("MACD")))
            return ComponentDisplayType.Oscillator;

        return ComponentDisplayType.Line;
    }
}

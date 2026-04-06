using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Alerts
{
    public record AlertEvaluationContext(
        string IndicatorCode,
        string ComponentName,
        double CurrentValue,
        double PreviousValue,
        Ohlcv CurrentBar,
        Ohlcv PreviousBar
    );
}

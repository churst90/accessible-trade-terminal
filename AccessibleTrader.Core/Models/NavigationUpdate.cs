using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Models
{
    /// <summary>
    /// An atomic packet containing all information needed to synchronize
    /// a single frame of visual, audio, and speech feedback.
    /// </summary>
    public record NavigationUpdate(
        Ohlcv Point,
        int DataIndex,
        ChartSeries FocusedSeries,
        int FocusedComponentIndex,
        AudioPoint Audio,
        string SpeechMessage,
        bool IsXMove,
        bool IsUserInitiated
    );
}
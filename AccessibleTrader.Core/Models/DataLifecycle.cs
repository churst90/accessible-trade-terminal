namespace AccessibleTrader.Core.Models
{
    public enum DataState
    {
        Initializing,
        HistoricalFilling,
        GapFilling,
        LiveStreaming,
        Stalled,
        Faulted
    }

    public enum DataTrigger
    {
        InitStarted,
        FetchHistoricalStarted,
        HistoricalDataReceived,
        GapFillStarted,
        LiveStreamStarted,
        TickReceived,
        NetworkLagged,
        ErrorOccurred,
        Reset
    }
}

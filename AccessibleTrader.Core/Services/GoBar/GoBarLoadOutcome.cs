namespace AccessibleTrader.Core.Services.GoBar
{
    /// <summary>What the Go bar's load request did, so the bar can say it.</summary>
    public enum GoBarLoadOutcomeKind
    {
        Loaded,
        NoChart,
        NotInDemo,
        ShapeChange,
        Failed,
    }

    /// <summary>
    /// The result of <c>IMarketOrchestrator.LoadOnActiveTabAsync</c>. The provider, symbol and
    /// timeframe are the values the load actually used, after the tab's identity and the demo
    /// guards, so the Go bar's "Loading …" sentence names what is really being loaded.
    /// </summary>
    public sealed record GoBarLoadOutcome(
        GoBarLoadOutcomeKind Kind,
        string Provider = "",
        string Symbol = "",
        string Timeframe = "",
        int NonCoreSeries = 0);
}

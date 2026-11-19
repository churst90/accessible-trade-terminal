using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Core.Services.GoBar
{
    /// <summary>
    /// What one Go bar option does when it runs. Stage 6b gives the four commands that had
    /// no handler (TF, SYM/GP, PROV, REF) their meaning here, and adds the verbs that take a
    /// value the registry has no entry for (ALERT with a price, GOTO, FIND).
    /// </summary>
    public abstract record GoBarAction;

    /// <summary>Change the chart on screen to this timeframe, keeping its symbol and provider.</summary>
    public sealed record GoBarTimeframeAction(string Timeframe, string Words) : GoBarAction;

    /// <summary>
    /// Open a symbol. <paramref name="SameVenue"/> is true when the symbol came from the
    /// current provider's list, so the active tab keeps its timeframe and every non-identity
    /// part of its selection.
    /// </summary>
    public sealed record GoBarSymbolAction(
        string Provider,
        string Symbol,
        MarketType Market,
        string SubType,
        bool SameVenue) : GoBarAction;

    /// <summary>Choose a provider in the toolbar and refresh its symbol list. Never loads.</summary>
    public sealed record GoBarProviderAction(string Provider) : GoBarAction;

    /// <summary>Add an indicator with its metadata defaults.</summary>
    public sealed record GoBarIndicatorAction(string Code, string Name) : GoBarAction;

    /// <summary>Open the alerts form with a price filled in. It does not create the alert.</summary>
    public sealed record GoBarPriceAlertAction(double Price) : GoBarAction;

    /// <summary>Move the cursor to the last bar at or before a wall-clock moment.</summary>
    public sealed record GoBarDateAction(DateTime Wall, bool HasTime, string Display) : GoBarAction;

    /// <summary>Take the chart to the latest placed bar for a market event.</summary>
    public sealed record GoBarEventAction(string EventId, string Name) : GoBarAction;

    /// <summary>Find the next candle pattern or chart formation right of the cursor.</summary>
    public sealed record GoBarFindAction(string Name, bool IsFormation) : GoBarAction;

    /// <summary>Reload the chart on screen with no changes.</summary>
    public sealed record GoBarRefreshAction : GoBarAction;

    /// <summary>
    /// A verb with no argument (or one of GOTO/FIND with no argument). It keeps the box open,
    /// completes the input to the verb plus a space, and says what to type next.
    /// </summary>
    public sealed record GoBarArgHintAction(string Prompt, string Completion) : GoBarAction;

    /// <summary>
    /// One line in the Go bar's list. Either a 6a command entry (with its existing meaning),
    /// or a resolved argument action. The box renders exactly these and reads
    /// <see cref="GoBarSuggestion.Sentence"/> from the same object.
    /// </summary>
    public sealed record GoBarOption(
        string Name,
        GoBarEntry? Entry = null,
        GoBarAction? Action = null,
        string? CanonicalText = null,
        string? Help = null,
        /// <summary>
        /// True when several symbols matched and Enter must not quietly run the first one.
        /// The user has to move onto one with the arrows (or click it) first.
        /// </summary>
        bool RequiresExplicitPick = false,
        string? ExplicitPickPrompt = null,
        /// <summary>How many options share this argument's group (only symbols use it).</summary>
        int GroupCount = 1,
        /// <summary>A sentence that describes the group rather than this one option.</summary>
        string? GroupSentence = null)
    {
        public bool IsArgument => Action != null;
    }

    /// <summary>The options for what has been typed, plus the one sentence that is read.</summary>
    public sealed record GoBarSuggestion(IReadOnlyList<GoBarOption> Options, string Sentence)
    {
        public bool HasMatches => Options.Count > 0;
    }
}

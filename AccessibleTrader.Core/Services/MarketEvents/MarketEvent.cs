using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>What a market event is about.</summary>
    public enum MarketEventKind
    {
        Economy,
        Fed,
        Expiry,
        Holiday,
        Halving,
        /// <summary>A company's quarterly report (stage 4b): on that company's charts only.</summary>
        Earnings,
    }

    /// <summary>How much it matters, in the three levels the setting can gate.</summary>
    public enum MarketEventImportance
    {
        High,
        Medium,
        Low,
    }

    /// <summary>Whether the event's moment is a real time or only its date is known.</summary>
    public enum MarketEventPrecision
    {
        Exact,
        DayOnly,
    }

    /// <summary>
    /// Where in the trading day a day-only event falls, when its source says so but gives no
    /// time: Finnhub's earnings calendar marks a report "bmo" (before market open) or "amc" (after
    /// market close). Spoken as those words, never as an invented clock time.
    /// </summary>
    public enum MarketEventSession
    {
        None,
        BeforeOpen,
        AfterClose,
    }

    /// <summary>What a figure is measured in. Percentages are spoken to one decimal; thousands are whole.</summary>
    public enum MarketEventFigureUnit
    {
        Percent,
        Thousands,
        /// <summary>Earnings per share, in the company's reporting currency, to the cent.</summary>
        PerShare,
        /// <summary>Revenue: spoken in billions or millions to one decimal.</summary>
        Money,
        /// <summary>An interest rate level, said to two decimals with a percent sign (the Fed
        /// rate's 4.25%); its surprise words are "higher" and "lower".</summary>
        Rate,
    }

    /// <summary>
    /// Which direction is "hot" for a figure. Stage 4a speaks it as the surprise word only when
    /// an actual and an expected figure both exist (design doc, section 1).
    /// </summary>
    public enum MarketEventFigureDirection
    {
        HigherIsHotter,
        HigherIsStronger,
        HigherIsWeaker,
    }

    /// <summary>
    /// One figure from a release: what was published, the figure it is measured against, its
    /// unit and which way is hot. A null actual means that source had nothing for this release.
    /// <see cref="Expected"/> is Kalshi's median outcome, or null when no market was stored; for
    /// earnings it is the analysts' consensus Finnhub publishes. <see cref="ExpectedBy"/> is said
    /// with it, because the source always travels with the number.
    /// </summary>
    public sealed record MarketEventFigure(
        string Name,
        double? Actual,
        double? Previous,
        MarketEventFigureUnit Unit,
        MarketEventFigureDirection Direction,
        double? Expected = null,
        string ExpectedBy = "Kalshi");

    /// <summary>
    /// The figures carried by one event. <see cref="EstimateNumber"/> and <see cref="Period"/> are
    /// GDP's: which estimate this release published, and the quarter it describes.
    /// </summary>
    public sealed record MarketEventFigures(
        IReadOnlyList<MarketEventFigure> Figures,
        int? EstimateNumber = null,
        DateOnly? Period = null)
    {
        /// <summary>
        /// True once the release has published a figure. Kalshi's merge gives an upcoming release
        /// an expected-only row, so a figures object alone does not mean the figure arrived.
        /// </summary>
        public static bool Published(MarketEvent e)
            => e.Figures?.Figures.Any(f => f.Actual is not null) == true;
    }

    /// <summary>
    /// The "Market events on the chart" setting. Off by default (Cody, 2026-11-18: "don't want to
    /// overwhelm new users").
    /// </summary>
    public enum MarketEventLevel
    {
        Off,
        ImportantOnly,
        ImportantAndMedium,
        All,
    }

    /// <summary>
    /// One scheduled thing that happened to a market: a US release, a Fed decision, an expiry, a
    /// holiday or a Bitcoin halving.
    ///
    /// <para>
    /// <see cref="MomentUtc"/> is the real instant when <see cref="Precision"/> is
    /// <see cref="MarketEventPrecision.Exact"/>. A <see cref="MarketEventPrecision.DayOnly"/>
    /// event carries its <see cref="Date"/> and a moment of that date's midnight in its own zone;
    /// the date, not the moment, is what placement reads. No field is ever blank: a source that
    /// gives only dates says so, and the wording says "time not given" rather than inventing one.
    /// </para>
    /// </summary>
    public sealed record MarketEvent(
        string Id,
        MarketEventKind Kind,
        string SpokenName,
        string LongName,
        IReadOnlyList<MarketType> Scope,
        MarketEventImportance Importance,
        DateTime MomentUtc,
        MarketEventPrecision Precision,
        DateOnly Date,
        string Source,
        MarketEventFigures? Figures = null,
        string? Symbol = null,
        MarketEventSession Session = MarketEventSession.None)
    {
        /// <summary>True when the event touches a chart of <paramref name="market"/>.</summary>
        public bool Touches(MarketType market) => Scope.Contains(market);

        /// <summary>
        /// True when the event belongs on a chart of <paramref name="symbol"/>: every market-wide
        /// event does, a company's earnings only on that company's own charts.
        /// </summary>
        public bool IsFor(string? symbol)
            => Symbol == null
               || string.Equals(Symbol, symbol, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when the time could be known but this record does not carry it: an expiry or a
        /// holiday is a whole-day fact by nature, and a halving's block time is not scheduled;
        /// a release or Fed decision that is day-only lost a time that did exist. Speech says
        /// "time not given" only for the second group.
        /// </summary>
        public bool TimeCouldBeKnownButIsNot =>
            Precision == MarketEventPrecision.DayOnly
            && Session == MarketEventSession.None
            && Kind is MarketEventKind.Economy or MarketEventKind.Fed or MarketEventKind.Halving
                or MarketEventKind.Earnings;
    }

    /// <summary>The six kind tick boxes, as one value so they travel together.</summary>
    public readonly record struct MarketEventKindFilter(
        bool Economy,
        bool Fed,
        bool Expiries,
        bool Holidays,
        bool Halvings,
        bool Earnings = true)
    {
        public static MarketEventKindFilter All => new(true, true, true, true, true, true);

        public bool Includes(MarketEventKind kind) => kind switch
        {
            MarketEventKind.Economy => Economy,
            MarketEventKind.Fed => Fed,
            MarketEventKind.Expiry => Expiries,
            MarketEventKind.Holiday => Holidays,
            MarketEventKind.Earnings => Earnings,
            _ => Halvings,
        };
    }

    /// <summary>Parsing and wording for the level setting, in one place.</summary>
    public static class MarketEventLevels
    {
        public const string Off = "off";
        public const string ImportantOnly = "importantOnly";
        public const string ImportantAndMedium = "importantAndMedium";
        public const string All = "all";

        public static MarketEventLevel Parse(string? value) => value switch
        {
            ImportantOnly => MarketEventLevel.ImportantOnly,
            ImportantAndMedium => MarketEventLevel.ImportantAndMedium,
            All => MarketEventLevel.All,
            _ => MarketEventLevel.Off,
        };

        public static string ToSetting(MarketEventLevel level) => level switch
        {
            MarketEventLevel.ImportantOnly => ImportantOnly,
            MarketEventLevel.ImportantAndMedium => ImportantAndMedium,
            MarketEventLevel.All => All,
            _ => Off,
        };

        /// <summary>The choice as a screen reader should say it.</summary>
        public static string Label(MarketEventLevel level) => level switch
        {
            MarketEventLevel.ImportantOnly => "Important only",
            MarketEventLevel.ImportantAndMedium => "Important and medium",
            MarketEventLevel.All => "All",
            _ => "Off",
        };

        /// <summary>Whether an event of this importance passes the level.</summary>
        public static bool Includes(MarketEventLevel level, MarketEventImportance importance) => level switch
        {
            MarketEventLevel.Off => false,
            MarketEventLevel.ImportantOnly => importance == MarketEventImportance.High,
            MarketEventLevel.ImportantAndMedium => importance != MarketEventImportance.Low,
            _ => true,
        };
    }
}

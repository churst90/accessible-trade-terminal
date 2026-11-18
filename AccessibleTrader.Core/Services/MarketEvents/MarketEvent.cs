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

    /// <summary>What a figure is measured in. Percentages are spoken to one decimal; thousands are whole.</summary>
    public enum MarketEventFigureUnit
    {
        Percent,
        Thousands,
    }

    /// <summary>
    /// Which direction is "hot" for a figure. Stored and tested, but deliberately not spoken in
    /// stage 2: without an expected figure there is no surprise word (design doc, section 1).
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
    /// </summary>
    public sealed record MarketEventFigure(
        string Name,
        double? Actual,
        double? Previous,
        MarketEventFigureUnit Unit,
        MarketEventFigureDirection Direction);

    /// <summary>
    /// The figures carried by one event. <see cref="EstimateNumber"/> and <see cref="Period"/> are
    /// GDP's: which estimate this release published, and the quarter it describes.
    /// </summary>
    public sealed record MarketEventFigures(
        IReadOnlyList<MarketEventFigure> Figures,
        int? EstimateNumber = null,
        DateOnly? Period = null);

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
        MarketEventFigures? Figures = null)
    {
        /// <summary>True when the event touches a chart of <paramref name="market"/>.</summary>
        public bool Touches(MarketType market) => Scope.Contains(market);

        /// <summary>
        /// True when the time could be known but this record does not carry it: an expiry or a
        /// holiday is a whole-day fact by nature, and a halving's block time is not scheduled;
        /// a release or Fed decision that is day-only lost a time that did exist. Speech says
        /// "time not given" only for the second group.
        /// </summary>
        public bool TimeCouldBeKnownButIsNot =>
            Precision == MarketEventPrecision.DayOnly
            && Kind is MarketEventKind.Economy or MarketEventKind.Fed or MarketEventKind.Halving;
    }

    /// <summary>The five kind tick boxes, as one value so they travel together.</summary>
    public readonly record struct MarketEventKindFilter(
        bool Economy,
        bool Fed,
        bool Expiries,
        bool Holidays,
        bool Halvings)
    {
        public static MarketEventKindFilter All => new(true, true, true, true, true);

        public bool Includes(MarketEventKind kind) => kind switch
        {
            MarketEventKind.Economy => Economy,
            MarketEventKind.Fed => Fed,
            MarketEventKind.Expiry => Expiries,
            MarketEventKind.Holiday => Holidays,
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

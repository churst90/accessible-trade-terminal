namespace AccessibleTrader.StrategyLab.Macro;

/// <summary>
/// The day a CFTC Commitments of Traders report became public, from its report ("as of") date.
///
/// <para><b>Why not "Tuesday + 3".</b> The report is usually released on the Friday after its
/// Tuesday, at 3:30 pm Eastern. A federal holiday on the Wednesday, Thursday or Friday of that week
/// pushes the release to the next business day (the published schedules mark these: 2013-07-08,
/// 2019-07-08, 2019-12-02, 2026-01-05, 2026-06-22, 2026-11-16 and others). Three government
/// shutdowns (2013, 2018-19, 2025) and the 2023 ION cyber incident delayed releases by weeks, and a
/// "+3" rule would serve those numbers a month before anyone saw them. Those releases are taken
/// from the CFTC's own announcements (the COT special-announcements page and press releases
/// 6745-13, 7864-19, 9138-25 and 9147-25, saved as raw files by <c>macro-fetch --only cftc</c>).
/// Where an announcement gave only a cadence or a range, the latest date it allows is used.</para>
///
/// <para><b>The history floor.</b> The Traders in Financial Futures report was first published
/// in 2010; its history back to June 2006 was posted on 2010-12-03 (special announcement of that
/// day). Nobody could read a 2008 leveraged-funds number in 2008, so every TFF report dated before
/// that is known only from 2010-12-03. The disaggregated report's history was completed on
/// 2010-05-07 (special announcement of that day), and is floored there the same way.</para>
///
/// <para>The day convention is the macro layer's: known by the END of that day (the release is at
/// 3:30 pm Eastern, so it is usable at a US close or at Bitcoin's 8 pm Eastern UTC close).</para>
/// </summary>
public static class CftcReleaseCalendar
{
    public enum Report { Tff, Disaggregated }

    public static readonly DateOnly TffHistoryPublished = new(2010, 12, 3);
    public static readonly DateOnly DisaggHistoryPublished = new(2010, 5, 7);

    /// <summary>Report date → actual release, where an announcement moved it off the rule.</summary>
    public static readonly IReadOnlyDictionary<DateOnly, DateOnly> Exceptions = BuildExceptions();

    private static Dictionary<DateOnly, DateOnly> BuildExceptions()
    {
        var x = new Dictionary<DateOnly, DateOnly>();
        void Add(int y, int m, int d, int ry, int rm, int rd) => x[new DateOnly(y, m, d)] = new DateOnly(ry, rm, rd);
        // 2013 shutdown (Oct 1-16). PR 6745-13: the report due Oct 4 (data Oct 1) on Oct 25; two
        // more the week of Oct 28 and at least two the week of Nov 4; normal from Nov 8. The latest
        // day of each announced week is used.
        Add(2013, 10, 1, 2013, 10, 25);
        Add(2013, 10, 8, 2013, 11, 1);
        Add(2013, 10, 15, 2013, 11, 1);
        Add(2013, 10, 22, 2013, 11, 8);
        Add(2013, 10, 29, 2013, 11, 8);
        // 2014: federal holidays Dec 25 and 26 (executive order); the special announcement of
        // 2014-12-19 gives Tuesday Dec 30.
        Add(2014, 12, 23, 2014, 12, 30);
        // 2018-19 shutdown. PR 7864-19: the report for Monday 2018-12-24 on Friday 2019-02-01, then
        // one on each Tuesday and Friday until current. Eleven reports, eleven slots, ending at the
        // normal Friday 2019-03-08.
        var backlog19 = new[] { (2018, 12, 24), (2018, 12, 31), (2019, 1, 8), (2019, 1, 15), (2019, 1, 22), (2019, 1, 29),
                                (2019, 2, 5), (2019, 2, 12), (2019, 2, 19), (2019, 2, 26), (2019, 3, 5) };
        var slots19 = new[] { (2019, 2, 1), (2019, 2, 5), (2019, 2, 8), (2019, 2, 12), (2019, 2, 15), (2019, 2, 19),
                              (2019, 2, 22), (2019, 2, 26), (2019, 3, 1), (2019, 3, 5), (2019, 3, 8) };
        for (int k = 0; k < backlog19.Length; k++)
            Add(backlog19[k].Item1, backlog19[k].Item2, backlog19[k].Item3, slots19[k].Item1, slots19[k].Item2, slots19[k].Item3);
        // 2023 ION cyber incident (special announcements 2023-02-02 to 2023-03-21).
        Add(2023, 1, 31, 2023, 2, 24);
        Add(2023, 2, 7, 2023, 3, 3);
        Add(2023, 2, 14, 2023, 3, 8);
        Add(2023, 2, 21, 2023, 3, 10);
        Add(2023, 2, 28, 2023, 3, 14);
        Add(2023, 3, 7, 2023, 3, 16);
        Add(2023, 3, 14, 2023, 3, 21);
        // 2025 shutdown (Oct 1 - Nov 12). Two published catch-up tables (PR 9138-25 of Nov 18 and
        // PR 9147-25 of Dec 9); the later of the two dates is used for each report.
        Add(2025, 9, 30, 2025, 11, 19);
        Add(2025, 10, 7, 2025, 11, 21);
        Add(2025, 10, 14, 2025, 11, 25);
        Add(2025, 10, 21, 2025, 12, 2);
        Add(2025, 10, 28, 2025, 12, 5);
        Add(2025, 11, 4, 2025, 12, 9);
        Add(2025, 11, 10, 2025, 12, 12);
        Add(2025, 11, 18, 2025, 12, 16);
        Add(2025, 11, 25, 2025, 12, 19);
        Add(2025, 12, 2, 2025, 12, 23);
        Add(2025, 12, 9, 2025, 12, 30);
        Add(2025, 12, 16, 2026, 1, 6);
        Add(2025, 12, 23, 2026, 1, 9);
        Add(2025, 12, 30, 2026, 1, 13);
        Add(2026, 1, 6, 2026, 1, 16);
        Add(2026, 1, 13, 2026, 1, 20);
        return x;
    }

    /// <summary>Days the federal government was closed by executive order or national mourning
    /// (beyond the statutory holidays computed in <see cref="IsFederalHoliday"/>).</summary>
    private static readonly HashSet<DateOnly> AdHocClosures = new()
    {
        new(2007, 1, 2),   // President Ford, national day of mourning
        new(2012, 10, 29), new(2012, 10, 30), // Hurricane Sandy
        new(2012, 12, 24),
        new(2014, 12, 26),
        new(2018, 12, 5),  // President G. H. W. Bush
        new(2018, 12, 24),
        new(2019, 12, 24),
        new(2020, 12, 24),
        new(2024, 12, 24),
        new(2025, 1, 9),   // President Carter (special announcement 2025-01-07)
        new(2025, 12, 24), new(2025, 12, 26),
    };

    /// <summary>The first day the report dated <paramref name="reportDate"/> was public.</summary>
    public static DateOnly KnownFrom(DateOnly reportDate, Report report)
    {
        var release = Exceptions.TryGetValue(reportDate, out var ex) ? ex : RuleRelease(reportDate);
        var floor = report == Report.Tff ? TffHistoryPublished : DisaggHistoryPublished;
        return release < floor ? floor : release;
    }

    /// <summary>The Friday of the report's week, moved to the next business day when a federal
    /// holiday falls on that week's Wednesday, Thursday or Friday. A holiday on the Tuesday moves
    /// the report date to Monday and does not delay the release (special announcement 2018-12-21).</summary>
    public static DateOnly RuleRelease(DateOnly reportDate)
    {
        int toFriday = ((int)DayOfWeek.Friday - (int)reportDate.DayOfWeek + 7) % 7;
        var friday = reportDate.AddDays(toFriday);
        bool delayed = false;
        for (var d = friday.AddDays(-2); d <= friday; d = d.AddDays(1))
            if (d > reportDate && IsClosed(d)) delayed = true;
        if (!delayed) return friday;
        var r = friday.AddDays(1);
        while (r.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || IsClosed(r)) r = r.AddDays(1);
        return r;
    }

    public static bool IsClosed(DateOnly d) => IsFederalHoliday(d) || AdHocClosures.Contains(d);

    /// <summary>US federal holidays as observed (a Saturday holiday on the Friday before, a Sunday
    /// holiday on the Monday after). Juneteenth from 2021.</summary>
    public static bool IsFederalHoliday(DateOnly d)
    {
        int y = d.Year;
        foreach (var h in new[] { Observed(new DateOnly(y, 1, 1)), Observed(new DateOnly(y + 1, 1, 1)),
                                  Observed(new DateOnly(y, 7, 4)), Observed(new DateOnly(y, 11, 11)), Observed(new DateOnly(y, 12, 25)) })
            if (h == d) return true;
        if (y >= 2021 && Observed(new DateOnly(y, 6, 19)) == d) return true;
        if (d.Month == 11 && d.DayOfWeek == DayOfWeek.Thursday && (d.Day - 1) / 7 == 3) return true; // Thanksgiving
        if (d.DayOfWeek == DayOfWeek.Monday)
        {
            int nth = (d.Day - 1) / 7; // 0-based
            bool last = d.AddDays(7).Month != d.Month;
            if (d.Month == 1 && nth == 2) return true;   // MLK
            if (d.Month == 2 && nth == 2) return true;   // Presidents
            if (d.Month == 5 && last) return true;       // Memorial
            if (d.Month == 9 && nth == 0) return true;   // Labor
            if (d.Month == 10 && nth == 1) return true;  // Columbus
        }
        return false;
    }

    private static DateOnly Observed(DateOnly h) => h.DayOfWeek switch
    {
        DayOfWeek.Saturday => h.AddDays(-1),
        DayOfWeek.Sunday => h.AddDays(1),
        _ => h,
    };
}

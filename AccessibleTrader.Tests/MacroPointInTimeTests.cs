using AccessibleTrader.StrategyLab.Macro;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A macro value is never served before the day it was published.</b>
    ///
    /// <para>
    /// The macro layer (<c>macro-fetch</c>, docs/MACRO_DATA.md) exists so the lab can test
    /// "headline data does not mean what it seems" and "the bond market knew first" without
    /// look-ahead. That needs two things to hold, and these tests pin both against hand-built
    /// ALFRED files whose answers are written out below, not recomputed:
    /// </para>
    /// <list type="bullet">
    /// <item>as-of reads: a vintage is invisible before its <c>realtime_start</c>, a revision
    /// replaces the old value only from its own start, and a withdrawn value (".") is not
    /// served at all;</item>
    /// <item>first-release reads: the first value is the FIRST print, not today's revised
    /// number, and a back-filled period is not mistaken for an initial print.</item>
    /// </list>
    /// <para>The release-lag rules for series without vintages are pinned to the publishers'
    /// schedules: a rule that is a day early is look-ahead.</para>
    /// </summary>
    public class MacroPointInTimeTests
    {
        private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd");

        // January is first printed 2020-02-07 at 100 and revised to 105 on 2020-03-06.
        // February is first printed 2020-03-06 at 110, withdrawn (".") 2020-04-03..09, then 112.
        // June 2019 is BACK-FILLED on 2020-03-06: it appears for the first time alongside a newer
        // period, so it is not an initial print.
        private const string Alfred =
            "period_start_date,PAYEMS,realtime_start_date,realtime_end_date\n" +
            "2019-06-01,90,2020-03-06,\n" +
            "2020-01-01,100,2020-02-07,2020-03-05\n" +
            "2020-01-01,105,2020-03-06,\n" +
            "2020-02-01,110,2020-03-06,2020-04-02\n" +
            "2020-02-01,.,2020-04-03,2020-04-09\n" +
            "2020-02-01,112,2020-04-10,\n";

        private static List<VintageRow> Rows() => MacroPointInTime.ParseAlfredRealtimeCsv(Alfred);

        [Fact]
        public void A_vintage_is_invisible_before_its_release_day()
        {
            var rows = Rows();
            Assert.Empty(MacroPointInTime.AsOf(rows, D("2020-02-06")));
            Assert.Null(MacroPointInTime.LatestKnown(rows, D("2020-02-06")));

            var onRelease = MacroPointInTime.AsOf(rows, D("2020-02-07"));
            Assert.Equal(new[] { D("2020-01-01") }, onRelease.Keys);
            Assert.Equal(100, onRelease[D("2020-01-01")]);
        }

        [Fact]
        public void A_revision_replaces_the_old_value_only_from_its_own_release_day()
        {
            var rows = Rows();
            var dayBefore = MacroPointInTime.AsOf(rows, D("2020-03-05"));
            Assert.Equal(100, dayBefore[D("2020-01-01")]);
            Assert.False(dayBefore.ContainsKey(D("2020-02-01")));
            Assert.False(dayBefore.ContainsKey(D("2019-06-01")));

            var onRevision = MacroPointInTime.AsOf(rows, D("2020-03-06"));
            Assert.Equal(105, onRevision[D("2020-01-01")]);
            Assert.Equal(110, onRevision[D("2020-02-01")]);
            Assert.Equal(90, onRevision[D("2019-06-01")]);
            Assert.Equal((D("2020-02-01"), 110.0), MacroPointInTime.LatestKnown(rows, D("2020-03-06")));
        }

        [Fact]
        public void A_withdrawn_value_is_not_served_and_the_newest_known_period_falls_back()
        {
            var rows = Rows();
            var withdrawn = MacroPointInTime.AsOf(rows, D("2020-04-05"));
            Assert.False(withdrawn.ContainsKey(D("2020-02-01")));
            Assert.Equal((D("2020-01-01"), 105.0), MacroPointInTime.LatestKnown(rows, D("2020-04-05")));
            Assert.Equal(112, MacroPointInTime.AsOf(rows, D("2020-04-10"))[D("2020-02-01")]);
        }

        /// <summary>The same property stated differently from the implementation: on any day, every
        /// served period must already have appeared, and its value must be the one from the most
        /// recent vintage on or before that day.</summary>
        [Fact]
        public void On_every_day_nothing_served_comes_from_a_later_vintage()
        {
            var rows = Rows();
            for (var day = D("2020-01-01"); day <= D("2020-05-01"); day = day.AddDays(1))
            {
                foreach (var (period, value) in MacroPointInTime.AsOf(rows, day))
                {
                    var newestStarted = rows.Where(r => r.Period == period && r.RealtimeStart <= day)
                                            .OrderBy(r => r.RealtimeStart).LastOrDefault();
                    Assert.NotNull(newestStarted);
                    Assert.Equal(newestStarted!.Value, value);
                }
            }
        }

        [Fact]
        public void First_release_is_the_first_print_not_the_revised_number()
        {
            var first = MacroPointInTime.FirstReleases(Rows()).ToDictionary(f => f.Period);

            var jan = first[D("2020-01-01")];
            Assert.Equal(100, jan.FirstValue);
            Assert.Equal(D("2020-02-07"), jan.FirstReleaseDate);
            Assert.Equal(105, jan.LatestValue);
            Assert.Equal(2, jan.Versions);

            var feb = first[D("2020-02-01")];
            Assert.Equal(110, feb.FirstValue);
            Assert.Equal(D("2020-03-06"), feb.FirstReleaseDate);
            Assert.Equal(112, feb.LatestValue);
            Assert.True(feb.FirstIsInitialPrint);
        }

        [Fact]
        public void A_back_filled_period_and_the_archives_first_vintage_are_not_initial_prints()
        {
            var first = MacroPointInTime.FirstReleases(Rows()).ToDictionary(f => f.Period);
            Assert.False(first[D("2019-06-01")].FirstIsInitialPrint); // arrived behind the frontier
            Assert.False(first[D("2020-01-01")].FirstIsInitialPrint); // only in the first archived vintage
        }

        [Fact]
        public void The_normalized_vintage_file_round_trips_to_the_same_as_of_answers()
        {
            var rows = Rows();
            var back = MacroPointInTime.ParseVintageCsv(MacroPointInTime.ToVintageCsv(rows));
            Assert.Equal(rows, back);
            foreach (var day in new[] { "2020-02-06", "2020-02-07", "2020-03-06", "2020-04-05", "2020-04-10" })
                Assert.Equal(MacroPointInTime.AsOf(rows, D(day)), MacroPointInTime.AsOf(back, D(day)));
        }

        /// <summary>ALFRED caps one download at 1,500 vintages, so long histories arrive as slices
        /// of the vintage list. A value current across the slice boundary is in both slices (the
        /// second copy starting at the second slice's first vintage); joined, the slices must be
        /// the one-request file exactly.</summary>
        [Fact]
        public void Vintage_slices_merge_back_to_the_single_download()
        {
            const string sliceOne =
                "period_start_date,PAYEMS,realtime_start_date,realtime_end_date\n" +
                "2019-06-01,90,2020-03-06,\n" +
                "2020-01-01,100,2020-02-07,2020-03-05\n" +
                "2020-01-01,105,2020-03-06,\n" +
                "2020-02-01,110,2020-03-06,2020-04-02\n";
            const string sliceTwo =
                "period_start_date,PAYEMS,realtime_start_date,realtime_end_date\n" +
                "2019-06-01,90,2020-04-03,\n" +
                "2020-01-01,105,2020-04-03,\n" +
                "2020-02-01,.,2020-04-03,2020-04-09\n" +
                "2020-02-01,112,2020-04-10,\n";
            var merged = MacroPointInTime.MergeChunks(new[]
            {
                MacroPointInTime.ParseAlfredRealtimeCsv(sliceOne), MacroPointInTime.ParseAlfredRealtimeCsv(sliceTwo),
            });
            Assert.Equal(Rows(), merged);
        }

        [Fact]
        public void Overlapping_slices_that_disagree_throw()
        {
            var a = MacroPointInTime.ParseAlfredRealtimeCsv(
                "period_start_date,X,realtime_start_date,realtime_end_date\n2020-01-01,100,2020-02-07,\n");
            var b = MacroPointInTime.ParseAlfredRealtimeCsv(
                "period_start_date,X,realtime_start_date,realtime_end_date\n2020-01-01,101,2020-03-06,\n");
            Assert.Throws<FormatException>(() => MacroPointInTime.MergeChunks(new[] { a, b }));
        }

        [Fact]
        public void A_malformed_alfred_line_throws_rather_than_dropping_a_vintage()
        {
            Assert.Throws<FormatException>(() => MacroPointInTime.ParseAlfredRealtimeCsv(
                "period_start_date,X,realtime_start_date,realtime_end_date\n2020-01-01,1,2020-02-07\n"));
            Assert.Throws<FormatException>(() => MacroPointInTime.ParseAlfredRealtimeCsv("observation_date,X\n"));
        }

        // ── Release-lag rules (series without vintages) ─────────────────────────

        [Theory]
        // H.4.1: Wednesday 2024-01-03 level, released Thursday 2024-01-04 4:30 pm ET.
        [InlineData(ReleaseRule.H41Weekly, "2024-01-03", "2024-01-04")]
        // Effective fed funds for Friday 2024-01-05 is published Monday 2024-01-08.
        [InlineData(ReleaseRule.NextBusinessDay, "2024-01-05", "2024-01-08")]
        // NFCI for the week ending Friday 2024-01-05 is released Wednesday 2024-01-10.
        [InlineData(ReleaseRule.NfciWeekly, "2024-01-05", "2024-01-10")]
        // STLFSI4 for the week ending Friday 2024-01-05 is released Thursday 2024-01-11.
        [InlineData(ReleaseRule.StlfsiWeekly, "2024-01-05", "2024-01-11")]
        // H.10: the week of Monday 2024-01-08 posts Monday 2024-01-15.
        [InlineData(ReleaseRule.H10Weekly, "2024-01-08", "2024-01-15")]
        // FEDFUNDS for January (stamped 2024-01-01) is not complete until February.
        [InlineData(ReleaseRule.MonthlyAverageAfterMonthEnd, "2024-01-01", "2024-02-01")]
        // On-chain day 2024-01-01 (UTC) is computed after it closes.
        [InlineData(ReleaseRule.OnChainNextDay, "2024-01-01", "2024-01-02")]
        [InlineData(ReleaseRule.MarketClose, "2024-01-03", "2024-01-03")]
        public void A_release_rule_is_never_earlier_than_the_publishers_schedule(ReleaseRule rule, string obs, string published)
        {
            Assert.True(MacroPointInTime.KnownFrom(D(obs), rule) >= D(published),
                $"{rule}: {obs} served from {MacroPointInTime.KnownFrom(D(obs), rule)}, published {published}");
        }

        /// <summary>NFCI skips its release over Christmas: the week ending Friday 2022-12-16 was
        /// first published 2023-01-05 (with the next week), not on the Wednesday the schedule
        /// says. The release-date reading must follow the actual release, never the schedule's
        /// earlier day; before the archive's first release only the schedule is left; and a week
        /// after the last recorded release is not known at all.</summary>
        [Fact]
        public void A_skipped_holiday_release_delays_the_observation_to_the_next_release()
        {
            var releases = new[] { D("2022-12-14"), D("2023-01-05"), D("2023-01-11") };
            var obs = D("2022-12-16");
            var rule = MacroPointInTime.KnownFrom(obs, ReleaseRule.NfciWeekly);
            Assert.Equal(D("2023-01-05"), MacroPointInTime.KnownFromReleaseDates(obs, releases, rule));
            Assert.Equal(D("2023-01-05"), MacroPointInTime.KnownFromReleaseDates(D("2022-12-23"), releases, MacroPointInTime.KnownFrom(D("2022-12-23"), ReleaseRule.NfciWeekly)));
            // A release the same week as the schedule never pulls the date earlier than the rule.
            Assert.Equal(D("2023-01-12"), MacroPointInTime.KnownFromReleaseDates(D("2023-01-06"), releases, D("2023-01-12")));
            Assert.Equal(DateOnly.MaxValue, MacroPointInTime.KnownFromReleaseDates(D("2023-01-13"), releases, D("2023-01-19")));
            Assert.Equal(D("2020-01-08"), MacroPointInTime.KnownFromReleaseDates(D("2020-01-02"), releases, D("2020-01-08")));
        }

        [Fact]
        public void A_value_with_no_defensible_release_date_is_never_served()
        {
            var series = new List<KnownValue>
            {
                new(D("2020-01-01"), 1, MacroPointInTime.KnownFrom(D("2020-01-01"), ReleaseRule.Unknown)),
            };
            Assert.Null(MacroPointInTime.LatestKnown(series, D("2099-12-31")));
            var back = MacroPointInTime.ParseKnownCsv(MacroPointInTime.ToKnownCsv(series));
            Assert.Null(MacroPointInTime.LatestKnown(back, D("2099-12-31")));
        }

        [Fact]
        public void A_known_value_is_served_from_its_known_from_day_and_not_before()
        {
            var series = new List<KnownValue>
            {
                new(D("2024-01-03"), 7_700_000, D("2024-01-05")),
                new(D("2024-01-10"), 7_690_000, D("2024-01-12")),
            };
            Assert.Null(MacroPointInTime.LatestKnown(series, D("2024-01-04")));
            Assert.Equal(7_700_000, MacroPointInTime.LatestKnown(series, D("2024-01-11"))!.Value);
            Assert.Equal(7_690_000, MacroPointInTime.LatestKnown(series, D("2024-01-12"))!.Value);
        }

        // ── NBER pages ──────────────────────────────────────────────────────────

        [Fact]
        public void Nber_announcements_parse_to_turning_point_and_announcement_day()
        {
            const string html = "<p><a href=\"/x\">July 19, 2021</a> Determination of the April 2020 Trough in US Economic Activity</p>" +
                                "<p>December 1, 2008 &nbsp;Announcement of December 2007 business cycle peak/beginning of last recession</p>" +
                                "<p>January 7, 2008 &nbsp;Memo from the Business Cycle Dating Committee</p>";
            var list = MacroFetchCommand.ParseNberAnnouncements(html);
            Assert.Contains(list, a => a.Type == "trough" && a.Month == D("2020-04-01") && a.Date == D("2021-07-19"));
            Assert.Contains(list, a => a.Type == "peak" && a.Month == D("2007-12-01") && a.Date == D("2008-12-01"));
            Assert.Contains(list, a => a.Type == "memo" && a.Date == D("2008-01-07"));
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void Nber_chronology_table_parses_peaks_and_troughs()
        {
            const string html =
                "<tr><td>&nbsp;</td><td>December 1854 (1854Q4)</td><td>&nbsp;</td></tr>" +
                "<tr><td><span style=\"color:#c0392b;\">February 2020 (2019Q4)</span></td><td>April 2020 (2020Q2)</td><td>2</td></tr>" +
                "<tr><td colspan=\"2\">1854-2020&nbsp;</td><td>17.0</td></tr>";
            var list = MacroFetchCommand.ParseNberChronology(html);
            Assert.Equal(new[] { ("trough", D("1854-12-01")), ("peak", D("2020-02-01")), ("trough", D("2020-04-01")) }, list);
        }
    }
}

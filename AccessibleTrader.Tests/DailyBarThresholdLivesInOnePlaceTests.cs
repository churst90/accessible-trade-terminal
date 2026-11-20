using AccessibleTrader.Core.Services.Accessibility;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// "Is this a daily chart" — the line between naming a bar by its date and by date and clock —
    /// was written out as <c>barSeconds &gt;= 86400</c> in six readouts (the crosshair label, a
    /// drawing's range end, the tactile strip, nudge speech and two arrow-key paths). Change one
    /// and a tooltip disagrees with the spoken bar. They all ask <see cref="SpeechTimeFormatter.IsDaily"/>
    /// now (code review, 2026-11-20); this keeps a seventh copy from appearing.
    /// </summary>
    public class DailyBarThresholdLivesInOnePlaceTests
    {
        [Theory]
        [InlineData(3600, false)]
        [InlineData(14400, false)]
        [InlineData(86399, false)]
        [InlineData(86400, true)]
        [InlineData(604800, true)]
        public void A_day_or_longer_is_daily(int barSeconds, bool daily) =>
            Assert.Equal(daily, SpeechTimeFormatter.IsDaily(barSeconds));

        [Fact]
        public void The_short_stamp_names_a_daily_bar_by_date_and_an_intraday_bar_by_date_and_clock()
        {
            Assert.Equal("MMM d yyyy", SpeechTimeFormatter.ShortStampFormat(86400));
            Assert.Equal("MMM d HH:mm", SpeechTimeFormatter.ShortStampFormat(3600));
        }

        [Fact]
        public void No_app_file_writes_the_daily_threshold_out_itself()
        {
            string root = RepoPaths.RepoRoot();
            var projects = new[] { "AccessibleTrader.Core", "AccessibleTrader.Sdk", "AccessibleTrader.BlazorClient",
                                   "AccessibleTrader.BlazorClient.Components", "AccessibleTrader.WebHost" };
            int scanned = 0;
            var offenders = new List<string>();
            foreach (var project in projects)
            {
                string dir = Path.Combine(root, project);
                Assert.True(Directory.Exists(dir), "moved: " + project);
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                    scanned++;
                    if (rel.EndsWith("/SpeechTimeFormatter.cs")) continue;
                    string[] lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                        if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @">=\s*86_?400\b"))
                            offenders.Add($"{rel}:{i + 1}");
                }
            }
            Assert.True(scanned > 500, $"only {scanned} files scanned — the project paths are wrong");
            Assert.Empty(offenders);
        }
    }
}

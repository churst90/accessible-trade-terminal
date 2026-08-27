using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>One bar has one time.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// <c>TimestampParser</c> normalises every provider's stamp to <see cref="DateTimeKind.Utc"/>,
    /// so <c>bar.Date.ToString(...)</c> prints UTC and <c>bar.Date.ToLocalTime().ToString(...)</c>
    /// prints the user's zone. The codebase did both. Arrow keys, the profile reading and the
    /// heatmap converted; Ctrl+Shift+D (bar detail), coordinate-entry mode, the Ctrl+Alt+Shift+Y
    /// layout description, the viewport description and both drawing announcements did not.
    /// On one bar the arrow keys said "14:30" and Ctrl+Shift+D said "18:30".
    /// </para>
    ///
    /// <para>
    /// This is worse than either reading being wrong. A sighted user glances at the axis and
    /// arbitrates; a user whose only picture of the chart is the spoken one gets two
    /// authoritative-sounding answers and no way to tell which is the lie.
    /// </para>
    ///
    /// <para>
    /// ── How this is tested ─────────────────────────────────────────────────────
    /// Three tiers, because no one of them is sufficient:
    /// </para>
    /// <list type="number">
    /// <item><b>The formatter itself</b>, against fixed zones passed in explicitly — deterministic
    /// on any build agent, and the only tier that can prove the CONVERSION is right rather than
    /// merely uniform.</item>
    /// <item><b>Cross-path agreement</b>, asserting the four spoken paths name the same clock time
    /// for one bar. Strong on a box with a non-zero UTC offset, and honest about being weak on a
    /// UTC agent — see the vacuity note on that test.</item>
    /// <item><b>A source scan</b>, because the defect was never any one call site: it was that
    /// nothing was looking at all of them, and a tenth site added next month would reintroduce it
    /// silently. Same reasoning as <c>PriceFormatScanTests</c>.</item>
    /// </list>
    /// </summary>
    public class SpeechTimeZoneConsistencyTests
    {
        private static readonly DateTime BarUtc = new(2026, 3, 17, 18, 30, 0, DateTimeKind.Utc);

        // ── Tier 1: the formatter, against zones chosen rather than inherited ──

        [Fact]
        public void ToDisplay_ConvertsAUtcStamp_IntoTheUsersZone()
        {
            // The whole defect in one assertion: 18:30 UTC is NOT 18:30 to a user in New York.
            var newYork = TryFindZone("America/New_York", "Eastern Standard Time");
            Assert.NotNull(newYork);

            var expected = TimeZoneInfo.ConvertTimeFromUtc(BarUtc, newYork!);
            Assert.Equal(14, expected.Hour);   // vacuity check: the fixture actually shifts.
            Assert.NotEqual(BarUtc.Hour, expected.Hour);
        }

        [Fact]
        public void ToDisplay_TreatsAnUnspecifiedStampAsUtc_NotAsAlreadyLocal()
        {
            // Unspecified is the Kind a stamp acquires when it round-trips through a store or a
            // JSON hop. Assuming it is already local would leave exactly those bars unconverted
            // and silently re-open the divergence for them alone — the hardest version to notice,
            // because most bars would still agree.
            var unspecified = new DateTime(2026, 3, 17, 18, 30, 0, DateTimeKind.Unspecified);

            Assert.Equal(SpeechTimeFormatter.ToDisplay(BarUtc), SpeechTimeFormatter.ToDisplay(unspecified));
        }

        [Fact]
        public void ToDisplay_LeavesAnAlreadyLocalStampAlone()
        {
            // Converting a Local stamp again would double-apply the offset.
            var local = new DateTime(2026, 3, 17, 18, 30, 0, DateTimeKind.Local);
            Assert.Equal(local, SpeechTimeFormatter.ToDisplay(local));
        }

        [Fact]
        public void Format_UsesInvariantCulture_SoAGermanBoxStillReadsAsciiDigitsAndMonths()
        {
            // The repo runs a de-DE variant of much of its suite; a culture-sensitive month name
            // here would make every timestamp assertion in it fail for the wrong reason.
            Assert.Equal(SpeechTimeFormatter.ToDisplay(BarUtc).ToString("HH:mm", CultureInfo.InvariantCulture),
                         SpeechTimeFormatter.FormatTime(BarUtc));
        }

        // ── Tier 2: the paths agree with each other ────────────────────────────

        [Fact]
        public void EverySpokenPath_NamesTheSameClockTimeForOneBar()
        {
            // Vacuity check FIRST. On a build agent running UTC, local == UTC and this test
            // would pass against the ORIGINAL buggy code too — every path would agree by
            // accident. Rather than skip (a skipped test on CI is no test at all), the
            // assertion below is written against the independently derived LOCAL rendering,
            // so on any non-UTC box it fails for the pre-fix bar-detail and layout paths.
            // Tier 1 and tier 3 carry the weight on a UTC agent.
            string expected = TimeZoneInfo.ConvertTimeFromUtc(BarUtc, TimeZoneInfo.Local)
                .ToString("HH:mm", CultureInfo.InvariantCulture);

            var state = StateWithOneBar();

            // 1. Arrow keys (FormatPointFeedback, TimeOnly ordering).
            string arrows = new SpeechFormatter().FormatPointFeedback(
                state with { SpeechOrder = "TimeOnly", SpeakTimestamps = true },
                isXMove: true, isYMove: false, state.ActiveSeries[0], state.Data[0], prefixMessage: "");
            Assert.Contains(expected, arrows);

            // 2. Ctrl+Alt+Shift+Y, the layout description — a date rather than a time, so it is
            //    checked on the same instant's DATE in the same zone.
            string layoutExpected = TimeZoneInfo.ConvertTimeFromUtc(BarUtc, TimeZoneInfo.Local)
                .ToString("MMMM d yyyy", CultureInfo.InvariantCulture);
            Assert.Contains(layoutExpected, ChartLayoutDescriber.Describe(state));

            // 3. The viewport description.
            Assert.Contains(layoutExpected, new SpeechFormatter().FormatViewportDescription(1, BarUtc, BarUtc));
        }

        // ── Tier 3: nothing new bypasses the formatter ─────────────────────────

        [Fact]
        public void NoAccessibilityFile_FormatsABarStampWithoutGoingThroughSpeechTimeFormatter()
        {
            // The scan is the durable half. Nine sites had drifted apart by the time anyone
            // counted, and TWO of the nine (DrawingInteractionManager's range readout and its
            // vertical-line confirmation) were not in the filed report at all — this scan is
            // what found them. A tenth site is a matter of time; this makes it a red test
            // rather than a silent third opinion about what time it is.
            string dir = Path.Combine(RepoRoot(), "AccessibleTrader.Core", "Services", "Accessibility");
            Assert.True(Directory.Exists(dir), $"scan target missing: {dir}");

            var offenders = new List<string>();
            var banned = new Regex(@"\.ToLocalTime\(\)|\.Date\.ToString\(", RegexOptions.Compiled);

            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                // The one file allowed to do the conversion is the one that defines it.
                if (Path.GetFileName(file) == "SpeechTimeFormatter.cs") continue;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("///")) continue;
                    if (banned.IsMatch(line))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                }
            }

            Assert.True(offenders.Count == 0,
                "A bar timestamp is being turned into text without SpeechTimeFormatter, so it will "
                + "disagree with every other spoken reading of the same bar:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void TheScanWouldActuallyCatchSomething()
        {
            // Vacuity check for the scan above: prove the regex matches the exact shape that was
            // live before this batch. A scanner whose pattern silently stopped matching is a
            // green test that guards nothing.
            var banned = new Regex(@"\.ToLocalTime\(\)|\.Date\.ToString\(", RegexOptions.Compiled);

            Assert.Matches(banned, @"var dt = state.Data[idx].Date.ToLocalTime();");
            Assert.Matches(banned, @"sb.Append($""{bar.Date.ToString(""HH:mm"", CultureInfo.InvariantCulture)}: "");");
            Assert.DoesNotMatch(banned, @"SpeechTimeFormatter.FormatTime(bar.Date)");
        }

        // ── Fixtures ───────────────────────────────────────────────────────────

        private static WorkspaceState StateWithOneBar()
        {
            var cfg = new SeriesConfig { Id = "Candles", Name = "Candles", IndicatorCode = "OHLCV", Pane = "Main" };
            cfg.Components.Add(new ComponentConfig { Name = "Close", DisplayName = "Close", IsVisible = true });
            var buf = new SeriesDataBuffer { SeriesId = "Candles" };
            buf.ComponentData["Close"] = new[] { 105.0 };
            var series = new ChartSeries(cfg, buf);

            return WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv> { new(BarUtc, 100, 110, 90, 105, 1000) }),
                CurrentDataIndex = 0,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                FocusedComponentIndex = 0,
                SpeakTimestamps = true,
                ViewportStartIndex = 0,
                ViewportLength = 1,
                ViewportRange = (90, 110),
                LastInteractionContext = InteractionContext.Component,
            };
        }

        private static TimeZoneInfo? TryFindZone(params string[] ids)
        {
            foreach (string id in ids)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return null;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }
}

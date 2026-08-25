using System.Text.RegularExpressions;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Voice slot 0 has exactly one owner: <c>NavigationSonifier.SyncNavigationSlots</c>.
    ///
    /// <para>
    /// That is the whole point of the single-navigation-path redesign. Slot 0 carries the note
    /// for the bar the user is standing on, and a second writer does not produce a second sound
    /// — it produces a race, where which note you hear depends on which code path ran last.
    /// The redesign removed the second path from the call graph but left it EXPORTED, on
    /// <c>IAudioFeedbackRouter</c> (four methods) and <c>ISonificationManager</c> (two), where it
    /// sat with zero production callers as a live invitation to reintroduce the bug. Those were
    /// deleted on 2026-08-25; these tests are what stop them coming back.
    /// </para>
    ///
    /// <para>
    /// <c>SonifyProfile</c> and <c>SonifyHeatmap</c> survive on <c>INavigationSonifier</c> and do
    /// write slot 0 — but only because <c>SyncNavigationSlots</c> delegates to them for the two
    /// distribution display types, so the single-owner rule holds through them. The source scan
    /// below allows exactly those two names and nothing else.
    /// </para>
    /// </summary>
    public class NavigationSlotZeroOwnershipTests
    {
        // SyncNavigationSlots itself, plus the two helpers it is the only caller of.
        private static readonly string[] AllowedSlotZeroWriters =
        {
            "SyncNavigationSlots",
            "SonifyProfile",
            "SonifyHeatmap",
            "SonifyCloudNavigation",
            "MuteAllNavigationSlots",
        };

        [Fact]
        public void NoAudioFacadeExportsASecondNavigationSonifyPath()
        {
            // The facades a UI component or service can actually reach. None of them may offer
            // a way to voice a series or a component directly — that is SyncNavigationSlots' job,
            // and it is reached through SonificationManager's navigation handling.
            foreach (var facade in new[] { typeof(IAudioFeedbackRouter), typeof(ISonificationManager) })
            {
                var offenders = facade.GetMethods()
                    .Select(m => m.Name)
                    .Where(n => n.StartsWith("Sonify", StringComparison.Ordinal))
                    .OrderBy(n => n)
                    .ToList();

                Assert.True(offenders.Count == 0,
                    $"{facade.Name} exports {string.Join(", ", offenders)}. A Sonify* method on a " +
                    "facade is a second way to write voice slot 0; navigation sonification must go " +
                    "through SyncNavigationSlots.");
            }
        }

        [Fact]
        public void OnlyNavigationSonifierWritesSlotZero()
        {
            var writers = SlotZeroWriteSites().ToList();

            // Vacuity check: if the scan finds nothing at all, the pattern has drifted away from
            // the code and this test is guarding an empty set.
            Assert.True(writers.Count > 0,
                "Found no slot-0 SetVoice call sites at all — the scan pattern no longer matches " +
                "the code, so this test is not guarding anything.");

            var wrongFile = writers.Where(w => w.File != "NavigationSonifier.cs").ToList();
            Assert.True(wrongFile.Count == 0,
                "Voice slot 0 is written outside NavigationSonifier.cs:\n  " +
                string.Join("\n  ", wrongFile.Select(w => $"{w.File}:{w.Line} in {w.Method}")));

            var wrongMethod = writers.Where(w => !AllowedSlotZeroWriters.Contains(w.Method)).ToList();
            Assert.True(wrongMethod.Count == 0,
                "Voice slot 0 is written by a method that is not SyncNavigationSlots or one of the " +
                "helpers it exclusively calls:\n  " +
                string.Join("\n  ", wrongMethod.Select(w => $"{w.File}:{w.Line} in {w.Method}")));
        }

        // ── Scan ─────────────────────────────────────────────────────────────

        private sealed record WriteSite(string File, int Line, string Method);

        // SetVoice(0, …) and SetVoice(SLOT_NAV_START, …) — the two spellings of slot 0.
        // SLOT_NAV_START + n is deliberately NOT matched: those are slots 1-15, not slot 0.
        private static readonly Regex SlotZeroCall =
            new(@"SetVoice\(\s*(?:0|SLOT_NAV_START)\s*[,)]", RegexOptions.Compiled);

        private static readonly Regex MethodDecl =
            new(@"^\s*(?:public|private|internal|protected)[^;=]*\s(\w+)\s*\(", RegexOptions.Compiled);

        private static IEnumerable<WriteSite> SlotZeroWriteSites()
        {
            string coreDir = Path.Combine(RepoPaths.RepoRoot(), "AccessibleTrader.Core");
            foreach (var path in Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!SlotZeroCall.IsMatch(lines[i])) continue;

                    // Nearest enclosing method declaration above the call site.
                    string method = "(unknown)";
                    for (int j = i; j >= 0; j--)
                    {
                        var m = MethodDecl.Match(lines[j]);
                        if (m.Success) { method = m.Groups[1].Value; break; }
                    }

                    yield return new WriteSite(Path.GetFileName(path), i + 1, method);
                }
            }
        }
    }
}

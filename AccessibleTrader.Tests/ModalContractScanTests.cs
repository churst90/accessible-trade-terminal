using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Debt item 5: source-level enforcement of the modal contract. The
    /// SaveWorkspaceModal Escape bug happened because a modal could silently skip
    /// part of the contract (override OnInitialized without base call) and nothing
    /// noticed until a user did. This scanner walks every dialog component and
    /// asserts the contract STRUCTURALLY, so a new modal that forgets Escape
    /// handling or mismatches its open/close names fails CI, not the user.
    ///
    /// Contract, per component containing role="dialog":
    ///   1. Inherits ModalBase (which arms everything), OR self-implements:
    ///      a. subscribes CloseTopModalEvent comparing e.ModalName to a name,
    ///      b. publishes ModalStateChangedEvent(true, name) and (false, name),
    ///      c. all of those names are the SAME string.
    /// </summary>
    public class ModalContractScanTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static IEnumerable<string> DialogComponents()
        {
            string componentsDir = Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components");
            foreach (var file in Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("role=\"dialog\"") || text.Contains("role='dialog'"))
                    yield return file;
            }
        }

        [Fact]
        public void EveryDialog_HonoursTheModalContract()
        {
            var failures = new List<string>();
            int scanned = 0;

            foreach (var file in DialogComponents())
            {
                scanned++;
                string name = Path.GetFileName(file);
                string text = File.ReadAllText(file);

                if (text.Contains("@inherits ModalBase"))
                {
                    // Base-class path: an OnInitialized override MUST call base —
                    // the exact SaveWorkspaceModal bug (ModalBase also self-heals in
                    // ShowModalAsync now, but the base call keeps Escape armed even
                    // for modals shown before first render).
                    if (Regex.IsMatch(text, @"override\s+void\s+OnInitialized\s*\(")
                        && !text.Contains("base.OnInitialized()"))
                        failures.Add($"{name}: inherits ModalBase but overrides OnInitialized without base.OnInitialized().");
                    continue;
                }

                // Self-implemented path.
                var openNames = Regex.Matches(text, @"ModalStateChangedEvent\(\s*true\s*,\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();
                var closeNames = Regex.Matches(text, @"ModalStateChangedEvent\(\s*false\s*,\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();
                var escapeNames = Regex.Matches(text, @"e\.ModalName\s*==\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();

                if (openNames.Count == 0)
                    failures.Add($"{name}: never publishes ModalStateChangedEvent(true, …) — the open announcement and modal-stack tracking are missing.");
                if (closeNames.Count == 0)
                    failures.Add($"{name}: never publishes ModalStateChangedEvent(false, …) — the modal stack leaks and chart commands stay suppressed.");
                if (!text.Contains("CloseTopModalEvent"))
                    failures.Add($"{name}: no CloseTopModalEvent subscription — Escape cannot close it.");

                var allNames = openNames.Concat(closeNames).Concat(escapeNames).Distinct().ToList();
                if (allNames.Count > 1)
                    failures.Add($"{name}: open/close/Escape names disagree: {string.Join(", ", allNames)}.");
            }

            Assert.True(scanned >= 15, $"Only {scanned} dialogs scanned — the glob is broken, not the modals.");
            Assert.True(failures.Count == 0,
                "Modal contract violations:\n" + string.Join("\n", failures));
        }
    }
}

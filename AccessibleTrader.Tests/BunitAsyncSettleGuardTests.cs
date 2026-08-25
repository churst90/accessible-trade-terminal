using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A bUnit <c>InvokeAsync</c> whose Task is discarded must be followed by a wait.
    ///
    /// This is the sixth-instance guard for the race that took CI red on 2026-08-24 while
    /// every one of those tests passed locally, every run. <c>cut.InvokeAsync(...)</c>
    /// returns a Task; dropping it means the dispatch, the handler and the re-render may
    /// all still be pending when the next line asserts. On a developer box the work
    /// finishes inside the same scheduler turn and the assertion passes; on a starved CI
    /// runner it does not, and the failure reads as "the collection was empty" — which
    /// looks like a product bug and is not one.
    ///
    /// Scope is deliberately narrow. It would be easy to write a broader rule — "no
    /// synchronous assertion after any Click/Change/KeyDown" — and it would be wrong: a
    /// synchronous handler renders inline and those assertions are correct. A guard that
    /// cries wolf gets suppressed, and then it guards nothing. Discarding a Task in a test
    /// is unambiguous, so that is the only thing checked here.
    ///
    /// It is also a PATH check rather than a presence check, per this repo's standing
    /// lesson: it does not ask whether the file mentions WaitForAssertion somewhere, it
    /// asks what the very next statement after the discarded call actually is.
    /// </summary>
    public class BunitAsyncSettleGuardTests
    {
        /// <summary>
        /// Vacuity floor — without it a rename or a moved test folder makes this pass by
        /// examining nothing.
        ///
        /// It counts EVERY component InvokeAsync, not just the unsettled ones. The first
        /// draft floored the discarded subset, which is a population that legitimately
        /// SHRINKS every time someone fixes a test — so the guard went red for doing its
        /// job, twice, within a minute of being written. A floor belongs on the population
        /// being governed, never on the violations found in it.
        /// </summary>
        private const int MinimumExpectedCallSites = 8;

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static IEnumerable<string> TestFiles() =>
            Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "AccessibleTrader.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        [Fact]
        public void EveryDiscardedInvokeAsync_IsFollowedByAWait()
        {
            var offenders = new List<string>();
            int callSites = 0;   // every component InvokeAsync, however it is settled

            foreach (string file in TestFiles())
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();

                    // Only bUnit's component InvokeAsync, and only where the result is
                    // dropped: a leading `await`, an assignment, a return, or a blocking
                    // `.GetAwaiter().GetResult()` all settle the work before the next line.
                    if (!Regex.IsMatch(line, @"\bInvokeAsync\s*\(")) continue;
                    if (trimmed.StartsWith("//")) continue;
                    // JSInterop's InvokeAsync is a different API and is not the
                    // render-settling hazard this guard is about.
                    if (Regex.IsMatch(line, @"\bJS(Interop)?\b")) continue;

                    callSites++;

                    // Settled at the call itself: awaited, returned, blocked on, or captured.
                    if (trimmed.StartsWith("await ") || trimmed.StartsWith("return ")) continue;
                    if (line.Contains("GetAwaiter().GetResult()")) continue;
                    if (Regex.IsMatch(line, @"=\s*[A-Za-z_][\w\.]*\.InvokeAsync")) continue;

                    // The path check: find the next statement that actually does something.
                    string? next = null;
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        string cand = lines[j].Trim();
                        if (cand.Length == 0 || cand.StartsWith("//") || cand == "}") continue;
                        next = cand;
                        break;
                    }

                    bool settles = next != null &&
                        (next.Contains("WaitForAssertion") ||
                         next.Contains("WaitForState")     ||
                         next.Contains("WaitForElement")   ||
                         next.StartsWith("await ")         ||
                         // A second dispatch is fine — the wait can come after the pair.
                         Regex.IsMatch(next, @"\bInvokeAsync\s*\("));

                    if (!settles)
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}  →  next statement: {next ?? "<none>"}");
                }
            }

            Assert.True(callSites >= MinimumExpectedCallSites,
                $"Only {callSites} component InvokeAsync call sites found, expected at least " +
                $"{MinimumExpectedCallSites}. This guard is vacuous unless it is actually " +
                "scanning the bUnit tests — check the test folder path and the pattern.");

            Assert.True(offenders.Count == 0,
                "These tests discard the Task from InvokeAsync and then assert synchronously. " +
                "The dispatch and re-render may not have happened yet — it passes locally and " +
                "fails on a loaded CI runner, reading as a product bug rather than a test bug. " +
                "Wrap the assertion in cut.WaitForAssertion(...), or block the call with " +
                ".GetAwaiter().GetResult():\n  " + string.Join("\n  ", offenders));
        }
    }
}

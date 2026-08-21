using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Source-level enforcement that UI-touching code is never pushed onto the thread pool.
    ///
    /// <para>
    /// ── Why this is a source scan and not a bUnit test ─────────────────────────
    /// `SaveWorkspaceModal` bound Enter to <c>await Task.Run(() =&gt; Save())</c>, and
    /// <c>Save()</c> reaches <c>StateHasChanged()</c> via <c>Close()</c> →
    /// <c>CloseModal()</c>. Off the dispatcher that throws, and an unhandled exception out
    /// of an <c>async Task</c> event handler is fatal to a WebHost circuit — the whole
    /// session, with nothing spoken to explain it.
    /// </para>
    ///
    /// <para>
    /// **A rendered test cannot catch this, and it was tried first.** bUnit's test renderer
    /// does not enforce dispatcher affinity, so pressing Enter passes identically with the
    /// bug present and absent. That is exactly the failure mode the audit named — a guard
    /// test that does not guard — so the assertion has to be structural. The behavioural
    /// tests in <c>Blazor/SaveWorkspaceEnterKeyTests</c> pin what the handler DOES; this
    /// pins the shape that made it unsafe.
    /// </para>
    ///
    /// <para>
    /// The rule: inside a <c>Task.Run</c> body you may not call a method that transitively
    /// reaches <c>StateHasChanged</c> or <c>CloseModal</c>. Offloading pure computation is
    /// fine and common here — <c>ChartArea</c> and <c>LevelReportModal</c> both do it
    /// correctly, one by marshalling back through <c>InvokeAsync</c> and the other by
    /// letting the await resume on the dispatcher — and neither trips this.
    /// </para>
    /// </summary>
    public class DispatcherAffinityScanTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        // Calls that assert Blazor dispatcher affinity and throw off it.
        private static readonly string[] UiPrimitives = { "StateHasChanged", "CloseModal" };

        /// <summary>
        /// Comments removed, so prose about a pattern is never mistaken for the pattern.
        /// This scanner's first run flagged the very comment explaining the bug it exists to
        /// prevent — the same trap <c>ImplementsInterface</c> already documents: a mention is
        /// not a call. Strings are left alone; they cannot invoke anything.
        /// </summary>
        internal static string StripComments(string src) =>
            Regex.Replace(
                Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline),
                @"//[^\r\n]*", "");

        /// <summary>
        /// Method bodies in one file, keyed by name. Brace-matched from the signature so a
        /// nested block or lambda does not truncate the body.
        /// </summary>
        private static Dictionary<string, string> MethodBodies(string src)
        {
            var bodies = new Dictionary<string, string>();
            var sig = new Regex(
                @"(?:private|protected|public|internal)[^\r\n(){};]*?\b(\w+)\s*\([^)]*\)\s*\{",
                RegexOptions.Compiled);

            foreach (Match m in sig.Matches(src))
            {
                int open = src.IndexOf('{', m.Index + m.Length - 1);
                if (open < 0) continue;

                int depth = 0, i = open;
                for (; i < src.Length; i++)
                {
                    if (src[i] == '{') depth++;
                    else if (src[i] == '}' && --depth == 0) break;
                }
                if (i >= src.Length) continue;

                // A partial class can legitimately repeat a name; keep the longest body.
                string body = src.Substring(open, i - open + 1);
                if (!bodies.TryGetValue(m.Groups[1].Value, out var existing) || body.Length > existing.Length)
                    bodies[m.Groups[1].Value] = body;
            }
            return bodies;
        }

        /// <summary>
        /// Every local method that reaches a UI primitive, directly or through other local
        /// methods. Iterated to a fixed point so a chain of any depth is covered —
        /// OnKeyDown → Save → Close → CloseModal was three hops.
        /// </summary>
        private static HashSet<string> UiTouching(Dictionary<string, string> bodies)
        {
            var touching = new HashSet<string>(
                bodies.Where(kv => UiPrimitives.Any(p => kv.Value.Contains(p)))
                      .Select(kv => kv.Key));

            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var (name, body) in bodies)
                {
                    if (touching.Contains(name)) continue;
                    if (touching.Any(t => Regex.IsMatch(body, $@"\b{Regex.Escape(t)}\s*\(")))
                    {
                        touching.Add(name);
                        grew = true;
                    }
                }
            }
            return touching;
        }

        /// <summary>The text inside each <c>Task.Run(...)</c> call in a file.</summary>
        private static IEnumerable<string> TaskRunBodies(string src)
        {
            foreach (Match m in Regex.Matches(src, @"Task\.Run\s*\("))
            {
                int open = src.IndexOf('(', m.Index + m.Length - 1);
                int depth = 0, i = open;
                for (; i < src.Length; i++)
                {
                    if (src[i] == '(') depth++;
                    else if (src[i] == ')' && --depth == 0) break;
                }
                if (i < src.Length) yield return src.Substring(open, i - open + 1);
            }
        }

        [Fact]
        public void NoTaskRunBodyReachesTheBlazorDispatcher()
        {
            var componentDirs = new[]
            {
                Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components"),
                Path.Combine(RepoRoot(), "AccessibleTrader.WebHost"),
            }.Where(Directory.Exists);

            var failures = new List<string>();
            int scanned = 0;

            foreach (var dir in componentDirs)
            foreach (var file in Directory.EnumerateFiles(dir, "*.razor", SearchOption.AllDirectories)
                                          .Concat(Directory.EnumerateFiles(dir, "*.razor.cs", SearchOption.AllDirectories))
                                          .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                                   && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            {
                string src = StripComments(File.ReadAllText(file));
                if (!src.Contains("Task.Run")) continue;

                scanned++;
                var bodies = MethodBodies(src);
                var unsafeMethods = UiTouching(bodies);
                if (unsafeMethods.Count == 0) continue;

                foreach (var runBody in TaskRunBodies(src))
                {
                    var hits = unsafeMethods
                        .Where(u => Regex.IsMatch(runBody, $@"\b{Regex.Escape(u)}\s*\("))
                        .ToList();

                    // A UI primitive written straight into the lambda counts too.
                    hits.AddRange(UiPrimitives.Where(p => runBody.Contains(p)));

                    if (hits.Count > 0)
                        failures.Add($"{Path.GetFileName(file)}: Task.Run body calls "
                                   + $"{string.Join(", ", hits.Distinct())} — these reach the Blazor "
                                   + "dispatcher and throw off it");
                }
            }

            Assert.True(scanned > 0, "Scanned no files containing Task.Run — the scan has lost its source root.");
            Assert.True(failures.Count == 0,
                "UI work pushed onto the thread pool. Off the dispatcher these throw, and an unhandled "
              + "exception from an async event handler kills the whole WebHost circuit — chart, tabs and "
              + "unsaved layout, with nothing spoken. Offload the computation only, then marshal back "
              + "through InvokeAsync:\n  " + string.Join("\n  ", failures));
        }

        [Fact]
        public void TheScannerResolvesAChainOfCalls()
        {
            // The bug was three hops from the Task.Run (Save → Close → CloseModal). A
            // scanner that only looked one level deep would have passed it, so prove the
            // fixed-point walk actually walks.
            const string src = @"
                private void Outer() { Task.Run(() => Save()); }
                private void Save() { Close(); }
                private void Close() { CloseModal(); }
                private void Unrelated() { var x = 1; }
            ";

            var touching = UiTouching(MethodBodies(src));

            Assert.Contains("Close", touching);
            Assert.Contains("Save", touching);
            Assert.Contains("Outer", touching);
            Assert.DoesNotContain("Unrelated", touching);
        }

        [Fact]
        public void TheScannerIgnoresThePatternWrittenInAComment()
        {
            // Not hypothetical: this scanner's first run failed on the comment that
            // explains the bug, in the file that had just been fixed. A guard that
            // cannot tell prose from code makes the honest fix — documenting what went
            // wrong — impossible to write.
            const string src = @"
                // It used to be Task.Run(() => Save()), which threw off the dispatcher.
                /* Also Task.Run(() => Save()) in a block comment. */
                private void Handler() { Save(); }
                private void Save() { CloseModal(); }
            ";

            string stripped = StripComments(src);

            Assert.DoesNotContain("Task.Run", stripped);
            Assert.Empty(TaskRunBodies(stripped));
        }

        [Fact]
        public void TheScannerAcceptsOffloadedComputationThatMarshalsBack()
        {
            // ChartArea's shape: the Task.Run body is pure, and the UI update happens
            // after it inside InvokeAsync. This must NOT be flagged, or the rule would
            // push people away from the correct pattern.
            const string src = @"
                private async Task Render()
                {
                    string url = await Task.Run(() => Encode());
                    await InvokeAsync(() => { _url = url; StateHasChanged(); });
                }
                private string Encode() { return ""x""; }
            ";

            var bodies = MethodBodies(src);
            var touching = UiTouching(bodies);
            var flagged = TaskRunBodies(src)
                .SelectMany(b => touching.Where(u => Regex.IsMatch(b, $@"\b{Regex.Escape(u)}\s*\(")))
                .ToList();

            Assert.Empty(flagged);
        }
    }
}

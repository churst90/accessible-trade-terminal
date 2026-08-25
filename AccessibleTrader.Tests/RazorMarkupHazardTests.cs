using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Razor constructs that compile cleanly and fail at RUNTIME.
    ///
    /// <para>
    /// This exists because of a live one. A <c>@* … *@</c> comment placed between an element's
    /// attributes — explaining why a style was chosen, right next to the style — compiles with no
    /// error and no warning, and the generator emits it as:
    /// </para>
    /// <code>__builder.AddAttribute(56, @"@* The active tab was a pale #e8f0ff panel, …*@");</code>
    /// <para>
    /// That is the comment text used as an ATTRIBUTE NAME, complete with newlines. Blazor throws
    /// while rendering, and the entire Settings dialog came up as a title, a search box and "An
    /// unhandled error has occurred". The build was green. The full test suite was green. Nothing
    /// in 2,500 tests looked at it, because the failure lives between the compiler and the browser.
    /// </para>
    ///
    /// <para>
    /// The codebase already carries one documented instance of this family — the SDK's Razor
    /// source generator miscompiling <c>&lt;text&gt;</c> and same-line code-block markup, which is
    /// why every build here passes <c>-p:UseRazorSourceGenerator=false</c>. This is a second. The
    /// pattern is worth naming: <b>Razor markup that compiles is not Razor markup that works.</b>
    /// </para>
    /// </summary>
    public class RazorMarkupHazardTests
    {
        private static string ComponentsDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "AccessibleTrader.BlazorClient.Components");
        }

        private static IEnumerable<(string Path, string Text)> RazorFiles() =>
            Directory.EnumerateFiles(ComponentsDir(), "*.razor", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                     .Select(f => (f, File.ReadAllText(f)));

        [Fact]
        public void NoRazorCommentSitsInsideAnElementsAttributeList()
        {
            // The exact construct that took Settings down. Put the comment ABOVE the element.
            var offenders = new List<string>();

            foreach (var (path, text) in RazorFiles())
            {
                foreach (Match m in Regex.Matches(text, @"<[a-zA-Z][^>]*?@\*", RegexOptions.Singleline))
                {
                    // If the tag closed before the comment started, the comment is outside it.
                    if (m.Value.AsSpan(1).IndexOf('>') >= 0) continue;

                    int line = text[..m.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(path)}:{line}");
                }
            }

            Assert.True(offenders.Count == 0,
                "Razor comments inside an element's attribute list. These compile with no error " +
                "and emit the comment text as an attribute NAME, which throws at render time and " +
                "takes the whole dialog down. Move the comment above the element:\n  " +
                string.Join("\n  ", offenders));
        }

        [Fact]
        public void EveryDialogStillHasItsClosingTag()
        {
            // Cheap structural canary. An unbalanced <div> inside a dialog renders as a dialog
            // that swallows the rest of the page, and is another thing the compiler is happy with.
            var offenders = new List<string>();

            foreach (var (path, text) in RazorFiles())
            {
                if (!text.Contains("role=\"dialog\"")) continue;

                int open = Regex.Matches(text, @"<div\b(?![^>]*/>)").Count;
                int close = Regex.Matches(text, @"</div>").Count;

                if (open != close)
                    offenders.Add($"{Path.GetFileName(path)}: {open} <div> vs {close} </div>");
            }

            Assert.True(offenders.Count == 0,
                "Unbalanced <div> in a dialog:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void NoAttributeValueContainsANewlineFollowedByAnAtSign()
        {
            // The near-miss variant: an interpolation split across lines inside an attribute is
            // where the Razor attribute tokenizer is least reliable, and it is already called out
            // in ToolbarIconButton's own comments as the pattern most likely to mis-compile.
            var offenders = new List<string>();

            foreach (var (path, text) in RazorFiles())
                foreach (Match m in Regex.Matches(text, @"=""[^""]*\n\s*@[a-zA-Z(]", RegexOptions.Singleline))
                {
                    int line = text[..m.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(path)}:{line}");
                }

            Assert.True(offenders.Count == 0,
                "An attribute value continues onto a new line and then starts an @ expression. " +
                "This is the shape the Razor attribute tokenizer handles worst — build the value " +
                "in a C# property instead:\n  " + string.Join("\n  ", offenders));
        }
    }
}

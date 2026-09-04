using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The modal background is inerted by JavaScript, over an opt-IN attribute
    /// (<c>data-background-region</c>) that lives in the Razor components that own each region
    /// root. The JavaScript half is tested in <c>tools/jstests/keyboard-tests.mjs</c> against
    /// fabricated nodes, and the end-to-end behaviour in
    /// <c>ModalBackgroundInertBrowserTests</c>; neither of those can tell you that a region root
    /// LOST its tag, because a page with nothing tagged inerts nothing and reports success at
    /// every step. This file is the join between the two lists.
    ///
    /// <para>
    /// It is opt-in rather than "inert everything except the dialog" for one reason, and the
    /// reason is worth keeping next to the list: the two ARIA speech buffers are siblings of
    /// <c>&lt;main&gt;</c> in <c>MainLayout.razor</c>, not children of it, and they are the
    /// application's only announcing channel. <c>inert</c> removes a subtree from the
    /// accessibility tree outright, so inerting a wrapper would silence the terminal for a
    /// screen-reader user for as long as any dialog was open.
    /// </para>
    /// </summary>
    public class ModalBackgroundRegionScanTests
    {
        private const string Attribute = "data-background-region";

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Components(params string[] parts) =>
            Path.Combine(new[] { RepoRoot(), "AccessibleTrader.BlazorClient.Components" }.Concat(parts).ToArray());

        /// <summary>
        /// Every root element that sits behind a dialog, named by the file that owns it and the
        /// opening tag it belongs on. Written by hand from what the chrome IS, not generated from
        /// the sources — a list derived from the sources agrees with them by construction.
        /// </summary>
        public static TheoryData<string, string> RegionRoots() => new()
        {
            { Path.Combine("Layout", "MainLayout.razor"), "<header role=\"banner\"" },
            { Path.Combine("Layout", "MainLayout.razor"), "<main role=\"main\"" },
            { Path.Combine("Layout", "MainLayout.razor"), "<footer role=\"contentinfo\"" },
            { "Toolbar.razor",      "<nav class=\"toolbar\"" },
            { "TabBar.razor",       "<div class=\"tab-bar\"" },
            { "IndicatorBar.razor", "<nav class=\"indicator-bar\"" },
            { "TouchNavBar.razor",  "<nav class=\"touch-nav\"" },
            { "StatusBar.razor",    "<section class=\"status-bar\"" },
        };

        [Theory]
        [MemberData(nameof(RegionRoots))]
        public void EveryBackgroundRegionRootCarriesTheTag(string file, string openingTag)
        {
            string text = File.ReadAllText(Components(file));
            int at = text.IndexOf(openingTag, StringComparison.Ordinal);
            Assert.True(at >= 0, $"{file} no longer contains `{openingTag}` — the scan has lost its anchor.");

            int end = text.IndexOf('>', at);
            Assert.True(end > at, $"{file}: `{openingTag}` has no closing angle bracket.");
            string tag = text[at..end];

            Assert.True(tag.Contains(Attribute, StringComparison.Ordinal),
                $"{file}: `{openingTag}` is app chrome behind every dialog but carries no {Attribute}, "
                + "so it stays focusable and described underneath an open modal.");
        }

        [Fact]
        public void NothingElseIsTaggedWithoutBeingDeclaredHere()
        {
            // A tag that appears somewhere this list does not know about is either a region that
            // should be declared or — the case that matters — something inside the dialog or
            // speech layer that would be silenced by it.
            var tagged = Directory
                .EnumerateFiles(Components(), "*.razor", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains(Attribute, StringComparison.Ordinal))
                .Select(f => Path.GetRelativePath(Components(), f))
                .ToHashSet(StringComparer.Ordinal);

            var declared = RegionRoots().Select(row => (string)row[0]!).ToHashSet(StringComparer.Ordinal);

            Assert.Equal(declared.OrderBy(x => x, StringComparer.Ordinal),
                         tagged.OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public void TheSpeechBuffersAndTheModalsAreNotInTheInertedSet()
        {
            // Stated as a negative on the one file that holds all three — the regions, the modals
            // and the live regions are siblings in MainLayout, and the difference between them is
            // exactly this attribute.
            string text = File.ReadAllText(Components("Layout", "MainLayout.razor"));

            foreach (var id in new[] { "aria-speech-1", "aria-speech-2" })
            {
                int at = text.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
                Assert.True(at >= 0, $"{id} is gone from MainLayout — the announcing channel moved.");
                int open = text.LastIndexOf('<', at);
                Assert.False(text[open..at].Contains(Attribute, StringComparison.Ordinal),
                    $"{id} is tagged as background chrome, so the app goes SILENT while any dialog is open.");
            }

            // The buffers' wrapper, one level up, is the other way to make the same mistake.
            int wrapper = text.IndexOf("<div class=\"visually-hidden\" aria-hidden=\"false\">", StringComparison.Ordinal);
            Assert.True(wrapper >= 0, "The speech buffer wrapper moved — re-aim this check before trusting it.");
            Assert.False(text[wrapper..text.IndexOf('>', wrapper)].Contains(Attribute, StringComparison.Ordinal),
                "The speech buffer wrapper is tagged as background chrome; inert would strip both buffers "
                + "out of the accessibility tree whenever a dialog is open.");
        }

        [Fact]
        public void TheJavaScriptLooksForTheSameAttribute()
        {
            string js = File.ReadAllText(Components("wwwroot", "js", "keyboard.js"));
            var selector = Regex.Match(js, @"_backgroundRegionSelector:\s*'([^']+)'");
            Assert.True(selector.Success, "keyboard.js no longer declares _backgroundRegionSelector.");
            Assert.Equal($"[{Attribute}]", selector.Groups[1].Value);
        }
    }
}

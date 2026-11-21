using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests;

/// <summary>
/// No emoji in what the app draws. The paper-trading banner's 📄 and the dashboard's tab marks
/// drew as an empty box on a machine without a colour-emoji font (the website photographs,
/// 2026-11-21), and a screen reader read some of them aloud by their Unicode names. The marks
/// are now drawn from IconSprite, which looks the same everywhere and is hidden from speech.
///
/// Scanned: every .razor file, and the code under the Blazor and WebHost projects. Comments are
/// left out, so a note can still say which emoji a mark replaced. Not scanned: the Telegram and
/// webhook alert channels, whose text goes to chat apps that draw emoji themselves.
/// </summary>
public sealed class EmojiFreeUiTests
{
    // The emoji planes, the emoji presentation selector that turns a plain mark into one, and
    // the same as an HTML entity ("&#128194;" was the Custom Scripts import button's folder).
    private static readonly Regex Emoji = new(
        @"[\uD83C-\uD83E][\uDC00-\uDFFF]|️|&#1[0-9]{5};|&#x1[fF][0-9a-fA-F]{3};",
        RegexOptions.Compiled);

    private static readonly Regex Comments = new(
        @"@\*.*?\*@|/\*.*?\*/|<!--.*?-->|(?<![:""'])//[^\n]*",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static bool Generated(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(p => p is "obj" or "bin" or "node_modules" or "lib");

    internal static IReadOnlyList<string> UiFiles()
    {
        string root = RepoRoot();
        var files = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(f => !Generated(Path.GetRelativePath(root, f)))
            .ToList();
        foreach (var project in new[] { "AccessibleTrader.BlazorClient.Components", "AccessibleTrader.BlazorClient", "AccessibleTrader.WebHost" })
        {
            string dir = Path.Combine(root, project);
            if (!Directory.Exists(dir)) continue;
            files.AddRange(Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".js") || f.EndsWith(".html"))
                .Where(f => !Generated(Path.GetRelativePath(root, f))));
        }
        return files.Distinct().ToList();
    }

    [Fact]
    public void No_emoji_in_the_interface()
    {
        var files = UiFiles();
        // A scan that finds no files passes for the wrong reason.
        Assert.Contains(files, f => f.EndsWith("TradingDashboardModal.razor"));
        Assert.Contains(files, f => f.EndsWith("CustomScriptsModal.razor"));
        Assert.True(files.Count > 100, $"expected the whole interface, found {files.Count} files");

        var found = new List<string>();
        foreach (var file in files)
        {
            string code = Comments.Replace(File.ReadAllText(file), m => new string('\n', m.Value.Count(c => c == '\n')));
            var lines = code.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (Emoji.IsMatch(lines[i]))
                    found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }

        Assert.True(found.Count == 0,
            "Emoji draw as an empty box without a colour-emoji font; use an IconSprite symbol:\n" + string.Join("\n", found));
    }

    [Theory]
    [InlineData("<span>📄 Paper trading</span>")]
    [InlineData("<button>&#128194; Import</button>")]
    [InlineData("<b>Show️</b>")]
    public void The_scan_sees_each_form_of_emoji(string markup)
    {
        Assert.Matches(Emoji, Comments.Replace(markup, ""));
    }

    [Fact]
    public void The_scan_skips_a_note_in_a_comment()
    {
        Assert.DoesNotMatch(Emoji, Comments.Replace("@* was 📄 *@ <span>/* 🔔 */</span>", ""));
    }
}

using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests;

/// <summary>
/// The app's chrome must stay reachable by landmark navigation.
///
/// <para>
/// The 2026-09-01 audit's highest value-to-effort finding was <c>&lt;nav role="toolbar"&gt;</c>:
/// an explicit role overrides the element's implicit one, so the app's primary chrome exposed a
/// toolbar and not a navigation landmark, and NVDA's <c>D</c> reached three landmarks — none of
/// them containing any of the ~41 toolbar controls. It was live on four containers and
/// <c>bc52e652</c> fixed all four.
/// </para>
///
/// <para>
/// <b>Why this test exists on top of that fix.</b>
/// <see cref="ChromeAccessibilityScanTests"/> bans the bad role. It cannot pin the good one:
/// its first act is <c>if (!text.Contains("role=\"toolbar\"")) continue;</c>, so changing
/// <c>&lt;nav&gt;</c> back to <c>&lt;div&gt;</c> — or simply deleting an <c>aria-label</c> — puts
/// the file straight past the guard and the app back to three landmarks with every test green
/// and not even a comment gone stale. A guard that forbids one spelling of a defect is not a
/// guard on the property; this asserts the property.
/// </para>
///
/// <para>
/// <b>The <c>aria-label</c> is load-bearing twice over</b>, which is why it is asserted rather
/// than assumed. Two <c>&lt;nav&gt;</c> elements in one document are indistinguishable in a
/// landmark list without names; and a <c>&lt;section&gt;</c> with no accessible name is not a
/// landmark AT ALL, so the label is the only thing making StatusBar's strip navigable.
/// </para>
/// </summary>
public class LandmarkContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Element names that are a landmark on their own, or with a name.</summary>
    private static readonly string[] LandmarkTags = { "nav", "section", "aside", "header", "footer", "main" };

    /// <summary>
    /// The chrome surfaces, each identified by the class its container carries. Deliberately
    /// keyed on class rather than on line number: the audit cited <c>Toolbar.razor:31</c> and the
    /// line has moved twice since.
    /// </summary>
    public static IEnumerable<object[]> Containers() => new[]
    {
        new object[] { "Toolbar.razor",     "toolbar",       "Main toolbar" },
        new object[] { "IndicatorBar.razor", "indicator-bar", "Indicator controls" },
        new object[] { "TouchNavBar.razor",  "touch-nav",     "Touch navigation" },
        new object[] { "StatusBar.razor",    "status-bar",    "Terminal status" },
    };


    /// <summary>
    /// The whole opening tag containing <paramref name="needle"/>, ending at the first
    /// <c>&gt;</c> that is not inside a quoted attribute value.
    ///
    /// <para>
    /// The obvious version of this is <c>&lt;(\w+)([^&gt;]*needle[^&gt;]*)&gt;</c> and it is wrong in a
    /// Razor file, in the silent direction. A Razor attribute value can legitimately contain a
    /// <c>&gt;</c> — <c>@onclick="() =&gt; Foo()"</c> — so <c>[^&gt;]*</c> stops early, and a
    /// <c>role=</c> or a deleted <c>aria-label=</c> written PAST that point would be invisible to
    /// this scan while every assertion below it still passed. A guard whose whole purpose is to
    /// assert a property rather than a spelling cannot afford a blind spot placed by attribute
    /// order.
    /// </para>
    /// </summary>
    internal static string? OpeningTagContaining(string src, string needle)
    {
        int hit = src.IndexOf(needle, StringComparison.Ordinal);
        if (hit < 0) return null;

        int open = src.LastIndexOf('<', hit);
        if (open < 0) return null;

        char quote = '\0';
        for (int i = open; i < src.Length; i++)
        {
            char c = src[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '>') return src[open..(i + 1)];
        }
        return null;   // unterminated tag
    }

    [Theory]
    [MemberData(nameof(Containers))]
    public void EveryChromeSurfaceIsANamedLandmark(string file, string className, string expectedLabel)
    {
        string path = Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components", file);
        Assert.True(File.Exists(path), path + " no longer exists; this contract needs re-pointing.");

        // Comments stripped first, so the explanation of why role="toolbar" was removed cannot
        // itself be read as markup. ModalContractScanTests learned this the hard way on 2026-09-03.
        string src = ModalContractScanTests.CodeOnly(File.ReadAllText(path));

        string? tag = OpeningTagContaining(src, "class=\"" + className + "\"");
        Assert.True(tag != null,
            $"{file} no longer has an element carrying class=\"{className}\". The landmark contract " +
            "cannot find the container it is supposed to be guarding.");

        string name = Regex.Match(tag!, "^<(?<n>[a-zA-Z][a-zA-Z0-9]*)").Groups["n"].Value.ToLowerInvariant();
        string attrs = tag!;

        Assert.True(LandmarkTags.Contains(name),
            $"{file}: class=\"{className}\" is on <{name}>, which is not a landmark. This surface is " +
            "part of the app's chrome and must be reachable by landmark navigation — that is the " +
            "regression the 2026-09-01 audit's headline finding was about. Use <nav> or a named " +
            "<section>; do not put the content back inside a bare <div>.");

        var role = Regex.Match(attrs, "\\brole\\s*=\\s*\"(?<r>[^\"]*)\"");
        Assert.False(role.Success,
            $"{file}: <{name} class=\"{className}\"> carries role=\"{role.Groups["r"].Value}\". An " +
            "explicit role OVERRIDES the element's implicit one, so this element is no longer a " +
            "landmark — exactly what <nav role=\"toolbar\"> did. If this surface needs a widget or " +
            "live-region role, put it on a child element, the way StatusBar does with role=\"status\".");

        var label = Regex.Match(attrs, "\\baria-label\\s*=\\s*\"(?<l>[^\"]*)\"");
        Assert.True(label.Success && !string.IsNullOrWhiteSpace(label.Groups["l"].Value),
            $"{file}: <{name} class=\"{className}\"> has no aria-label. There is more than one " +
            "navigation landmark in this app, so an unnamed one is unidentifiable in a landmark " +
            "list — and an unnamed <section> is not a landmark at all.");

        Assert.Equal(expectedLabel, label.Groups["l"].Value);
    }
}

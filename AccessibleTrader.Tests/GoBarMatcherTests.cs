using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.GoBar;

namespace AccessibleTrader.Tests;

/// <summary>
/// The five tiers in their fixed order, the invariant upper-casing, and the one rule that is
/// allowed to override all of them: money is never fuzzy.
/// </summary>
public class GoBarMatcherTests
{
    private static GoBarEntry Entry(
        string name,
        string[]? codes = null,
        string[]? aliases = null,
        GoBarKind kind = GoBarKind.Normal,
        SystemCommand command = SystemCommand.None)
        => new(command, name, codes ?? Array.Empty<string>(), aliases ?? Array.Empty<string>(),
               "Help line", kind);

    private static IReadOnlyList<GoBarMatch> Match(string input, params GoBarEntry[] entries)
        => GoBarMatcher.Match(input, entries);

    [Fact]
    public void AnExactCodeBeatsAPrefixOfAnotherCode()
    {
        var prefix = Entry("Alpha beta", codes: new[] { "ABCDE" }, command: SystemCommand.OpenSettings);
        var exact = Entry("Alpha", codes: new[] { "ABC" }, command: SystemCommand.OpenHelp);

        var results = Match("ABC", prefix, exact);

        Assert.Equal(2, results.Count);
        Assert.Equal(SystemCommand.OpenHelp, results[0].Entry.Command);
        Assert.Equal(GoBarMatchKind.ExactCode, results[0].How);
        Assert.Equal(SystemCommand.OpenSettings, results[1].Entry.Command);
        Assert.Equal(GoBarMatchKind.Prefix, results[1].How);
    }

    [Fact]
    public void AnExactAliasIsTierTwo()
    {
        var entry = Entry("Market events", codes: new[] { "ECO" }, aliases: new[] { "calendar", "economic" });

        var result = Assert.Single(Match("calendar", entry));

        Assert.Equal(GoBarMatchKind.ExactAlias, result.How);
        Assert.Equal("calendar", result.MatchedText);
    }

    [Fact]
    public void AnExactNameIsTierTwo()
    {
        var entry = Entry("Market pulse", codes: new[] { "PULSE" }, aliases: new[] { "pulse" });

        var result = Assert.Single(Match("market pulse", entry));

        Assert.Equal(GoBarMatchKind.ExactName, result.How);
        Assert.Equal("Market pulse", result.MatchedText);
    }

    [Fact]
    public void APrefixMatchesACodeAliasOrName()
    {
        var entry = Entry("Market pulse", codes: new[] { "PULSE" }, aliases: new[] { "pulse" });

        Assert.All(
            new[] { "PUL", "puls" },
            input =>
            {
                var result = Assert.Single(Match(input, entry));
                Assert.Equal(GoBarMatchKind.Prefix, result.How);
            });

        var namePrefix = Assert.Single(Match("market p", entry));
        Assert.Equal(GoBarMatchKind.Prefix, namePrefix.How);
    }

    [Fact]
    public void TheStartOfAWordMatchesInsideAName()
    {
        var entry = Entry("Log scale", codes: new[] { "LOG" });

        var result = Assert.Single(Match("scale", entry));

        Assert.Equal(GoBarMatchKind.WordStart, result.How);
        Assert.Equal("scale", result.MatchedText);
    }

    [Fact]
    public void NearMissesNeedAtLeastFourCharacters()
    {
        var entry = Entry("Alpha", codes: new[] { "ABC" });

        Assert.Empty(Match("ABD", entry));
        Assert.Empty(Match("AB", entry));
    }

    [Fact]
    public void FourToSevenCharactersAllowOneEdit()
    {
        var entry = Entry("Market pulse", codes: new[] { "PULSE" });

        var result = Assert.Single(Match("plse", entry));

        Assert.Equal(GoBarMatchKind.NearMiss, result.How);
    }

    [Fact]
    public void EightOrMoreCharactersAllowTwoEdits()
    {
        var entry = Entry("Alpha beta", codes: new[] { "ABCDEFGH" });

        var two = Assert.Single(Match("ABCDEFXX", entry));
        Assert.Equal(GoBarMatchKind.NearMiss, two.How);

        Assert.Empty(Match("ABCDEXXX", entry));
    }

    [Fact]
    public void AnEntryAppearsOnceAtItsBestTier()
    {
        var entry = Entry("Log scale", codes: new[] { "LOG" }, aliases: new[] { "log" });

        var result = Assert.Single(Match("log", entry));

        Assert.Equal(GoBarMatchKind.ExactCode, result.How);
    }

    [Fact]
    public void TurkishDottedIDoesNotBreakMatching()
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            var entry = Entry("Add an indicator", codes: new[] { "IND" });

            var result = Assert.Single(Match("ind", entry));
            Assert.Equal(GoBarMatchKind.ExactCode, result.How);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void WhitespaceIsTrimmedAndCollapsed()
    {
        var entry = Entry("Market pulse", codes: new[] { "PULSE" });

        var result = Assert.Single(Match("  market   pulse  ", entry));

        Assert.Equal(GoBarMatchKind.ExactName, result.How);
    }

    [Fact]
    public void EmptyInputHasNoMatches()
    {
        var entry = Entry("Market pulse", codes: new[] { "PULSE" });

        Assert.Empty(Match("", entry));
        Assert.Empty(Match("   ", entry));
    }

    [Fact]
    public void ResultsAreCappedAtTwenty()
    {
        var entries = Enumerable.Range(0, 25)
            .Select(i => Entry($"Alpha {i:00}", codes: new[] { $"AA{i:00}" }))
            .ToArray();

        var results = Match("A", entries);

        Assert.Equal(GoBarMatcher.MaxMatches, results.Count);
    }

    // ── Money is never fuzzy ────────────────────────────────────────────────

    [Fact]
    public void AMoneyCommandRunsOnItsExactCodeOrExactFullName()
    {
        var entry = GoBarRegistry.All.Single(e => e.Command == SystemCommand.QuickArmRisk2);

        var byCode = Match("R10", GoBarRegistry.All.ToArray())
            .Single(m => m.Entry.Command == SystemCommand.QuickArmRisk2);
        Assert.Equal(GoBarMatchKind.ExactCode, byCode.How);

        var byName = Match("Arm 1 percent risk", GoBarRegistry.All.ToArray())
            .Single(m => m.Entry.Command == SystemCommand.QuickArmRisk2);
        Assert.Equal(GoBarMatchKind.ExactName, byName.How);
    }

    [Fact]
    public void AMoneyCommandNeverAppearsOnAPrefixAliasWordStartOrTypo()
    {
        var entries = GoBarRegistry.All.ToArray();

        void Never(string input)
        {
            Assert.DoesNotContain(
                GoBarMatcher.Match(input, entries),
                m => m.Entry.Command == SystemCommand.QuickArmRisk2);
        }

        Never("R1");          // prefix of its code
        Never("arm 1");       // exact alias, but not the full name
        Never("risk");        // start of a word in its name
        Never("R11");         // one-letter typo of its code
        Never("Arm 1 percent"); // prefix of its name
    }
}

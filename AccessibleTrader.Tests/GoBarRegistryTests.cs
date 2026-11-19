using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;

namespace AccessibleTrader.Tests;

/// <summary>
/// The guard the Go bar exists for: every action the terminal has is in the registry, and the
/// registry's own rules hold. Removing one entry must turn this red.
/// </summary>
public class GoBarRegistryTests
{
    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    [Fact]
    public void EveryCommandExceptNoneHasExactlyOneEntry()
    {
        var all = Enum.GetValues(typeof(SystemCommand)).Cast<SystemCommand>().ToList();

        Assert.DoesNotContain(GoBarRegistry.All, e => e.Command == SystemCommand.None);

        var missing = all
            .Where(c => c != SystemCommand.None && GoBarRegistry.All.Count(e => e.Command == c) != 1)
            .ToList();

        Assert.True(missing.Count == 0,
            "These SystemCommand values do not have exactly one Go bar entry: "
            + string.Join(", ", missing));

        Assert.Equal(all.Count - 1, GoBarRegistry.All.Count);
    }

    [Fact]
    public void EveryEntryHasANameAndAHelpLine()
    {
        var broken = GoBarRegistry.All
            .Where(e => string.IsNullOrWhiteSpace(e.Name) || string.IsNullOrWhiteSpace(e.Help))
            .Select(e => e.Command.ToString())
            .ToList();

        Assert.True(broken.Count == 0,
            "Go bar entries need a spoken name and a one-line help: " + string.Join(", ", broken));
    }

    [Fact]
    public void CodesAreWellFormedAndGloballyUnique()
    {
        var seen = new Dictionary<string, SystemCommand>(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var entry in GoBarRegistry.All)
        {
            if (entry.Codes.Count > 3)
                problems.Add($"{entry.Command} has {entry.Codes.Count} codes; the limit is three");

            foreach (var code in entry.Codes)
            {
                if (!Regex.IsMatch(code, "^[A-Z0-9]{2,8}$"))
                {
                    problems.Add($"{entry.Command} code '{code}' is not 2–8 upper-case letters or digits");
                    continue;
                }

                if (seen.TryGetValue(code, out var other))
                    problems.Add($"code {code} is used by both {other} and {entry.Command}");
                else
                    seen[code] = entry.Command;
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void AliasesAreLowerCase()
    {
        var broken = GoBarRegistry.All
            .SelectMany(e => e.Aliases.Select(a => (e.Command, Alias: a)))
            .Where(x => string.IsNullOrWhiteSpace(x.Alias) || x.Alias != x.Alias.ToLowerInvariant())
            .Select(x => $"{x.Command}: '{x.Alias}'")
            .ToList();

        Assert.True(broken.Count == 0, "Aliases are typed words and must be lower case:\n"
            + string.Join("\n", broken));
    }

    [Fact]
    public void EveryQuickCommandIsMoney()
    {
        var quick = GoBarRegistry.All.Where(e => e.Command.ToString().StartsWith("Quick", StringComparison.Ordinal)).ToList();

        Assert.Equal(8, quick.Count);
        Assert.All(quick, e => Assert.Equal(GoBarKind.Money, e.Kind));
    }

    [Fact]
    public void EveryCommandBoundToAnArrowOrPageKeyIsKeyOnly()
    {
        var dir = TestTemp.NewDir("att-gobar-shortcuts-");
        var manager = new ShortcutManager(new TempPaths(dir));

        var navigationKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "LEFT", "RIGHT", "UP", "DOWN", "HOME", "END", "PAGEUP", "PAGEDOWN",
        };

        var relevant = manager.CurrentProfile.Shortcuts
            .Where(s => navigationKeys.Contains(KeyNormalizationService.NormalizeKey(s.Key).ToUpperInvariant()))
            .ToList();

        Assert.True(relevant.Count >= 10,
            $"fixture floor: only {relevant.Count} arrow/Page bindings were found");

        var broken = relevant
            .Select(s => (Binding: s, Entry: GoBarRegistry.All.SingleOrDefault(e => e.Command == s.Command)))
            .Where(x => x.Entry == null || x.Entry.Kind != GoBarKind.KeyOnly)
            .Select(x => $"{x.Binding.Command} on {x.Binding.Key}"
                         + (x.Entry == null ? " has no entry" : $" is {x.Entry.Kind}"))
            .ToList();

        Assert.True(broken.Count == 0,
            "These keys move the cursor or the page, so the Go bar can only name the key:\n"
            + string.Join("\n", broken));
    }

    [Fact]
    public void TheNamedCodesMapToTheRightCommands()
    {
        var expected = new Dictionary<string, SystemCommand>(StringComparer.Ordinal)
        {
            ["ECO"] = SystemCommand.OpenMarketEventCalendar,
            ["PULSE"] = SystemCommand.MarketPulse,
            ["CHANGES"] = SystemCommand.WhatChanged,
            ["DES"] = SystemCommand.SpeakChartLayout,
            ["HELP"] = SystemCommand.OpenHelp,
            ["GO"] = SystemCommand.OpenGoBar,
        };

        foreach (var (code, command) in expected)
        {
            var entries = GoBarRegistry.All.Where(e => e.Codes.Contains(code, StringComparer.Ordinal)).ToList();
            Assert.True(entries.Count == 1, $"code {code} should name exactly one command, found {entries.Count}");
            Assert.Equal(command, entries[0].Command);
        }
    }

    /// <summary>
    /// A money code is reached only by typing it exactly, so it must never be a word somebody
    /// types while looking for something else. "MARKET" was: "market" meant to find Market
    /// pulse or the market watch was an exact code for "Place a market order", top of the
    /// list, one Enter from an order. Every word of every other entry's name and aliases, and
    /// every other code, is a thing the user may type; none may be a money code.
    /// </summary>
    [Fact]
    public void NoMoneyCodeIsAWordAnotherCommandIsKnownBy()
    {
        var otherWords = GoBarRegistry.All
            .Where(e => e.Kind != GoBarKind.Money)
            .SelectMany(e => e.Codes
                .Concat(e.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Concat(e.Aliases.SelectMany(a => a.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                .Concat(e.Aliases))
            .Select(w => w.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var clashes = GoBarRegistry.All
            .Where(e => e.Kind == GoBarKind.Money)
            .SelectMany(e => e.Codes.Where(c => otherWords.Contains(c.ToUpperInvariant()))
                .Select(c => $"{c} ({e.Command})"))
            .ToList();

        Assert.True(clashes.Count == 0,
            "money codes that are also a word for another command: " + string.Join(", ", clashes));
    }

    [Theory]
    [InlineData("market")]
    [InlineData("stop")]
    [InlineData("limit")]
    [InlineData("armed")]
    [InlineData("disarm")]
    public void TypingAnOrdinaryWordNeverOffersAMoneyCommand(string typed)
    {
        var money = GoBarMatcher.Match(typed, GoBarRegistry.All)
            .Where(m => m.Entry.Kind == GoBarKind.Money)
            .Select(m => m.Entry.Command)
            .ToList();
        Assert.True(money.Count == 0, $"'{typed}' offered {string.Join(", ", money)}");
    }
}

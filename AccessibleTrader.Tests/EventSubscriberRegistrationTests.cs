using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// A class that subscribes to the event bus in its constructor must be registered, and must be
/// resolved by something — or it never exists and its subscription never happens.
///
/// <para>
/// <b>The defect this exists to prevent.</b> <c>QuickTradeExecutor</c> is the half of quick trade
/// that turns a committed trade into an order: it subscribes to <c>QuickTradeRequestedEvent</c> and
/// calls the order service. It was **never registered in DI and never instantiated**. So
/// <c>QuickTradeService</c> published the event into a void — the feature announced "sent", produced
/// no fill, no rejection and no position, and had never placed a single order since it was written.
/// </para>
///
/// <para>
/// Nothing could catch it. It compiled. Its own unit tests passed, because they construct it
/// directly. The DI container never complained, because nothing asked for a service that was not
/// there. The failure only existed in the gap between "the class is correct" and "the class runs".
/// </para>
///
/// <para>
/// <b>Registration is only half.</b> A scoped or singleton service is constructed lazily, on first
/// resolve. A subscriber that nobody injects is registered and still never runs, which looks exactly
/// like the bug it just fixed — so this also checks something actually resolves it.
/// </para>
/// </summary>
public class EventSubscriberRegistrationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// Types whose constructor subscribes to the event bus. Constructor-only: a class that subscribes
    /// from an Initialize method has an explicit caller and is therefore already accounted for.
    /// </summary>
    private static List<string> ConstructorSubscribers()
    {
        var found = new List<string>();
        string core = Path.Combine(RepoRoot(), "AccessibleTrader.Core");

        foreach (string file in Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            string text = File.ReadAllText(file);
            if (!text.Contains(".Subscribe<")) continue;

            foreach (Match m in Regex.Matches(text, @"(?:sealed\s+)?class\s+(\w+)"))
            {
                string type = m.Groups[1].Value;
                // The constructor body is where a subscription makes the type self-starting.
                var ctor = Regex.Match(text, $@"public\s+{Regex.Escape(type)}\s*\([^)]*\)[^{{]*{{(.*?)\n        }}",
                                       RegexOptions.Singleline);
                if (ctor.Success && ctor.Groups[1].Value.Contains(".Subscribe<"))
                    found.Add(type);
            }
        }

        return found.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    private static string Registrations(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    /// <summary>
    /// Subscribers that are legitimately created by hand rather than by the container, with the
    /// reason. An entry here is a claim that something else guarantees the type is constructed.
    /// </summary>
    private static readonly Dictionary<string, string> ConstructedElsewhere = new(StringComparer.Ordinal)
    {
        // (empty — add with a reason if a subscriber is genuinely hand-built)
    };

    [Fact]
    public void EverySelfSubscribingServiceIsRegisteredOnBothHeads()
    {
        string web = Registrations("AccessibleTrader.WebHost/ServiceCollectionExtensions.cs");
        string maui = Registrations("AccessibleTrader.BlazorClient/ServiceCollectionExtensions.cs");

        var missing = new List<string>();
        foreach (string type in ConstructorSubscribers())
        {
            if (ConstructedElsewhere.ContainsKey(type)) continue;
            bool inWeb = web.Contains(type, StringComparison.Ordinal);
            bool inMaui = maui.Contains(type, StringComparison.Ordinal);
            if (!inWeb || !inMaui)
                missing.Add($"{type} (WebHost: {(inWeb ? "yes" : "NO")}, MAUI: {(inMaui ? "yes" : "NO")})");
        }

        Assert.True(missing.Count == 0,
            "These types subscribe to the event bus in their constructor but are not registered on "
          + "both heads. A subscriber that is never constructed never subscribes, and the events it "
          + "was written to handle vanish silently — which is exactly how quick trade announced "
          + "\"sent\" while never placing an order:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Registration alone is not enough: lazy construction means a subscriber nobody injects still
    /// never runs. Something has to resolve it.
    /// </summary>
    [Fact]
    public void TheQuickTradeExecutorIsActuallyResolvedBySomething()
    {
        string root = RepoRoot();
        bool injected = Directory
            .EnumerateFiles(Path.Combine(root, "AccessibleTrader.BlazorClient.Components"), "*.razor",
                            SearchOption.AllDirectories)
            .Any(f => File.ReadAllText(f).Contains("QuickTradeExecutor", StringComparison.Ordinal));

        Assert.True(injected,
            "QuickTradeExecutor is registered but nothing injects it. DI constructs scoped and "
          + "singleton services lazily, so a subscriber nobody resolves is never built and never "
          + "subscribes — indistinguishable from not being registered at all.");
    }

    /// <summary>
    /// Guards the guard. If the scan stopped finding subscribers, the check above would pass by
    /// examining an empty list.
    /// </summary>
    [Fact]
    public void TheScanFindsSubscribers()
    {
        var found = ConstructorSubscribers();
        Assert.True(found.Count >= 3,
            "The constructor-subscriber scan found almost nothing, so it is probably not checking "
          + "anything: " + string.Join(", ", found));
        Assert.Contains("QuickTradeExecutor", found);
    }
}

using AccessibleTrader.Core.Services.ChangeLog;

namespace AccessibleTrader.Tests;

public class ChangeLogRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    private static ChangeLogEntry E(
        string key,
        ChangeLogCategory category = ChangeLogCategory.Money,
        int minutesAgo = 0,
        bool heard = false,
        string text = "something happened.")
        => new(key, Now.AddMinutes(-minutesAgo), category, text, null, heard);

    private static ChangeLogDocument Doc(params ChangeLogEntry[] entries)
        => new(entries.ToList());

    [Fact]
    public void AnEntryWhoseKeyIsAlreadyPresentIsIgnored()
    {
        var doc = Doc(E("a", text: "first."), E("b"));

        var next = ChangeLogRules.Append(doc, E("a", ChangeLogCategory.Alert, text: "second."), Now);

        Assert.Equal(2, next.Entries.Count);
        Assert.Equal("first.", next.Entries.Single(e => e.Key == "a").Text);
    }

    [Fact]
    public void FiveHundredEntriesAndSevenDaysKeepTheNewestAndNeverReorder()
    {
        var entries = new List<ChangeLogEntry>
        {
            E("too-old", minutesAgo: 8 * 24 * 60),
        };
        // Newest first on purpose: pruning must pick by time, not by list position.
        for (int i = 1; i <= 500; i++)
            entries.Add(E($"k{i:000}", minutesAgo: i));

        var next = ChangeLogRules.Append(Doc(entries.ToArray()),
            E("brand-new", minutesAgo: 0), Now);

        Assert.Equal(500, next.Entries.Count);
        Assert.DoesNotContain(next.Entries, e => e.Key == "too-old");
        Assert.DoesNotContain(next.Entries, e => e.Key == "k500");
        Assert.Contains(next.Entries, e => e.Key == "brand-new");
        Assert.Contains(next.Entries, e => e.Key == "k499");

        Assert.Equal("k001", next.Entries[0].Key);
        Assert.Equal("brand-new", next.Entries[^1].Key);
    }

    [Fact]
    public void AppendingAnOlderEntryWithinSevenDaysDoesNotReorderTheLog()
    {
        var doc = Doc(E("newer", minutesAgo: 1), E("oldest", minutesAgo: 3));

        var next = ChangeLogRules.Append(doc, E("late", minutesAgo: 5), Now);

        Assert.Equal(new[] { "newer", "oldest", "late" },
            next.Entries.Select(e => e.Key));
    }

    [Fact]
    public void ThePlanReadsCategoryOrderThenOldestFirstAndTakesFive()
    {
        var doc = Doc(
            E("connection", ChangeLogCategory.Connection, minutesAgo: 1),
            E("signal", ChangeLogCategory.Signal, minutesAgo: 2),
            E("unusual", ChangeLogCategory.Unusual, minutesAgo: 3),
            E("event", ChangeLogCategory.Event, minutesAgo: 4),
            E("alert-new", ChangeLogCategory.Alert, minutesAgo: 5),
            E("alert-old", ChangeLogCategory.Alert, minutesAgo: 6),
            E("money-new", ChangeLogCategory.Money, minutesAgo: 7),
            E("money-old", ChangeLogCategory.Money, minutesAgo: 8));

        var plan = ChangeLogRules.Plan(doc, Now, maxItems: 5);

        Assert.Equal(new[] { "money-old", "money-new", "alert-old", "alert-new", "event" },
            plan.Items.Select(e => e.Key));
        Assert.Equal(8, plan.TotalUnheard);
        Assert.Equal(3, plan.Remaining);
    }

    [Fact]
    public void HeardEntriesAreNotInThePlan()
    {
        var doc = Doc(E("heard", heard: true), E("unheard"));

        var plan = ChangeLogRules.Plan(doc, Now);

        Assert.Equal(new[] { "unheard" }, plan.Items.Select(e => e.Key));
        Assert.Equal(1, plan.TotalUnheard);
    }

    [Fact]
    public void MarkHeardTouchesOnlyTheGivenKeysAndSetsTheCatchUpTime()
    {
        var lastPresent = Now.AddHours(-1);
        var doc = new ChangeLogDocument(
            new List<ChangeLogEntry> { E("a"), E("b"), E("c") },
            LastCatchUpUtc: null,
            LastPresentUtc: lastPresent);

        var next = ChangeLogRules.MarkHeard(doc, new[] { "a", "c" }, Now);

        Assert.True(next.Entries.Single(e => e.Key == "a").Heard);
        Assert.False(next.Entries.Single(e => e.Key == "b").Heard);
        Assert.True(next.Entries.Single(e => e.Key == "c").Heard);
        Assert.Equal(Now, next.LastCatchUpUtc);
        Assert.Equal(lastPresent, next.LastPresentUtc);
    }

    [Fact]
    public void UnheardKeepsTheDocumentOrder()
    {
        var doc = Doc(E("one", heard: true), E("two"), E("three"));

        Assert.Equal(new[] { "two", "three" },
            ChangeLogRules.Unheard(doc).Select(e => e.Key));
    }
}

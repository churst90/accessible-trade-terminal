using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.ChangeLog;

namespace AccessibleTrader.Tests;

[Collection(nameof(SpeechClockSettingsTests))]
public class ChangeLogWordingTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 18, 10, 27, 0, DateTimeKind.Utc);

    public ChangeLogWordingTests()
    {
        SpeechTimeFormatter.UseUtc = true;
        SpeechTimeFormatter.TwelveHour = false;
    }

    public void Dispose()
    {
        SpeechTimeFormatter.UseUtc = false;
        SpeechTimeFormatter.TwelveHour = false;
    }

    private static ChangeLogEntry E(
        string key, ChangeLogCategory category, int hour, int minute,
        string text, bool heard = false)
        => new(key, new DateTime(2026, 11, 18, hour, minute, 0, DateTimeKind.Utc),
            category, text, null, heard);

    private static string Speak(ChangeLogDocument doc, bool followUp = false)
        => ChangeLogWording.Press(doc, ChangeLogRules.Plan(doc, Now), Now, followUp);

    [Fact]
    public void TheFirstPressNamesTheCountTheCatchUpTimeAndTheAge()
    {
        var doc = new ChangeLogDocument(
            new List<ChangeLogEntry>
            {
                E("m1", ChangeLogCategory.Money, 9, 52, "BTC/USDT stop hit, sold 0.1 at 94,210."),
                E("a1", ChangeLogCategory.Alert, 9, 44, "BTC/USDT crossed above 95,000."),
                E("a2", ChangeLogCategory.Alert, 10, 1, "ETH/USDT RSI above 70."),
                E("e1", ChangeLogCategory.Event, 10, 0, "CPI out, up 0.3, hotter than expected."),
                E("c1", ChangeLogCategory.Connection, 10, 12, "MEXC disconnected."),
                E("a3", ChangeLogCategory.Alert, 10, 2, "SOL/USDT crossed above 200."),
                E("c2", ChangeLogCategory.Connection, 10, 13, "Kraken disconnected."),
            },
            LastCatchUpUtc: new DateTime(2026, 11, 18, 9, 40, 0, DateTimeKind.Utc));

        string sentence = Speak(doc);

        Assert.StartsWith("7 changes since 09:40, 47 minutes ago. ", sentence);
        Assert.Contains("Money: 09:52, BTC/USDT stop hit, sold 0.1 at 94,210.", sentence);
        Assert.Contains("Alerts: 09:44, BTC/USDT crossed above 95,000.", sentence);
        Assert.Contains("10:01, ETH/USDT RSI above 70.", sentence);
        Assert.Contains("10:02, SOL/USDT crossed above 200.", sentence);
        Assert.Contains("Events: 10:00, CPI out, up 0.3, hotter than expected.", sentence);
        Assert.Contains("Press Alt+U again for 2 more.", sentence);
        Assert.DoesNotContain("Alerts: 10:01", sentence);
    }

    [Fact]
    public void OneChangeIsSingular()
    {
        var doc = new ChangeLogDocument(
            new List<ChangeLogEntry> { E("m1", ChangeLogCategory.Money, 9, 52, "BTC/USDT filled.") },
            LastCatchUpUtc: new DateTime(2026, 11, 18, 9, 40, 0, DateTimeKind.Utc));

        Assert.StartsWith("1 change since 09:40, 47 minutes ago.", Speak(doc));
    }

    [Fact]
    public void WithoutACatchUpTimeTheOldestUnheardEntrySetsTheSinceTime()
    {
        var doc = new ChangeLogDocument(new List<ChangeLogEntry>
        {
            E("m1", ChangeLogCategory.Money, 9, 52, "BTC/USDT filled."),
            E("a1", ChangeLogCategory.Alert, 10, 10, "BTC/USDT crossed above 95,000."),
        });

        Assert.StartsWith("2 changes since 09:52, 35 minutes ago.", Speak(doc));
    }

    [Fact]
    public void EachCategoryWordIsSaidOncePerPress()
    {
        var doc = new ChangeLogDocument(new List<ChangeLogEntry>
        {
            E("m1", ChangeLogCategory.Money, 9, 50, "BTC/USDT filled."),
            E("m2", ChangeLogCategory.Money, 9, 51, "ETH/USDT filled."),
            E("c1", ChangeLogCategory.Connection, 9, 52, "MEXC disconnected."),
        });

        string sentence = Speak(doc);

        Assert.Contains("Money: 09:50, BTC/USDT filled. 09:51, ETH/USDT filled.", sentence);
        Assert.Contains("Connection: 09:52, MEXC disconnected.", sentence);
        Assert.Equal(1, Count(sentence, "Money:"));
        Assert.Equal(1, Count(sentence, "Connection:"));
    }

    [Fact]
    public void TheFollowUpPressStartsWithHowManyMoreThereAre()
    {
        var doc = new ChangeLogDocument(new List<ChangeLogEntry>
        {
            E("m1", ChangeLogCategory.Money, 9, 50, "BTC/USDT filled."),
            E("m2", ChangeLogCategory.Money, 9, 51, "ETH/USDT filled."),
        });

        string sentence = Speak(doc, followUp: true);

        Assert.StartsWith("2 more. Money: 09:50, BTC/USDT filled. 09:51, ETH/USDT filled.", sentence);
        Assert.DoesNotContain("since", sentence);
    }

    [Fact]
    public void NothingNewUsesTheLastCatchUpTime()
    {
        var doc = new ChangeLogDocument(
            new List<ChangeLogEntry>
            {
                E("m1", ChangeLogCategory.Money, 9, 50, "BTC/USDT filled.", heard: true),
            },
            LastCatchUpUtc: new DateTime(2026, 11, 18, 9, 40, 0, DateTimeKind.Utc));

        Assert.Equal("Nothing new since 09:40.", Speak(doc));
    }

    [Fact]
    public void AnEmptyLogSaysNothingHasBeenRecorded()
    {
        Assert.Equal("Nothing recorded yet.", Speak(ChangeLogDocument.Empty));
    }

    [Fact]
    public void TheReturnPromptCountsWhatChangedAndNeverReadsIt()
    {
        var lastPresent = new DateTime(2026, 11, 18, 9, 40, 0, DateTimeKind.Utc);

        Assert.Equal(
            "You were away 47 minutes, 3 things changed, Alt+U to hear them.",
            ChangeLogWording.ReturnPrompt(lastPresent, 3, Now));
        Assert.Equal(
            "You were away 47 minutes, 1 thing changed, Alt+U to hear them.",
            ChangeLogWording.ReturnPrompt(lastPresent, 1, Now));
    }

    [Fact]
    public void ATrailingPeriodOnAStoredSentenceNeverMakesADoubleStop()
    {
        var doc = new ChangeLogDocument(new List<ChangeLogEntry>
        {
            E("m1", ChangeLogCategory.Money, 9, 52, "BTC/USDT stop hit, sold 0.1 at 94,210."),
        });

        string sentence = Speak(doc);

        Assert.DoesNotContain(". .", sentence);
        Assert.Contains("BTC/USDT stop hit, sold 0.1 at 94,210.", sentence);
    }

    [UseCulture("de-DE")]
    [Fact]
    public void TheWordsAreCultureInvariant()
    {
        var doc = new ChangeLogDocument(
            new List<ChangeLogEntry>
            {
                E("m1", ChangeLogCategory.Money, 9, 52, "BTC/USDT stop hit, sold 0.1 at 94,210."),
            },
            LastCatchUpUtc: new DateTime(2026, 11, 18, 9, 40, 0, DateTimeKind.Utc));

        Assert.Equal(
            "1 change since 09:40, 47 minutes ago. Money: 09:52, BTC/USDT stop hit, sold 0.1 at 94,210.",
            Speak(doc));
    }

    [Fact]
    public void TimesFollowTheUsersClockSettings()
    {
        SpeechTimeFormatter.TwelveHour = true;
        var doc = new ChangeLogDocument(
            new List<ChangeLogEntry>
            {
                E("m1", ChangeLogCategory.Money, 14, 30, "BTC/USDT filled."),
            },
            LastCatchUpUtc: new DateTime(2026, 11, 18, 9, 40, 0, DateTimeKind.Utc));

        string sentence = Speak(doc);

        Assert.Contains("Money: 2:30 PM, BTC/USDT filled.", sentence);
        Assert.StartsWith("1 change since 9:40 AM", sentence);
    }

    [Fact]
    public void AgesReadAsMinutesHoursAndDays()
    {
        Assert.Equal("47 minutes", ChangeLogWording.Age(Now.AddMinutes(-47), Now));
        Assert.Equal("1 minute", ChangeLogWording.Age(Now.AddSeconds(-61), Now));
        Assert.Equal("1 hour 5 minutes", ChangeLogWording.Age(Now.AddMinutes(-65), Now));
        Assert.Equal("2 hours", ChangeLogWording.Age(Now.AddHours(-2), Now));
        Assert.Equal("1 day", ChangeLogWording.Age(Now.AddDays(-1), Now));
        Assert.Equal("2 days", ChangeLogWording.Age(Now.AddDays(-2), Now));
    }

    private static int Count(string haystack, string needle)
    {
        int count = 0;
        int at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }
}

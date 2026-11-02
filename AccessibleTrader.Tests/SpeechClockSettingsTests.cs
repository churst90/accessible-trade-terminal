using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using Microsoft.Extensions.Logging.Abstractions;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests;

/// <summary>Runs alone: <see cref="SpeechTimeFormatter.UseUtc"/> and
/// <see cref="SpeechTimeFormatter.TwelveHour"/> are process-wide, and a test that flips them while
/// another formats a time would make that one fail for a reason it cannot see.</summary>
[CollectionDefinition(nameof(SpeechClockSettingsTests), DisableParallelization = true)]
public sealed class SpeechClockSettingsCollection { }

/// <summary>
/// THE CLOCK TIMES ARE READ ON — this computer's zone or UTC, 24-hour or 12-hour (Settings, F12,
/// Speech).
///
/// <para>
/// Cody, 2026-11-01, the day the clocks went back: <i>"can I have the option in f12 settings on
/// settings the candle timestamps to local time or utc....because I feel like the candle times
/// are off"</i>, then <i>"I may not want 24 hour time, so we need AM and PM if we do 12 hour
/// time"</i>.
/// </para>
/// </summary>
[Collection(nameof(SpeechClockSettingsTests))]
public sealed class SpeechClockSettingsTests : IDisposable
{
    private static readonly DateTime HalfPastTwoPmUtc = new(2026, 11, 1, 14, 30, 0, DateTimeKind.Utc);

    public SpeechClockSettingsTests()
    {
        SpeechTimeFormatter.UseUtc = false;
        SpeechTimeFormatter.TwelveHour = false;
    }

    public void Dispose()
    {
        SpeechTimeFormatter.UseUtc = false;
        SpeechTimeFormatter.TwelveHour = false;
    }

    [Fact]
    public void With_UTC_on_an_intraday_time_is_the_bars_UTC_clock_time()
    {
        SpeechTimeFormatter.UseUtc = true;

        Assert.Equal("14:30", SpeechTimeFormatter.FormatTime(HalfPastTwoPmUtc));
        Assert.Equal("14:30", SpeechTimeFormatter.FormatBarClock(HalfPastTwoPmUtc, 3600));
        // The Kind says which clock was used on any machine, a UTC build agent included.
        Assert.Equal(DateTimeKind.Utc, SpeechTimeFormatter.ToDisplay(HalfPastTwoPmUtc).Kind);
        Assert.Equal(DateTimeKind.Utc, SpeechTimeFormatter.ToDisplay(HalfPastTwoPmUtc.ToLocalTime()).Kind);
        Assert.Equal(HalfPastTwoPmUtc, SpeechTimeFormatter.ToDisplay(HalfPastTwoPmUtc.ToLocalTime()));
    }

    [Fact]
    public void Off_by_default_an_intraday_time_is_this_computers_clock_time()
    {
        var shown = SpeechTimeFormatter.ToDisplay(HalfPastTwoPmUtc);

        Assert.Equal(DateTimeKind.Local, shown.Kind);
        Assert.Equal(HalfPastTwoPmUtc.ToLocalTime(), shown);
        Assert.Equal(HalfPastTwoPmUtc.ToLocalTime().ToString("HH:mm"), SpeechTimeFormatter.FormatTime(HalfPastTwoPmUtc));
    }

    [Fact]
    public void A_daily_bar_is_named_by_its_own_date_in_UTC_and_local_alike()
    {
        var day = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        string local = SpeechTimeFormatter.FormatBarClock(day, 86400);
        SpeechTimeFormatter.UseUtc = true;

        Assert.Equal("November 1 2026", local);
        Assert.Equal(local, SpeechTimeFormatter.FormatBarClock(day, 86400));
    }

    [Fact]
    public void A_twelve_hour_clock_says_AM_and_PM()
    {
        SpeechTimeFormatter.UseUtc = true;
        SpeechTimeFormatter.TwelveHour = true;

        Assert.Equal("2:30 PM", SpeechTimeFormatter.FormatTime(HalfPastTwoPmUtc));
        Assert.Equal("November 01, 2026, 2:30 PM",
            SpeechTimeFormatter.Format(HalfPastTwoPmUtc, SpeechTimeFormatter.DateTimeFormat));
        Assert.Equal("2:30 PM", SpeechTimeFormatter.FormatBarClock(HalfPastTwoPmUtc, 3600));
        Assert.Equal("9:05 AM", SpeechTimeFormatter.FormatTime(new DateTime(2026, 11, 1, 9, 5, 0, DateTimeKind.Utc)));
        Assert.Equal("12:00 AM", SpeechTimeFormatter.FormatTime(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)));
        // The hour landmark playback speaks, and the chart's axis format.
        Assert.Equal("3:00 PM", PlaybackNarration.Landmark(HalfPastTwoPmUtc, HalfPastTwoPmUtc.AddMinutes(30),
            PlaybackNarration.LandmarkUnit.Hour, 1800));
        Assert.Equal("h:mm tt", SpeechTimeFormatter.Clock(ChartMath.XAxisFormat(TimeSpan.FromHours(6)).Format));
        // A date has no hour and is left alone.
        Assert.Equal(SpeechTimeFormatter.LongDateFormat, SpeechTimeFormatter.Clock(SpeechTimeFormatter.LongDateFormat));
    }

    [Fact]
    public void Startup_reads_both_switches_from_settings()
    {
        var settings = new AppSettings(new DictSettings()) { TimesInUtc = true, TwelveHourClock = true };

        new PreferencePersistenceService(new MockWorkspaceStore(), settings,
            NullLogger<PreferencePersistenceService>.Instance).Initialize();

        Assert.True(SpeechTimeFormatter.UseUtc);
        Assert.True(SpeechTimeFormatter.TwelveHour);
    }

    [Fact]
    public void Both_switches_default_off_and_persist_under_their_keys()
    {
        var dict = new DictSettings();
        var settings = new AppSettings(dict);
        Assert.False(settings.TimesInUtc);
        Assert.False(settings.TwelveHourClock);

        settings.TimesInUtc = true;
        settings.TwelveHourClock = true;

        Assert.True(dict.Store[SettingsKeys.TimesInUtc].Value<bool>());
        Assert.True(dict.Store[SettingsKeys.TwelveHourClock].Value<bool>());
    }

    private sealed class DictSettings : ISettingsManager
    {
        public readonly Dictionary<string, JToken> Store = new(StringComparer.Ordinal);
        public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
            => Store.TryGetValue(keyPath, out var v) ? v : defaultValue;
        public void SetSetting(string keyPath, JToken value) => Store[keyPath] = value;
        public JObject GetEffectiveSettingsForSeries(string seriesId) => new();
        public void SaveSettings() { }
        public void ResetToDefaults() { }
        public void Reload() { }
    }
}

using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Theming;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// THE FACTORY RESET, and mostly what it refuses to touch.
///
/// <para>
/// Cody, 2026-09-04: <i>"general tab, have a reset all terminal settings to defaults with a
/// confirmation"</i>. The destructive half is easy; the half worth testing is the boundary,
/// because a settings reset that also took a user's API keys is not recoverable from this
/// machine and a user who suspects it might would never press the button.
/// </para>
/// </summary>
public class TerminalResetTests
{
    private sealed class Fixture
    {
        public ISettingsManager Settings { get; } = Substitute.For<ISettingsManager>();
        public IShortcutManager Shortcuts { get; } = Substitute.For<IShortcutManager>();
        public IThemeLibrary Themes { get; } = Substitute.For<IThemeLibrary>();
        public ISoundPatchLibrary Patches { get; } = Substitute.For<ISoundPatchLibrary>();
        public IIndicatorPreferencesService Prefs { get; } = Substitute.For<IIndicatorPreferencesService>();

        public TerminalResetService Build()
        {
            Themes.All.Returns(new List<ThemePreset>());
            return new TerminalResetService(Settings, Shortcuts, Themes, Patches, Prefs);
        }
    }

    [Fact]
    public void ResetEverything_ReachesEverySubsystemItClaimsTo()
    {
        var f = new Fixture();
        int failures = f.Build().ResetEverything();

        Assert.Equal(0, failures);
        f.Settings.Received(1).ResetToDefaults();
        f.Shortcuts.Received(1).ResetToDefaults();
        f.Patches.Received(1).ResetToDefaults();
        f.Prefs.Received(1).ClearAllPreferences();
        f.Themes.Received(1).Save();
    }

    [Fact]
    public void EveryUserThemeIsRemoved()
    {
        var f = new Fixture();
        f.Themes.All.Returns(new List<ThemePreset>
        {
            new("a", "Mine",      ThemeType.Blackout, new Dictionary<string, string?>()),
            new("b", "Also mine", ThemeType.Blackout, new Dictionary<string, string?>()),
        });

        new TerminalResetService(f.Settings, f.Shortcuts, f.Themes, f.Patches, f.Prefs).ResetEverything();

        f.Themes.Received(1).Remove("a");
        f.Themes.Received(1).Remove("b");
    }

    [Fact]
    public void OneSubsystemThrowing_DoesNotStopTheRest_AndIsCounted()
    {
        // A half-reset that stops at the first exception is the worst outcome available: the
        // user is left with a keyboard from one era and preferences from another and no way to
        // tell which. Everything is attempted; the count is what the dialog announces, and it is
        // why the dialog does not say "done" unconditionally.
        var f = new Fixture();
        f.Settings.When(s => s.ResetToDefaults()).Do(_ => throw new IOException("locked"));

        int failures = f.Build().ResetEverything();

        Assert.Equal(1, failures);
        f.Shortcuts.Received(1).ResetToDefaults();
        f.Patches.Received(1).ResetToDefaults();
        f.Prefs.Received(1).ClearAllPreferences();
    }

    [Fact]
    public void TheConfirmationNamesWhatSurvives_NotOnlyWhatGoes()
    {
        // "All personalization will be lost" is the sentence a user is most likely to read as
        // "including my API keys". Being wrong about that in the frightening direction stops
        // people using a button they need, so the survivors are part of the question.
        var svc = new Fixture().Build();

        Assert.Contains(svc.WhatSurvives, s => s.Contains("API keys"));
        Assert.Contains(svc.WhatSurvives, s => s.Contains("paper"));
        Assert.Contains(svc.WhatSurvives, s => s.Contains("workspace"));
        Assert.NotEmpty(svc.WhatIsErased);

        // Nothing may appear on both lists — the pair is read out as one sentence, and an item
        // claimed as both erased and kept is worse than saying nothing about it.
        Assert.Empty(svc.WhatIsErased.Intersect(svc.WhatSurvives, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void SettingsManager_ResetToDefaults_ClearsTheInMemoryDocumentAndWritesIt()
    {
        // The in-memory half is the whole reason this is not a file delete in the caller.
        // SettingsManager caches the JObject for the life of the process: clearing only the file
        // would leave the old values answering every read, and the next SaveSettings — triggered
        // by any unrelated preference change — would write them straight back.
        string dir = TestTemp.NewDir("reset-settings");
        try
        {
            var sm = new SettingsManager(new FixedPaths(dir),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsManager>.Instance);
            sm.SetSetting("speech.speakTimestamps", Newtonsoft.Json.Linq.JToken.FromObject(false));
            sm.SaveSettings();
            Assert.False(sm.GetSetting("speech.speakTimestamps")!.ToObject<bool>());

            sm.ResetToDefaults();

            Assert.Null(sm.GetSetting("speech.speakTimestamps"));

            // And it survives a reload — proving the FILE was rewritten, not just the cache.
            var reloaded = new SettingsManager(new FixedPaths(dir),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsManager>.Instance);
            Assert.Null(reloaded.GetSetting("speech.speakTimestamps"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private sealed class FixedPaths : IPlatformPathService
    {
        public FixedPaths(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }
}

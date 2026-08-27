using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase E test-debt for ShortcutManager: default-profile bindings, key+modifier
    /// resolution (case-insensitive), user rebinds overriding defaults with conflict
    /// eviction, persistence round-trip through the injectable path service, and
    /// corrupt-file fallback to defaults.
    /// </summary>
    public class ShortcutManagerTests : IDisposable
    {
        private sealed class TempPathService : IPlatformPathService
        {
            public TempPathService(string root) { AppDataDirectory = root; CacheDirectory = root; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private readonly string _dir;
        private readonly TempPathService _paths;

        public ShortcutManagerTests()
        {
            _dir = TestTemp.NewDir("att-shortcut-tests-");
            _paths = new TempPathService(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string ShortcutsFile => Path.Combine(_dir, "shortcuts.json");

        [Fact]
        public void DefaultProfile_ContainsTheCoreBindings()
        {
            var mgr = new ShortcutManager(_paths);

            Assert.Equal("Default", mgr.CurrentProfile.Name);
            Assert.NotEmpty(mgr.CurrentProfile.Shortcuts);
            // Spot-check the pillars a screen-reader user relies on from minute one.
            Assert.Equal(SystemCommand.OpenHelp, mgr.GetCommand("F1", false, false, false));
            Assert.Equal(SystemCommand.OpenSettings, mgr.GetCommand("F12", false, false, false));
            Assert.Equal(SystemCommand.NavLeft, mgr.GetCommand("LEFT", false, false, false));
            Assert.Equal(SystemCommand.PlayChart, mgr.GetCommand("SPACE", false, false, false));
            Assert.Equal(SystemCommand.ToggleSpeech, mgr.GetCommand("F2", false, false, false));
        }

        [Fact]
        public void GetCommand_IsCaseInsensitiveOnTheKeyName()
        {
            // keyboard.js may report "ArrowLeft"/"arrowleft" depending on the source;
            // the lookup key is upper-cased on both sides so casing never matters.
            var mgr = new ShortcutManager(_paths);

            Assert.Equal(SystemCommand.NavLeft, mgr.GetCommand("arrowleft", false, false, false));
            Assert.Equal(SystemCommand.OpenHelp, mgr.GetCommand("f1", false, false, false));
        }

        [Fact]
        public void Modifiers_DisambiguateTheSameBaseKey()
        {
            var mgr = new ShortcutManager(_paths);

            // Space family: plain = chart toggle, Ctrl = pause, Shift = series scope.
            Assert.Equal(SystemCommand.PlayChart, mgr.GetCommand("SPACE", false, false, false));
            Assert.Equal(SystemCommand.PlayPause, mgr.GetCommand("SPACE", false, true, false));
            Assert.Equal(SystemCommand.PlaySeries, mgr.GetCommand("SPACE", true, false, false));
            // F12: plain = settings, Shift = properties (the Phase 5 F-key exception).
            Assert.Equal(SystemCommand.OpenProperties, mgr.GetCommand("F12", true, false, false));
        }

        [Fact]
        public void UnknownCombination_ReturnsNone()
        {
            var mgr = new ShortcutManager(_paths);

            Assert.Equal(SystemCommand.None, mgr.GetCommand("F1", true, true, true));
            Assert.Equal(SystemCommand.None, mgr.GetCommand("Q", false, false, false));
        }

        [Fact]
        public void UpdateBinding_RebindsCommand_AndRemovesAllItsOldBindings()
        {
            var mgr = new ShortcutManager(_paths);

            mgr.UpdateBinding(SystemCommand.OpenHelp, "F9");

            Assert.Equal(SystemCommand.OpenHelp, mgr.GetCommand("F9", false, false, false));
            // The old F1 binding is gone — a command has exactly one combo after a rebind.
            Assert.Equal(SystemCommand.None, mgr.GetCommand("F1", false, false, false));
        }

        [Fact]
        public void UpdateBinding_EvictsTheConflictingCommandFromTheCombo_AndReportsIt()
        {
            var mgr = new ShortcutManager(_paths);

            // F12 (unmodified) is OpenSettings by default. Rebinding OpenHelp onto it
            // must evict OpenSettings from that combo — one combo, one command.
            var displaced = mgr.UpdateBinding(SystemCommand.OpenHelp, "F12");

            Assert.Equal(SystemCommand.OpenHelp, mgr.GetCommand("F12", false, false, false));
            // OpenSettings now has NO binding at all — and UpdateBinding REPORTS that,
            // so the Settings UI can tell the user rather than leaving it silent.
            Assert.DoesNotContain(mgr.CurrentProfile.Shortcuts,
                s => s.Command == SystemCommand.OpenSettings);
            Assert.Contains(SystemCommand.OpenSettings, displaced);
            // Shift+F12 (OpenProperties) is a different combo and must be untouched.
            Assert.Equal(SystemCommand.OpenProperties, mgr.GetCommand("F12", true, false, false));
        }

        [Fact]
        public void UpdateBinding_DoesNotReport_ACommandThatStillHasAnotherBinding()
        {
            var mgr = new ShortcutManager(_paths);

            // JumpToLatest genuinely has two default bindings — "OEM5" and "\\" — which are DIFFERENT
            // keys that normalise to different lookups. Stealing one leaves the other, so it must
            // not be reported as stranded.
            //
            // This test used to use NavLeft's "LEFT" and "ARROWLEFT" pair, which was not a real
            // example: the normaliser rewrites ARROWLEFT to LEFT, so those were one binding written
            // twice and the second could never fire. The duplicates have been removed.
            var displaced = mgr.UpdateBinding(SystemCommand.OpenHelp, "OEM5");

            Assert.DoesNotContain(SystemCommand.JumpToLatest, displaced);
            Assert.Equal(SystemCommand.JumpToLatest, mgr.GetCommand("\\", false, false, false));
        }

        [Fact]
        public void UpdateBinding_ToAFreeCombo_ReportsNothingDisplaced()
        {
            var mgr = new ShortcutManager(_paths);
            var displaced = mgr.UpdateBinding(SystemCommand.OpenHelp, "F9", ctrl: true, alt: true, shift: true);
            Assert.Empty(displaced);
        }

        [Fact]
        public void UpdateBinding_PersistsImmediately_AndSurvivesReload()
        {
            var first = new ShortcutManager(_paths);
            first.UpdateBinding(SystemCommand.OpenHelp, "F9", ctrl: true);

            Assert.True(File.Exists(ShortcutsFile), "UpdateBinding must save to disk");

            // A brand-new manager (fresh app start) loads the user profile over defaults.
            var second = new ShortcutManager(_paths);
            Assert.Equal(SystemCommand.OpenHelp, second.GetCommand("F9", false, true, false));
            Assert.Equal(SystemCommand.None, second.GetCommand("F1", false, false, false));
        }

        [Fact]
        public void CorruptShortcutsFile_FallsBackToDefaults_WithoutThrowing()
        {
            File.WriteAllText(ShortcutsFile, "{ not valid json ///");

            // A corrupted file must never prevent startup; defaults stay active.
            var mgr = new ShortcutManager(_paths);

            Assert.Equal(SystemCommand.OpenHelp, mgr.GetCommand("F1", false, false, false));
            Assert.NotEmpty(mgr.CurrentProfile.Shortcuts);
        }

        [Fact]
        public void GetAllBindings_FormatsModifierChords_ForTheShortcutHelpList()
        {
            var mgr = new ShortcutManager(_paths);

            var bindings = mgr.GetAllBindings();

            // Ctrl+Alt+Shift+C (ChartFocus) renders in Ctrl,Alt,Shift order — this is
            // the string spoken in the shortcut help modal, so the order is a UX contract.
            Assert.Contains(bindings, b =>
                b.Description == nameof(SystemCommand.ChartFocus) &&
                b.DisplayString == "Ctrl+Alt+Shift+C");
        }
    }
}

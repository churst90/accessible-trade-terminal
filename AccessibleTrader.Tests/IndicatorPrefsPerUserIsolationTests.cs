using System.Collections.Generic;
using System.IO;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Indicator preferences are user state and must follow the same per-user routing as
    /// workspaces, settings, strategies and paper accounts.
    ///
    /// <para>
    /// <see cref="IndicatorPreferencesService"/> was the second service found building its own
    /// path from <c>Environment.GetFolderPath(LocalApplicationData)</c> instead of taking
    /// <see cref="IPlatformPathService"/> — the same defect
    /// <see cref="WorkspacePerUserIsolationTests"/> covers for workspaces, and with the same two
    /// consequences: every hosted account shared one set of preferences (restyling an indicator
    /// changed it for everyone), and on Unix the path could resolve RELATIVE and land in the
    /// deployment directory that a redeploy replaces.
    /// </para>
    /// </summary>
    public sealed class IndicatorPrefsPerUserIsolationTests
    {
        private static IndicatorPreferencesService Make(IPlatformPathService paths) =>
            new(NullLogger<IndicatorPreferencesService>.Instance, paths);

        private static List<ComponentPreference> Prefs(string color) =>
            new() { new ComponentPreference { Name = "Line", ColorHex = color } };

        [Fact]
        public void TwoUsers_DoNotShareIndicatorPreferences()
        {
            var alice = new TempWorkspacePaths();
            var bob = new TempWorkspacePaths();

            Make(alice).SavePreferences("RSI", Prefs("#ff0000"));

            // Bob has never styled RSI, so he must still be on defaults — not looking at Alice's.
            Assert.Null(Make(bob).GetPreferences("RSI"));
            Assert.Equal("#ff0000", Make(alice).GetPreferences("RSI")![0].ColorHex);
        }

        [Fact]
        public void OneUsersRestyle_DoesNotOverwriteAnothers()
        {
            var alice = new TempWorkspacePaths();
            var bob = new TempWorkspacePaths();

            Make(alice).SavePreferences("MACD", Prefs("#ff0000"));
            Make(bob).SavePreferences("MACD", Prefs("#00ff00"));

            Assert.Equal("#ff0000", Make(alice).GetPreferences("MACD")![0].ColorHex);
            Assert.Equal("#00ff00", Make(bob).GetPreferences("MACD")![0].ColorHex);
        }

        [Fact]
        public void PreferencesLiveUnderTheProvidedAppDataDirectory()
        {
            var paths = new TempWorkspacePaths();
            Make(paths).SavePreferences("ATR", Prefs("#123456"));

            // Desktop keeps its existing location because AppDataDirectory is
            // ~/.local/share/AccessibleTrader there; hosted gets users/<id>/IndicatorPrefs.
            var dir = Path.Combine(paths.AppDataDirectory, "IndicatorPrefs");
            Assert.True(Directory.Exists(dir), $"expected the prefs directory at {dir}");
            Assert.NotEmpty(Directory.GetFiles(dir, "*.json"));
        }
    }
}

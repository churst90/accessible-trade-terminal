using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins the WebHost browser-shortcut remap. Firefox/Chrome reserve a set of
    /// chords at the chrome level that page-level <c>preventDefault</c> cannot stop,
    /// so on the WebHost they must be rebound to web-safe equivalents:
    /// <list type="bullet">
    /// <item>Ctrl+Shift+letter (drawing tools) → Alt+Shift+letter.</item>
    /// <item>Ctrl+T / Ctrl+W / Ctrl+Tab / Ctrl+Shift+Tab (browser tab management) → removed;
    /// AddTab still answers to Alt+Shift+N and switching goes through Ctrl+Alt+Shift+T.</item>
    /// <item>Ctrl+PageUp / Ctrl+PageDown (browser tab cycling) → nothing to remap: pane
    /// navigation is Alt+PageUp / Alt+PageDown on every head, and Ctrl+PageUp/Down is left
    /// unbound rather than reassigned.</item>
    /// </list>
    /// </summary>
    public class WebHostShortcutRemapTests
    {
        private static ShortcutManager FreshManager()
        {
            // Point at an empty temp dir so no on-disk profile overrides the defaults.
            var path = Substitute.For<IPlatformPathService>();
            path.AppDataDirectory.Returns(TestTemp.NewDir("att-remap-"));
            return new ShortcutManager(path);
        }

        [Fact]
        public void Remap_DropsReservedTabChords_AndKeepsWebSafeAlternatives()
        {
            var sm = FreshManager();
            // Sanity: defaults bind the reserved chords before the remap runs.
            Assert.Equal(SystemCommand.AddTab, sm.GetCommand("T", shift: false, ctrl: true, alt: false));
            Assert.Equal(SystemCommand.SwitchTabNext, sm.GetCommand("TAB", shift: false, ctrl: true, alt: false));

            WebHostShortcutRemap.ApplyBrowserHostOverrides(sm, NullLogger.Instance);

            // Reserved single-Ctrl chrome chords are gone…
            Assert.Equal(SystemCommand.None, sm.GetCommand("T", shift: false, ctrl: true, alt: false));   // Ctrl+T
            Assert.Equal(SystemCommand.None, sm.GetCommand("W", shift: false, ctrl: true, alt: false));   // Ctrl+W
            Assert.Equal(SystemCommand.None, sm.GetCommand("TAB", shift: false, ctrl: true, alt: false)); // Ctrl+Tab
            Assert.Equal(SystemCommand.None, sm.GetCommand("TAB", shift: true, ctrl: true, alt: false));  // Ctrl+Shift+Tab

            // …and the web-safe alternatives still resolve.
            Assert.Equal(SystemCommand.AddTab, sm.GetCommand("N", shift: true, ctrl: false, alt: true));  // Alt+Shift+N
            Assert.Equal(SystemCommand.FocusTabBar, sm.GetCommand("T", shift: true, ctrl: true, alt: true)); // Ctrl+Alt+Shift+T
        }

        [Fact]
        public void PaneNavigation_IsAltPageKeys_OnEveryHead_AndCtrlPageKeysAreUnbound()
        {
            // The remap used to move these; it no longer has to, because the DEFAULT profile
            // binds them where the browser leaves them alone. That difference is worth pinning:
            // a desktop head and a browser head disagreeing about a navigation key is a thing
            // the Help dialog has to explain and a user has to remember, for no gain.
            //
            // Ctrl+PageUp/PageDown stays UNBOUND rather than being handed to something else.
            // Chrome, Brave, Edge and Firefox all cycle their own tabs on it, ahead of any
            // page-level listener, so a binding there is a binding that sometimes vanishes.
            var sm = FreshManager();
            Assert.Equal(SystemCommand.NavPaneNext, sm.GetCommand("PAGEDOWN", shift: false, ctrl: false, alt: true));
            Assert.Equal(SystemCommand.NavPanePrev, sm.GetCommand("PAGEUP", shift: false, ctrl: false, alt: true));
            Assert.Equal(SystemCommand.None, sm.GetCommand("PAGEDOWN", shift: false, ctrl: true, alt: false));
            Assert.Equal(SystemCommand.None, sm.GetCommand("PAGEUP", shift: false, ctrl: true, alt: false));

            WebHostShortcutRemap.ApplyBrowserHostOverrides(sm, NullLogger.Instance);

            Assert.Equal(SystemCommand.NavPaneNext, sm.GetCommand("PAGEDOWN", shift: false, ctrl: false, alt: true));
            Assert.Equal(SystemCommand.NavPanePrev, sm.GetCommand("PAGEUP", shift: false, ctrl: false, alt: true));
            Assert.Equal(SystemCommand.None, sm.GetCommand("PAGEDOWN", shift: false, ctrl: true, alt: false));
            Assert.Equal(SystemCommand.None, sm.GetCommand("PAGEUP", shift: false, ctrl: true, alt: false));
        }

        [Fact]
        public void Remap_StillConvertsDrawingTools_ToAltShiftLetter()
        {
            var sm = FreshManager();
            WebHostShortcutRemap.ApplyBrowserHostOverrides(sm, NullLogger.Instance);

            // Ctrl+Shift+T (DrawTrend) → Alt+Shift+T; the Ctrl+Shift form is gone.
            Assert.Equal(SystemCommand.DrawTrend, sm.GetCommand("T", shift: true, ctrl: false, alt: true));
            Assert.Equal(SystemCommand.None, sm.GetCommand("T", shift: true, ctrl: true, alt: false));
        }
    }
}

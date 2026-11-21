using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// The one place a binding becomes words a screen reader can voice. The chart menu reads the
/// live profile through this, so a rebind is reflected; the default-profile chords are pinned
/// here rather than in the menu's markup.
/// </summary>
public sealed class ShortcutWordsTests
{
    private static (ShortcutManager Manager, string Dir) NewManager()
    {
        string dir = TestTemp.NewDir("att-shortcut-words-");
        return (new ShortcutManager(new Paths(dir)), dir);
    }

    private sealed class Paths : IPlatformPathService
    {
        public Paths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    [Fact]
    public void Default_letter_chords_are_spelled_in_words()
    {
        var (manager, dir) = NewManager();
        try
        {
            Assert.Equal("Alt plus Shift plus L", ShortcutWords.ForCommand(manager, SystemCommand.DrawLabel));
            Assert.Equal("Alt plus Shift plus H", ShortcutWords.ForCommand(manager, SystemCommand.DrawHorizontal));
            Assert.Equal("Alt plus O", ShortcutWords.ForCommand(manager, SystemCommand.OpenObjectTree));
            Assert.Equal("Alt plus A", ShortcutWords.ForCommand(manager, SystemCommand.OpenIndicators));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void A_command_with_two_bindings_names_the_portable_chord_not_the_function_key()
    {
        var (manager, dir) = NewManager();
        try
        {
            // Replay is F11 AND Ctrl+Alt+Shift+P. F11 is a browser-owned key on the web, and
            // the toolbar/manual already name the portable chord.
            Assert.Equal("Control plus Alt plus Shift plus P",
                ShortcutWords.ForCommand(manager, SystemCommand.ReplayToggle));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void Rebinding_the_command_changes_the_words()
    {
        var (manager, dir) = NewManager();
        try
        {
            manager.UpdateBinding(SystemCommand.DrawLabel, "J", ctrl: true, alt: true);

            Assert.Equal("Control plus Alt plus J",
                ShortcutWords.ForCommand(manager, SystemCommand.DrawLabel));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void An_unbound_command_says_none()
    {
        var manager = new ShortcutManager(new Paths(TestTemp.NewDir("att-shortcut-words-")));
        Assert.Null(ShortcutWords.ForCommand(manager, SystemCommand.SelectNextSeries));
    }

    // The Settings keyboard tab listed "ZoomIn" beside "Shift+OEMMINUS" and "OEM5" until
    // 2026-11-21: the enum's spelling and the browser's key code, not anything a person says.
    [Fact]
    public void The_keyboard_settings_show_every_shortcut_in_words()
    {
        var dir = TestTemp.NewDir("att-shortcut-words-");
        try
        {
            var bindings = new ShortcutManager(new Paths(dir)).GetAllBindings();
            Assert.True(bindings.Count > 50, $"expected the full default profile, got {bindings.Count}");

            var raw = bindings.Where(b => b.DisplayString.Contains("OEM", StringComparison.OrdinalIgnoreCase)
                                       || b.DisplayString.Split('+').Any(k => k.Length > 1 && k == k.ToUpperInvariant() && !k.StartsWith('F')))
                              .Select(b => $"{b.Description}: {b.DisplayString}").ToList();
            Assert.True(raw.Count == 0, "key codes shown as key names:\n" + string.Join("\n", raw));

            var camel = bindings.Select(b => ShortcutWords.ActionName(b.Description))
                                .Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, "[a-z][A-Z]"))
                                .ToList();
            Assert.True(camel.Count == 0, "action names still in code spelling:\n" + string.Join("\n", camel));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void An_action_is_named_as_the_Go_bar_names_it()
    {
        Assert.Equal(AccessibleTrader.Core.Services.GoBar.GoBarRegistry.All.First(e => e.Command == SystemCommand.ZoomIn).Name,
            ShortcutWords.ActionName(nameof(SystemCommand.ZoomIn)));
        Assert.Equal("Not Bound Anywhere", ShortcutWords.ActionName("NotBoundAnywhere"));
    }
}

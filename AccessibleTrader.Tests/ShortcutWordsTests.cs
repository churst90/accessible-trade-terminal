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
}

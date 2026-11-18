using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// Apostrophe and Shift+apostrophe reach the two jump commands by every spelling a keyboard or
/// keyboard.js produces, and no other default binding occupies them.
/// </summary>
public class MarketEventKeyBindingTests
{
    private sealed class TempPaths : IPlatformPathService
    {
        public string AppDataDirectory { get; } = TestTemp.NewDir("att-market-event-keys-");
        public string CacheDirectory => AppDataDirectory;
    }

    private static ShortcutManager Fresh() => new(new TempPaths());

    [Theory]
    [InlineData("'", false, SystemCommand.NavEventNext)]
    [InlineData("OEM7", false, SystemCommand.NavEventNext)]
    [InlineData("\"", true, SystemCommand.NavEventPrev)]
    [InlineData("OEM7", true, SystemCommand.NavEventPrev)]
    public void BothSpellingsOfApostropheReachTheJumpCommands(string key, bool shift, SystemCommand expected)
        => Assert.Equal(expected, Fresh().GetCommand(key, shift, ctrl: false, alt: false));

    [Fact]
    public void NoOtherDefaultBindingUsesApostrophe()
    {
        var apostropheBindings = Fresh().CurrentProfile.Shortcuts
            .Where(s => s.Key == "'" || s.Key == "OEM7")
            .ToList();

        Assert.Equal(4, apostropheBindings.Count);
        Assert.All(apostropheBindings, s =>
            Assert.True(s.Command is SystemCommand.NavEventNext or SystemCommand.NavEventPrev,
                $"apostrophe is also bound to {s.Command}"));
        Assert.Contains(apostropheBindings, s => s.Command == SystemCommand.NavEventNext && !s.Shift);
        Assert.Contains(apostropheBindings, s => s.Command == SystemCommand.NavEventPrev && s.Shift);
    }
}

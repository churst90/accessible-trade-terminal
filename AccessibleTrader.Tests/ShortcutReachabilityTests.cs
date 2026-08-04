using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Every default shortcut must be reachable by pressing keys on a keyboard.
///
/// <para>
/// <b>Why this is not the same as the conflict test.</b> <c>ShortcutConflictTests</c> proves no two
/// commands claim the same combination. It says nothing about whether a combination can be
/// <i>produced</i>. A binding can be unique, documented, and completely dead.
/// </para>
///
/// <para>
/// <b>Two real failures this pins.</b> Found by a maintainer testing paper trading — the entire quick
/// trade feature was unreachable except for two keys, and the whole 2,875-test suite was green:
/// </para>
///
/// <list type="number">
///   <item>
///     <b>Shifted digits.</b> A browser reports <c>event.key</c> as the character produced, so
///     Shift+1 arrives as <c>"!"</c>, never <c>"1"</c>. <c>Ctrl+Alt+Shift+1/2/3</c> (arm 0.5/1/2%)
///     and <c>Ctrl+Alt+Shift+0</c> (disarm) matched nothing. <c>Ctrl+Alt+Shift+X</c> worked, because
///     Shift+X is still "X" — which is exactly why the feature looked half-working rather than
///     broken.
///   </item>
///   <item>
///     <b>Two spellings of one key.</b> Bindings were stored verbatim while incoming keys were
///     normalised, and the normaliser rewrites <c>"ENTER"</c> to <c>"RETURN"</c>. The only two
///     bindings in the file spelled <c>"ENTER"</c> were Shift+Enter and Ctrl+Enter — the keys that
///     place the order.
///   </item>
/// </list>
///
/// <para>
/// Both are the same defect in different clothes: the binding side and the keypress side were
/// compared without passing through the same function.
/// </para>
/// </summary>
public class ShortcutReachabilityTests
{
    private sealed class TempPaths : IPlatformPathService
    {
        public string AppDataDirectory { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "att-shortcut-" + Guid.NewGuid().ToString("N"));
        public string CacheDirectory => AppDataDirectory;
        public TempPaths() => System.IO.Directory.CreateDirectory(AppDataDirectory);
    }

    private static ShortcutManager Fresh() => new(new TempPaths());

    /// <summary>
    /// What a US-layout browser actually puts in <c>event.key</c> when the binding's key is pressed
    /// with the binding's modifiers. This is the bridge the production code was missing.
    /// </summary>
    private static string BrowserKeyFor(string bindingKey, bool shift)
    {
        if (!shift || bindingKey.Length != 1) return bindingKey;

        const string unshifted = "1234567890";
        const string shifted   = "!@#$%^&*()";
        int i = unshifted.IndexOf(bindingKey[0]);
        return i >= 0 ? shifted[i].ToString() : bindingKey;
    }

    [Fact]
    public void EveryDefaultBindingResolvesFromTheKeyABrowserWouldSend()
    {
        var mgr = Fresh();
        var dead = new List<string>();

        foreach (var b in mgr.CurrentProfile.Shortcuts)
        {
            string browserKey = BrowserKeyFor(b.Key, b.Shift);
            var resolved = mgr.GetCommand(browserKey, b.Shift, b.Ctrl, b.Alt);

            if (resolved != b.Command)
                dead.Add($"{b.Command}: bound to '{b.Key}' (shift={b.Shift} ctrl={b.Ctrl} alt={b.Alt}); "
                       + $"a keyboard sends '{browserKey}' which resolves to {resolved}");
        }

        Assert.True(dead.Count == 0,
            "These shortcuts cannot be triggered by any keypress — they are documented, unique, and "
          + "dead:\n  " + string.Join("\n  ", dead));
    }

    /// <summary>
    /// The quick trade keys specifically, spelled out. This feature has no dialog: a keystroke sizes
    /// and sends a real order, so a dead key here means the feature does not exist.
    /// </summary>
    [Theory]
    [InlineData("!", true, true, true, SystemCommand.QuickArmRisk1)]
    [InlineData("@", true, true, true, SystemCommand.QuickArmRisk2)]
    [InlineData("#", true, true, true, SystemCommand.QuickArmRisk3)]
    [InlineData(")", true, true, true, SystemCommand.QuickDisarm)]
    [InlineData("X", true, true, true, SystemCommand.QuickSetStop)]
    [InlineData("Q", true, true, true, SystemCommand.QuickArmStatus)]
    public void TheQuickTradeChordsResolve(string browserKey, bool shift, bool ctrl, bool alt, SystemCommand expected)
        => Assert.Equal(expected, Fresh().GetCommand(browserKey, shift, ctrl, alt));

    /// <summary>
    /// Enter reaches the dispatcher under both spellings, because the two placement keys were
    /// declared as "ENTER" while every incoming Enter normalises to "RETURN".
    /// </summary>
    [Theory]
    [InlineData("Enter")]
    [InlineData("ENTER")]
    [InlineData("Return")]
    public void BothSpellingsOfEnterPlaceAQuickTrade(string spelling)
    {
        var mgr = Fresh();
        Assert.Equal(SystemCommand.QuickPlaceLimit,  mgr.GetCommand(spelling, shift: true,  ctrl: false, alt: false));
        Assert.Equal(SystemCommand.QuickPlaceMarket, mgr.GetCommand(spelling, shift: false, ctrl: true,  alt: false));
    }

    /// <summary>
    /// The unshifted digit must still resolve. On layouts where digits require Shift (AZERTY) the
    /// key arrives as "1" directly, and on any layout the numeric keypad sends an unshifted digit.
    /// </summary>
    [Fact]
    public void UnshiftedDigitsStillResolve()
        => Assert.Equal(SystemCommand.QuickArmRisk1, Fresh().GetCommand("1", shift: true, ctrl: true, alt: true));

    /// <summary>
    /// <c>event.code</c> is layout-independent, so accepting it means a future switch to code-based
    /// input needs no further mapping.
    /// </summary>
    [Theory]
    [InlineData("Digit1")]
    [InlineData("Numpad1")]
    public void KeyCodesResolveToo(string code)
        => Assert.Equal(SystemCommand.QuickArmRisk1, Fresh().GetCommand(code, shift: true, ctrl: true, alt: true));

    /// <summary>
    /// Guards the guard: if the profile stopped loading, every assertion above would pass over an
    /// empty list.
    /// </summary>
    [Fact]
    public void TheDefaultProfileIsPopulated()
        => Assert.True(Fresh().CurrentProfile.Shortcuts.Count > 100);
}

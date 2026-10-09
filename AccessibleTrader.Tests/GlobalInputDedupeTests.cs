using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// One keypress on the focused chart reaches .NET by two routes — keyboard.js's window listener
/// and ChartArea's <c>@onkeydown</c> fallback — and <see cref="GlobalInputService.TryProcessKey"/>
/// is what makes it one command. It used to compare the two spellings as strings, and the two
/// pipelines do not spell every key alike: keyboard.js sends <c>OEM1</c> for <c>;</c> and the
/// element path sent <c>;</c>. So semicolon ran the formation pin twice per press — forward and
/// straight back — and the user heard the same "1 of 2" every time (live report, 2026-10-09).
///
/// <para>
/// The browser half of this is <c>ChartFormationKeyBrowserTests</c>, which watches the real
/// listeners do it. This pins the dedupe's own rule on every key where the spellings differ.
/// </para>
/// </summary>
public sealed class GlobalInputDedupeTests
{
    /// <summary>
    /// (what keyboard.js sends, the raw <c>KeyboardEvent.key</c> the element path sees, shift).
    /// The left column is keyboard.js's <c>normalizeKeyName</c> output, upper-cased as the trap
    /// sends it; the right is run through <see cref="GlobalInputService.NormalizeKey"/> below,
    /// exactly as ChartArea does.
    /// </summary>
    public static TheoryData<string, string, bool> OnePressTwoSpellings => new()
    {
        { "OEM1", ";", false },     // semicolon: pin the next formation
        { "OEM1", ":", true },      // Shift+semicolon: clear the pin
        { "OEM2", "/", false },
        { "OEM2", "?", true },
        { "OEM4", "[", false },
        { "OEM6", "]", false },
        { "OEMMINUS", "-", false },
        { "OEMPLUS", "=", false },
        { ",", ",", false },
        { "LEFT", "ArrowLeft", false },
    };

    private static (GlobalInputService Sut, IInputService Input) Build()
    {
        var input = Substitute.For<IInputService>();
        return (new GlobalInputService(input, new SpyEventBus(), Substitute.For<IWorkspaceStore>()), input);
    }

    [Theory]
    [MemberData(nameof(OnePressTwoSpellings))]
    public void One_press_arriving_by_both_pipelines_is_processed_once(string fromJs, string rawKey, bool shift)
    {
        var (sut, input) = Build();

        sut.OnKeyDown(fromJs, shift, false, false);
        sut.TryProcessKey(GlobalInputService.NormalizeKey(rawKey), shift, false, false);

        input.ReceivedWithAnyArgs(1).ProcessKey(default!, default, default, default);
    }

    /// <summary>
    /// The vacuity floor: a dedupe that swallowed every second key would pass the theory above.
    /// Two DIFFERENT keys in quick succession are two presses.
    /// </summary>
    [Fact]
    public void Two_different_keys_are_both_processed()
    {
        var (sut, input) = Build();

        sut.OnKeyDown("OEM1", false, false, false);
        sut.TryProcessKey(GlobalInputService.NormalizeKey(","), false, false, false);

        input.ReceivedWithAnyArgs(2).ProcessKey(default!, default, default, default);
    }

    /// <summary>Semicolon and Shift+semicolon are different commands, not one press twice.</summary>
    [Fact]
    public void Semicolon_then_shift_semicolon_is_two_presses()
    {
        var (sut, input) = Build();

        sut.OnKeyDown("OEM1", false, false, false);
        sut.OnKeyDown("OEM1", true, false, false);

        input.ReceivedWithAnyArgs(2).ProcessKey(default!, default, default, default);
    }
}

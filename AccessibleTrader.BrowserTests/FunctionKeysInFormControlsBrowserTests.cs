using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Function keys must work from inside a text field (WCAG 2.1.1). <c>keyboard.js</c>'s
/// form-control guard returned for every unmodified key in an input, textarea or select except
/// Escape, so F1 in Settings' search box opened nothing (which is why the stacked-modal route
/// Tabs past it), F2 could not mute while typing, and F12 opened the browser's DevTools from the
/// toolbar's own selects — the exact controls the handler's comment promises F-keys work from.
/// Both theories were red on the tree before the guard let <c>/^F\d{1,2}$/</c> through.
/// </summary>
[Collection("Terminal browser")]
public sealed class FunctionKeysInFormControlsBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public FunctionKeysInFormControlsBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private const string SettingsTitle = "settings-title";
    private const string SettingsSearch = "s-search";
    private const string HelpTitle = "help-title";
    private const string MarketSelect = "market-select";

    [BrowserFact]
    public async Task F1_pressed_inside_Settings_search_box_opens_Help()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.PressAsync("F12");
        Assert.True(await t.WaitForFocusAsync(SettingsTitle), "Settings did not open on F12.");
        await t.PressAsync("Tab");
        Assert.True(await t.WaitForFocusAsync(SettingsSearch),
            $"One Tab from Settings' heading should land in the search box; focus is on {(await t.ActiveElementAsync()).Describe()}.");

        await t.PressAsync("F1");

        Assert.True(await t.WaitForFocusAsync(HelpTitle),
            $"F1 inside Settings' search box opened nothing; focus is on {(await t.ActiveElementAsync()).Describe()}. " +
            "The keydown handler's form-control guard swallowed the function key.");
        Assert.Equal(2, (await t.VisibleDialogIdsAsync()).Count);
    }

    [BrowserFact]
    public async Task F1_pressed_on_the_toolbar_market_select_opens_Help()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.FocusAsync($"#{MarketSelect}");
        Assert.True(await t.WaitForFocusAsync(MarketSelect),
            $"Could not put focus on the toolbar's market select; focus is on {(await t.ActiveElementAsync()).Describe()}.");

        await t.PressAsync("F1");

        Assert.True(await t.WaitForFocusAsync(HelpTitle),
            $"F1 on the toolbar's market select opened nothing; focus is on {(await t.ActiveElementAsync()).Describe()}. " +
            "The keydown handler's form-control guard swallowed the function key.");
    }
}

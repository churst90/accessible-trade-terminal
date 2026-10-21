using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// <b>A symbol list too long to render whole is searched, not dumped into the dropdown.</b>
/// Alpaca's US stock list is 14,402 symbols. The toolbar rendered an option for every one, so
/// arriving on DIA (the first Alpaca stock tab, after two Bitstamp tabs) put fourteen thousand
/// options into the accessibility tree at once and Orca stopped talking until it caught up
/// (Cody, 2026-10-21, the Camel cycles workspace). See <see cref="SymbolChoices"/>.
/// </summary>
public class LongSymbolListTests
{
    /// <summary>The size of Alpaca's real list, with DIA, SPY, GLD and TLT among them.</summary>
    private static List<string> StockList()
    {
        var list = new List<string> { "DIA", "GLD", "SPY", "SPYG", "SPYV", "TLT", "XSPY" };
        for (int i = 0; list.Count < 14_402; i++) list.Add($"S{i:D5}");
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static BlazorTestHarness Harness(List<string> symbols, string selected)
    {
        var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Stock|Spot", "Alpaca", selected, "1d"),
            CurrentDataShape = ProviderDataShape.Ohlcv,
        });
        h.MarketOrchestrator.AvailableSymbols.Returns(symbols);
        h.MarketOrchestrator.SelectedSymbol.Returns(selected);
        return h;
    }

    [Fact]
    public void Alpacas_stock_list_does_not_render_an_option_per_symbol()
    {
        using var h = Harness(StockList(), "DIA");

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#symbol-select")));
        var options = cut.FindAll("#symbol-select option");
        Assert.InRange(options.Count, 1, SymbolChoices.MatchLimit + 1);
        // The dropdown still holds, and shows, the chart's own symbol.
        Assert.Contains(options, o => o.GetAttribute("value") == "DIA" && o.HasAttribute("selected"));
        Assert.NotNull(cut.Find("#symbol-find"));
    }

    [Fact]
    public void Typing_in_Find_puts_the_matches_in_the_dropdown_exact_match_first()
    {
        using var h = Harness(StockList(), "DIA");
        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        cut.Find("#symbol-find").Input("spy");

        var values = cut.FindAll("#symbol-select option").Select(o => o.GetAttribute("value")).ToList();
        // The current symbol leads (a native select must hold its value), then SPY exactly,
        // then the symbols starting with it, then the one containing it.
        Assert.Equal(new[] { "DIA", "SPY", "SPYG", "SPYV", "XSPY" }, values);
        Assert.Contains("4 matches", cut.Find("#symbol-find-help").TextContent);
    }

    [Fact]
    public void A_short_list_renders_whole_with_no_Find_box()
    {
        // Bitstamp's real list is 256: nothing about it changes.
        var pairs = Enumerable.Range(0, 256).Select(i => $"P{i:D3}USD").ToList();
        using var h = Harness(pairs, "P010USD");

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        cut.WaitForAssertion(() => Assert.Equal(256, cut.FindAll("#symbol-select option").Count));
        Assert.Empty(cut.FindAll("#symbol-find"));
    }
}

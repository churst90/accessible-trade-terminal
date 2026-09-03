// WCAG 2.5.3 Label in Name, asked of the RENDERED DOM rather than of the markup.
//
// DismissControlNameScanTests already carries this rule and is the authority on the
// branches a cold-open render never reaches. It has two blind spots the 2026-09-01 audit
// named as gates 2 and 3 of its "ten gates that assert the wrong thing", and both are
// properties of reading source rather than of the rule:
//
//   * its regex is `<button\b…`, and `<ToolbarIconButton />` is a COMPONENT TAG. All ~34
//     call sites — the entire main toolbar and the indicator bar, which is to say every
//     control a user meets before opening anything — were invisible to it.
//   * it filters with `.Where(!c.VisibleText.Contains('@'))`, which drops every dynamic
//     label. `@(x ? "Hide" : "Show")` is dynamic AND generic: exactly the case the rule
//     exists for.
//
// Rendering closes both at once, because a rendered button is a <button> whatever the
// Razor tag was, and a dynamic expression has become a string by the time it is in the
// DOM. The measured result when this file was written: ten controls failed — Objects
// announced as "Object Tree", Drawings as "Drawing Tools", Trade as "Trading Dashboard",
// Zones as "Level Respect Report" (zero word overlap), the four pan/zoom buttons, and
// both IndicatorBar toggles, which showed the STATE ("Visible") and named the ACTION
// ("Hide SMA 20").
//
// The rule, and it is containment not equality: the accessible name must CONTAIN the
// visible text, so that a speech-input user saying the words they can see activates the
// control. Text inside an aria-hidden subtree is not visible text — the icon glyph is
// aria-hidden on purpose — and a control with no visible text at all is out of scope,
// since 2.5.3 has nothing to compare.

using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AngleSharp.Dom;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class LabelInNameRenderSweepTests
{
    /// <summary>
    /// The text a sighted user reads off the control: descendant text nodes with every
    /// aria-hidden subtree removed. Whitespace is collapsed because Razor indentation
    /// lands inside the element.
    /// </summary>
    private static string VisibleText(IElement el)
    {
        var sb = new System.Text.StringBuilder();
        Walk(el);
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();

        void Walk(INode node)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is IText t) { sb.Append(t.Data); continue; }
                if (child is IElement e)
                {
                    if (string.Equals(e.GetAttribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase))
                        continue;
                    Walk(e);
                }
            }
        }
    }

    // The comparison itself is DismissControlNameScanTests' — one definition of
    // "contains", shared, so the source scan and this render sweep cannot disagree about
    // which controls are failures. It is a contiguous WORD sequence, not a substring:
    // "Watch" is not contained in "Watchlists", and treating it as if it were is how two
    // of the twelve failures went unreported when they were first counted by eye.
    private static bool NameContainsVisibleText(string name, string visible) =>
        DismissControlNameScanTests.ContainsVisibleWords(name, visible);

    private sealed record Failure(string Surface, string Visible, string Name);

    private static List<Failure> Sweep(string surface, IRenderedFragment cut)
    {
        var failures = new List<Failure>();
        var roots = cut.Nodes.OfType<IElement>().ToList();
        var all = roots.SelectMany(r => new[] { r }.Concat(r.QuerySelectorAll("*"))).ToList();

        foreach (var el in all)
        {
            if (el.TagName is not ("BUTTON" or "A" or "SUMMARY")) continue;
            var name = el.GetAttribute("aria-label");
            if (string.IsNullOrWhiteSpace(name)) continue;      // named by its own text: 2.5.3 holds trivially
            var visible = VisibleText(el);
            if (visible.Length == 0) continue;                  // icon-only: nothing to contain
            if (NameContainsVisibleText(name, visible)) continue;
            failures.Add(new Failure(surface, visible, name));
        }
        return failures;
    }

    private static string Report(IEnumerable<Failure> failures) =>
        "These controls announce a name that does NOT contain their visible text, so a "
        + "speech-input user saying what they can see does not activate them (WCAG 2.5.3). "
        + "Extend the visible text; do not describe the control afresh:\n  "
        + string.Join("\n  ", failures.Select(f => $"{f.Surface}: visible \"{f.Visible}\" announced \"{f.Name}\""));

    // ── the chrome: the toolbar and the indicator bar ────────────────────────
    //
    // These are the surfaces the source scan could not see at all, and they are also the
    // ones that are on screen before any dialog is opened.

    [Theory]
    [InlineData("Toolbar")]
    [InlineData("IndicatorBar")]
    [InlineData("StatusBar")]
    public void ChromeSurface_EveryNamedControlContainsItsVisibleText(string name)
    {
        using var h = new BlazorTestHarness();
        var cut = ModalCatalog.RenderBare(h, ModalCatalog.Bare(name));

        var failures = Sweep(name, cut);
        Assert.True(failures.Count == 0, Report(failures));
    }

    [Fact]
    public void TheToolbarSweepActuallyReachesTheIconButtons()
    {
        // Vacuity check. The whole point of this file is that <ToolbarIconButton /> is a
        // component tag, so a harness that rendered the Toolbar without its icon buttons
        // — a DemoPolicy gate closing, a seed that leaves no chart — would report a clean
        // sweep over nothing. The toolbar carries roughly thirty of them.
        using var h = new BlazorTestHarness();
        var cut = ModalCatalog.RenderBare(h, ModalCatalog.Bare("Toolbar"));

        var iconButtons = cut.FindAll("button.icon-btn").Count;
        Assert.True(iconButtons >= 20,
            $"only {iconButtons} icon buttons rendered — the toolbar fixture is not exercising "
            + "the component this sweep exists to reach.");

        using var h2 = new BlazorTestHarness();
        var bar = ModalCatalog.RenderBare(h2, ModalCatalog.Bare("IndicatorBar"));
        var toggles = bar.FindAll("button.icon-btn").Count;
        Assert.True(toggles >= 3,
            $"only {toggles} icon buttons rendered in the indicator bar — the visibility and "
            + "audio toggles render only when a series is focused, and they are two of the ten.");
    }

    // ── every dialog in the catalog ──────────────────────────────────────────

    // ── the states the catalog seed never renders ────────────────────────────
    //
    // ModalCatalog.SeedChartState seeds one series that is VISIBLE, AUDIBLE and has no
    // components. So across the whole suite the only arms ever rendered of every
    // state-in-the-label control are "Hide" and "Mute" — the "Show" and "Unmute" arms, and
    // every per-component row button, are rendered by no test at all. That is the half of
    // the population this file exists for: a regression to
    // `Label="@(v ? "Hide" : "Hidden")"` beside `AriaLabel="@(v ? "Hide X" : "Show X")"` is
    // a live 2.5.3 failure in one state and correct in the other.

    private static ChartSeries HiddenMutedSeries(string id, string name, params string[] components)
    {
        var config = new SeriesConfig { Id = id, Name = name, FriendlyName = name };
        foreach (var c in components)
            config.Components.Add(new ComponentConfig
            {
                Name = c, DisplayName = c,
                DisplayType = AccessibleTrader.Sdk.Models.ComponentDisplayType.Line,
                IsVisible = false, IsMuted = true,
            });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = id })
        {
            IsVisible = false,
            IsMuted = true,
        };
    }

    private static void SeedHiddenMuted(BlazorTestHarness h, ChartSeries series)
    {
        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
        };
        h.WorkspaceStore.State.Returns(_ => state);
    }

    [Fact]
    public void TheIndicatorBarsOtherStateAlsoContainsItsVisibleText()
    {
        using var h = new BlazorTestHarness();
        SeedHiddenMuted(h, HiddenMutedSeries("sma20", "SMA 20"));
        var cut = ModalCatalog.Bare("IndicatorBar").Render(h.Ctx);

        // Vacuity: the two toggles only render when a series is focused, and they are the
        // whole point of this case.
        var texts = cut.FindAll("button.icon-btn").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains(texts, t => t.Contains("Show", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("Unmute", StringComparison.Ordinal));

        var failures = Sweep("IndicatorBar(hidden, muted)", cut);
        Assert.True(failures.Count == 0, Report(failures));
    }

    [Fact]
    public void TheObjectTreesPerComponentRowButtonsContainTheirVisibleText()
    {
        // The catalog seed gives ObjectTreeModal a series with ZERO components, so the
        // per-component Hide/Mute buttons — which gained aria-labels on 2026-09-03 because
        // five identical "Hide" buttons is what a component list used to put in the button
        // list — are rendered by the catalog sweep never.
        using var h = new BlazorTestHarness();
        SeedHiddenMuted(h, HiddenMutedSeries("rsi14", "RSI 14", "RSI", "Signal", "Histogram"));
        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.ObjectTreeModal>(
            b => b.Publish(new OpenObjectTreeEvent()));

        var mini = cut.FindAll("button.mini-btn");
        Assert.True(mini.Count >= 6,
            $"only {mini.Count} component row buttons rendered — the fixture is not exercising "
            + "the rows this case exists for (3 components should give 6).");
        // Each one names its own component, not just the action.
        Assert.All(mini, b => Assert.Contains("RSI 14", b.GetAttribute("aria-label") ?? ""));
        Assert.Equal(mini.Count, mini.Select(b => b.GetAttribute("aria-label")).Distinct().Count());

        var failures = Sweep("ObjectTreeModal(3 components, hidden, muted)", cut);
        Assert.True(failures.Count == 0, Report(failures));
    }

    [Theory]
    [MemberData(nameof(DialogNames))]
    public void OpenedDialog_EveryNamedControlContainsItsVisibleText(string name)
    {
        using var h = new BlazorTestHarness();
        var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog(name));
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));

        var failures = Sweep(name, cut);
        Assert.True(failures.Count == 0, Report(failures));
    }

    public static TheoryData<string> DialogNames => ModalCatalog.DialogNames;
}

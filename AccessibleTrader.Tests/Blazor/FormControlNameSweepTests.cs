// Does every form control a dialog renders have a name a screen reader can read out?
//
// The browser harness already asks this (AccessibleNameSweepTests) against Chromium's own
// accessibility tree, and it is the authority wherever it can reach. It cannot reach
// PropertiesModal: Shift+F12 resolves the FOCUSED series and returns silently when there is
// none, so on a cold-start page the dialog never opens and the sweep never sees it. That is how
// the 2026-09-01 audit's headline WCAG 3.3.2 finding — the sonification config rendering a
// screenful of "colour edit", "slider", "spin button", "combo box" with no names — sat under a
// green suite. The idiom was an orphan <label> with neither `for` nor a wrapped control: it
// LOOKS labelled in the markup and names nothing in the browser.
//
// This is the bUnit half. It renders every catalog dialog with whatever state its ShowAsync
// needs, walks every tab, and applies the markup-level naming rules to each control. It is
// deliberately NOT a re-implementation of the accname algorithm; it is the short list of ways a
// control in this library is allowed to get its name, and a control that uses none of them has
// no name by any algorithm. A `placeholder` is not on the list on purpose: it vanishes the moment
// the field has content, which is the SoundDesignerModal finding of 2026-08-26.
//
// The second theory opens PropertiesModal on a series rich enough to render every branch of
// every tab — a candle component and a line component, a colour rule that needs a level, a cloud
// fill, one level with the earcon on and one with it off, and separately a trend line drawing so
// the anchor editors render — because the catalog's one-line "Candles" series renders almost
// none of the controls the audit counted. It carries a floor on the number of controls swept, so
// a fixture that quietly stops rendering the tabs cannot report a clean sweep.

using AngleSharp.Dom;
using Bunit;
using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class FormControlNameSweepTests
{
    // ── the rules ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The ways a form control in this library may be named. Anything else is unnamed.
    /// Returned as a short description of the offending control for the failure message.
    /// </summary>
    internal static (List<string> Unnamed, int Swept) Sweep(IRenderedFragment cut)
    {
        var roots = cut.Nodes.OfType<IElement>().ToList();
        var all = roots.SelectMany(r => new[] { r }.Concat(r.QuerySelectorAll("*"))).ToList();

        var byId = new Dictionary<string, IElement>(StringComparer.Ordinal);
        foreach (var el in all)
        {
            var id = el.GetAttribute("id");
            if (!string.IsNullOrEmpty(id) && !byId.ContainsKey(id)) byId[id] = el;
        }
        // Matched on the attribute string, not via a CSS selector, so an id that would need
        // escaping still resolves the way a browser resolves it.
        var labelFor = new Dictionary<string, IElement>(StringComparer.Ordinal);
        foreach (var el in all.Where(e => e.TagName == "LABEL"))
        {
            var f = el.GetAttribute("for");
            if (!string.IsNullOrEmpty(f) && !labelFor.ContainsKey(f)) labelFor[f] = el;
        }

        var unnamed = new List<string>();
        int swept = 0;
        foreach (var el in all)
        {
            if (el.TagName is not ("INPUT" or "SELECT" or "TEXTAREA")) continue;
            if (el.TagName == "INPUT" &&
                string.Equals(el.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase)) continue;
            swept++;
            if (HasName(el, byId, labelFor)) continue;
            unnamed.Add(Describe(el));
        }
        return (unnamed, swept);
    }

    private static bool HasName(IElement el, Dictionary<string, IElement> byId, Dictionary<string, IElement> labelFor)
    {
        if (!string.IsNullOrWhiteSpace(el.GetAttribute("aria-label"))) return true;

        var labelledBy = el.GetAttribute("aria-labelledby");
        if (!string.IsNullOrWhiteSpace(labelledBy) &&
            labelledBy.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Any(id => byId.TryGetValue(id, out var t) && !string.IsNullOrWhiteSpace(t.TextContent)))
            return true;

        var id = el.GetAttribute("id");
        if (!string.IsNullOrEmpty(id) && labelFor.TryGetValue(id, out var lbl) &&
            !string.IsNullOrWhiteSpace(lbl.TextContent))
            return true;

        for (var p = el.ParentElement; p != null; p = p.ParentElement)
            if (p.TagName == "LABEL" && !string.IsNullOrWhiteSpace(p.TextContent)) return true;

        if (!string.IsNullOrWhiteSpace(el.GetAttribute("title"))) return true;

        return false;
    }

    private static string Describe(IElement el)
    {
        var tag = el.TagName.ToLowerInvariant();
        var type = el.GetAttribute("type");
        var head = tag == "input" ? $"<input type=\"{type}\">" : $"<{tag}>";

        // The orphan label, if there is one, so the message names the control the way the user
        // would have seen it; and the fieldset legend, which is the group it sits in.
        string orphan = "";
        for (var s = el.PreviousElementSibling; s != null; s = s.PreviousElementSibling)
            if (s.TagName == "LABEL") { orphan = $" beside orphan label \"{Squash(s.TextContent)}\""; break; }
        string group = "";
        var fs = el.Closest("fieldset");
        var legend = fs?.QuerySelector("legend");
        if (legend != null) group = $" in \"{Squash(legend.TextContent)}\"";

        return head + orphan + group;
    }

    private static string Squash(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ── walking a dialog's tabs ───────────────────────────────────────────────

    /// <summary>
    /// Sweep the dialog as it opened, then once per tab. Only one tab's controls exist in the
    /// DOM at a time, so a sweep of the first tab alone covers a fraction of the dialog.
    /// </summary>
    private static (List<string> Unnamed, int Swept) SweepAllTabs(string name, IRenderedFragment cut)
    {
        var unnamed = new List<string>();
        var (u0, swept) = Sweep(cut);
        unnamed.AddRange(u0.Select(d => $"{name}|(initial)|{d}"));

        int tabCount = cut.FindAll("[role='tab']").Count;
        for (int i = 0; i < tabCount; i++)
        {
            var tab = cut.FindAll("[role='tab']")[i];
            var tabName = Squash(tab.TextContent);
            tab.Click();
            cut.WaitForAssertion(() =>
                Assert.Equal("true", cut.FindAll("[role='tab']")[i].GetAttribute("aria-selected")));
            var (u, n) = Sweep(cut);
            swept += n;
            unnamed.AddRange(u.Select(d => $"{name}|{tabName}|{d}"));
        }
        return (unnamed.Distinct().ToList(), swept);
    }

    private static void AssertNoneUnnamed(string name, List<string> unnamed) =>
        Assert.True(unnamed.Count == 0,
            $"Form controls in {name} that a screen reader announces with no name:\n  "
            + string.Join("\n  ", unnamed)
            + "\n\nGive each one a name: a <label for> pointing at its id, a wrapping <label>, or an "
            + "aria-label that CONTAINS the visible label text (WCAG 2.5.3). RiskPlanEditor.razor's "
            + "take-profit ladder is the in-repo template. There is no exemption list here on purpose.");

    // ── every catalog dialog ──────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(DialogNames))]
    public void OpenedDialog_EveryFormControlHasAName(string name)
    {
        using var h = new BlazorTestHarness();
        var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog(name));
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));

        var (unnamed, _) = SweepAllTabs(name, cut);
        AssertNoneUnnamed(name, unnamed);
    }

    public static TheoryData<string> DialogNames => ModalCatalog.DialogNames;

    // ── PropertiesModal on a series that renders every branch ────────────────

    private static ChartSeries RichIndicatorSeries()
    {
        var config = new SeriesConfig { Id = "rich", Name = "Rich Oscillator", FriendlyName = "Rich Oscillator" };
        var body = new ComponentConfig
        {
            Name = "Body", DisplayName = "Body", DisplayType = ComponentDisplayType.Candle, IsVisible = true,
        };
        body.ColorRules.Add(new ColorRule { Condition = ColorCondition.AboveLevel, Level = 70, ColorHex = "#FF0000" });
        config.Components.Add(body);
        config.Components.Add(new ComponentConfig
        {
            Name = "Signal Line", DisplayName = "Signal Line", DisplayType = ComponentDisplayType.Line, IsVisible = true,
        });
        config.CloudFills.Add(new CloudFillConfig
        {
            DisplayName = "Body cloud", UpperComponentName = "Body", LowerComponentName = "Signal Line",
        });
        // One level with the earcon ON (renders volume + crossing direction) and one with it OFF,
        // and a name with a space in it, because the ids are derived from the name.
        config.Levels.Add(new LevelConfig { Name = "Overbought", Value = 70, PlayEarcon = true });
        config.Levels.Add(new LevelConfig { Name = "Oversold zone", Value = 30, PlayEarcon = false });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "rich" });
    }

    private static ChartSeries TrendLineDrawing()
    {
        var config = new SeriesConfig { Id = "tl", Name = "Trend line", FriendlyName = "Trend line" };
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "tl" })
        {
            // A trend line needs both a price and a date at each end, so all four anchor editors render.
            Drawing = new DrawingData
            {
                Type = DrawingType.TrendLine,
                AnchorPrice1 = 100, AnchorDate1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AnchorPrice2 = 110, AnchorDate2 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        };
    }

    private static void SeedFocused(BlazorTestHarness h, ChartSeries series)
    {
        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
        };
        h.WorkspaceStore.State.Returns(_ => state);
    }

    [Fact]
    public void PropertiesModal_OnARichIndicator_EveryFormControlHasAName()
    {
        using var h = new BlazorTestHarness();
        SeedFocused(h, RichIndicatorSeries());
        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.PropertiesModal>(
            b => b.Publish(new OpenPropertiesEvent()));
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));

        var (unnamed, swept) = SweepAllTabs("PropertiesModal(rich)", cut);
        AssertNoneUnnamed("PropertiesModal(rich)", unnamed);

        // The vacuity floor. Two components across Appearance and Sonification, a colour rule, a
        // cloud fill, two levels and four speech templates come to well over thirty controls; a
        // count below this means a tab did not render, not that the dialog is clean.
        Assert.True(swept >= 30,
            $"The rich-series sweep of PropertiesModal examined only {swept} controls, so its clean "
            + "result means little. A tab stopped rendering, or the fixture stopped exercising it.");
    }

    [Fact]
    public void PropertiesModal_OnATrendLine_EveryFormControlHasAName()
    {
        using var h = new BlazorTestHarness();
        SeedFocused(h, TrendLineDrawing());
        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.PropertiesModal>(
            b => b.Publish(new OpenPropertiesEvent()));
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));

        var (unnamed, swept) = SweepAllTabs("PropertiesModal(drawing)", cut);
        AssertNoneUnnamed("PropertiesModal(drawing)", unnamed);

        // The name field plus four anchor editors on the General tab alone.
        Assert.True(swept >= 5,
            $"The drawing sweep of PropertiesModal examined only {swept} controls; the anchor editors did not render.");
    }

    // ── the instrument itself ─────────────────────────────────────────────────

    /// <summary>
    /// The sweep must flag the exact idiom the audit found — an orphan label beside an input, and
    /// a bare select — while accepting each of the sanctioned naming routes. Without this, every
    /// green theory above is also what a sweep that recognises nothing would report.
    /// </summary>
    [Fact]
    public void Sweep_FlagsOrphanLabelsAndBareControls_AndAcceptsEachNamingRoute()
    {
        using var h = new BlazorTestHarness();
        var cut = h.Ctx.Render(builder =>
        {
            int s = 0;
            builder.OpenElement(s++, "fieldset");
            builder.OpenElement(s++, "legend"); builder.AddContent(s++, "Component: Body"); builder.CloseElement();

            // Unnamed: the audit's idiom, and a control with nothing beside it at all.
            builder.OpenElement(s++, "label"); builder.AddContent(s++, "Bullish Color"); builder.CloseElement();
            builder.OpenElement(s++, "input"); builder.AddAttribute(s++, "type", "color"); builder.CloseElement();
            builder.OpenElement(s++, "select"); builder.CloseElement();
            // Placeholder-only is unnamed too — it vanishes once the field has content.
            builder.OpenElement(s++, "textarea"); builder.AddAttribute(s++, "placeholder", "Paste here"); builder.CloseElement();

            // Named, one per route.
            builder.OpenElement(s++, "label"); builder.AddAttribute(s++, "for", "n1"); builder.AddContent(s++, "Volume"); builder.CloseElement();
            builder.OpenElement(s++, "input"); builder.AddAttribute(s++, "id", "n1"); builder.AddAttribute(s++, "type", "range"); builder.CloseElement();
            builder.OpenElement(s++, "label");
            builder.OpenElement(s++, "input"); builder.AddAttribute(s++, "type", "checkbox"); builder.CloseElement();
            builder.AddContent(s++, "Visible");
            builder.CloseElement();
            builder.OpenElement(s++, "select"); builder.AddAttribute(s++, "aria-label", "Dash style for Body"); builder.CloseElement();
            builder.OpenElement(s++, "span"); builder.AddAttribute(s++, "id", "n2"); builder.AddContent(s++, "Base frequency"); builder.CloseElement();
            builder.OpenElement(s++, "input"); builder.AddAttribute(s++, "type", "number"); builder.AddAttribute(s++, "aria-labelledby", "n2"); builder.CloseElement();
            // A label whose `for` points nowhere names nothing.
            builder.OpenElement(s++, "label"); builder.AddAttribute(s++, "for", "gone"); builder.AddContent(s++, "Thickness"); builder.CloseElement();
            builder.OpenElement(s++, "input"); builder.AddAttribute(s++, "type", "range"); builder.CloseElement();
            builder.CloseElement();
        });

        var (unnamed, swept) = Sweep(cut);

        Assert.Equal(8, swept);
        Assert.Equal(4, unnamed.Count);
        Assert.Contains(unnamed, d => d.Contains("<input type=\"color\">") && d.Contains("orphan label \"Bullish Color\"") && d.Contains("Component: Body"));
        Assert.Contains(unnamed, d => d.StartsWith("<select>", StringComparison.Ordinal));
        Assert.Contains(unnamed, d => d.StartsWith("<textarea>", StringComparison.Ordinal));
        Assert.Contains(unnamed, d => d.Contains("<input type=\"range\">") && d.Contains("orphan label \"Thickness\""));
    }
}

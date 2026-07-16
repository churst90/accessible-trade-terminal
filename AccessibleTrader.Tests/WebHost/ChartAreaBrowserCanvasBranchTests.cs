using System;
using System.Collections.Generic;
using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Tests.Blazor;
using Bunit;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Pins the MAUI-safety contract on <c>ChartArea.razor</c>: the inline
/// browser <c>&lt;img&gt;</c> chart surface introduced in phase L2 is
/// rendered ONLY when <c>IRuntimePlatform.IsBrowserHost</c> is true.
/// On the MAUI heads (Windows / macOS / iOS / Android) the property
/// returns false via its default-interface implementation, so the
/// component must NOT emit an &lt;img&gt; — those hosts paint the
/// chart through the native SkiaSharp overlay declared in MainPage.xaml,
/// and an extra &lt;img&gt; would visually cover it.
///
/// A future refactor that flips the default or accidentally removes the
/// guard would silently regress the MAUI experience; these two tests
/// catch it.
/// </summary>
public class ChartAreaBrowserCanvasBranchTests
{
    [Fact]
    public void MauiHost_NoImageElementIsRendered()
    {
        using var harness = BuildHarness(isBrowserHost: false);
        var cut = harness.Ctx.RenderComponent<ChartArea>();

        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void WebHost_ImageElementIsRenderedWithDataUrlSrc()
    {
        using var harness = BuildHarness(isBrowserHost: true);
        var cut = harness.Ctx.RenderComponent<ChartArea>();

        var img = cut.Find("img");
        var src = img.GetAttribute("src") ?? "";
        // Before the first throttled render trigger fires (100 ms), the
        // src is the inlined 1×1 black-pixel placeholder, which is still
        // a base64 PNG data URL. That's enough to pin the contract.
        Assert.StartsWith("data:image/png;base64,", src);
        // pointer-events:none + z-index:0 are load-bearing so clicks
        // fall through to the surrounding chart-interact-zone div.
        var style = img.GetAttribute("style") ?? "";
        Assert.Contains("pointer-events: none", style);
        Assert.Contains("z-index: 0", style);
    }

    // ── Harness setup ────────────────────────────────────────────────────────
    //
    // ChartArea has nine @inject directives. Most resolve to NSubstitute
    // stubs; the few that ChartArea reads on first render (IDataOrchestrator
    // CurrentState / StateChanged, IPaneLayoutService.Dividers,
    // IRuntimePlatform.IsBrowserHost) are configured below.

    // Shared with ChartAreaBarSliderTests (Phase C): pass a state to seed the
    // store with chart data; null keeps the empty WorkspaceState.Initial.
    internal static BlazorTestHarness BuildHarness(bool isBrowserHost, Sdk.Models.WorkspaceState? state = null)
    {
        var harness = new BlazorTestHarness();

        // WorkspaceStore.State returns WorkspaceState.Initial via the default
        // harness wiring unless a caller-provided state overrides it.
        if (state != null)
            harness.WorkspaceStore.State.Returns(_ => state);

        // IRuntimePlatform — the property under test.
        var platform = Substitute.For<IRuntimePlatform>();
        platform.IsBrowserHost.Returns(isBrowserHost);
        harness.Ctx.Services.AddSingleton(platform);

        // The bar-navigator flick slider shares the touch-controls gate
        // (ui.touchNavBar): report a touch device so the slider tests see it.
        harness.Ctx.JSInterop.Setup<bool>("accessibleTrader.isTouchCapable").SetResult(true);
        harness.Ctx.JSInterop.SetupVoid("canvasRegion.start", _ => true).SetVoidResult();

        // IDataOrchestrator — CurrentState read in OnInitialized,
        // StateChanged subscribed to.
        var orch = Substitute.For<IDataOrchestrator>();
        orch.CurrentState.Returns(DataState.Initializing);
        orch.StateChanged.Returns(System.Reactive.Linq.Observable.Empty<DataState>());
        harness.Ctx.Services.AddSingleton(orch);

        // IPaneLayoutService — Dividers iterated in the markup. Dividers
        // is a list of (BelowPaneName, DividerFraction) tuples.
        var pane = Substitute.For<IPaneLayoutService>();
        pane.Dividers.Returns(new List<(string BelowPaneName, float DividerFraction)>());
        harness.Ctx.Services.AddSingleton(pane);

        // IAccessibilityFeedbackCoordinator — empty stub, never invoked here.
        harness.Ctx.Services.AddSingleton(Substitute.For<IAccessibilityFeedbackCoordinator>());

        // ICanvasRegionProvider — BoundsChanged subscribed to.
        var region = Substitute.For<ICanvasRegionProvider>();
        region.BoundsChanged.Returns(System.Reactive.Linq.Observable.Empty<CanvasBounds>());
        harness.Ctx.Services.AddSingleton(region);

        // GlobalInputService is a concrete class — construct one with stubbed deps.
        // OnKeyDown / OnMouseEvent aren't invoked in the first render.
        harness.Ctx.Services.AddSingleton(new GlobalInputService(
            Substitute.For<IInputService>(),
            harness.EventBus,
            harness.WorkspaceStore));

        // ChartHoverTracker (Phase B crosshair) — concrete class injected by ChartArea;
        // no mouse events flow in these render-branch tests.
        harness.Ctx.Services.AddSingleton(new ChartHoverTracker(
            Substitute.For<IInputService>(),
            harness.WorkspaceStore));

        // ChartRenderer — concrete class, only called inside
        // RenderChartImageAsync which is gated behind the 100 ms render
        // trigger. We never wait for that in these tests, so we just need
        // a non-null instance for DI to resolve.
        // Note: ChartRenderer takes a concrete ThemeService, not IThemeService.
        // BlazorTestHarness constructs one as `new ThemeService(SettingsManager)`
        // but exposes it via the IThemeService property; we build a fresh one here.
        var concreteTheme = new ThemeService(harness.SettingsManager);
        harness.Ctx.Services.AddSingleton(new ChartRenderer(
            concreteTheme,
            Substitute.For<IStylingService>(),
            Substitute.For<IProfileService>(),
            pane,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChartRenderer>.Instance,
            Substitute.For<AccessibleTrader.Sdk.Logging.IAppLogger>()));

        // ChartArea calls JS interop on first render — shim the calls so
        // bUnit doesn't throw "JSInterop calls cannot be issued at this time".
        harness.Ctx.JSInterop.SetupVoid("canvasRegion.start", _ => true).SetVoidResult();
        harness.Ctx.JSInterop.SetupVoid("canvasRegion.stop");
        harness.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true);
        harness.Ctx.JSInterop.SetupVoid("accessibleTrader.setChartFocused", _ => true);

        return harness;
    }
}

using System;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Theming;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebView;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace AccessibleTrader.BlazorClient;

public partial class MainPage : ContentPage
{
    private readonly IWorkspaceStore _store;
    private readonly ChartRenderer _renderer;
    private readonly IThemeService _themeService;
    private readonly IEventBus _eventBus;
    private readonly ICanvasRegionProvider _canvasRegion;
    private readonly ILogger<MainPage> _logger;
    private IDisposable? _stateSub;
    private IDisposable? _redrawSub;
    private IDisposable? _modalSub;
    private IDisposable? _titleSub;
    private IDisposable? _canvasRegionSub;
    private EventHandler<AccessibleTrader.Sdk.Theming.ChartTheme>? _themeChangedHandler;
    private int _openModalCount;
    private string _lastTitleKey = "";

    public MainPage(
        IWorkspaceStore store,
        ChartRenderer renderer,
        IAppStartupService startupService,
        IThemeService themeService,
        IEventBus eventBus,
        ICanvasRegionProvider canvasRegion,
        ILogger<MainPage> logger)
    {
        InitializeComponent();

        _store = store;
        _renderer = renderer;
        _themeService = themeService;
        _eventBus = eventBus;
        _canvasRegion = canvasRegion;
        _logger = logger;

        // Subscribe before the handler attaches so no initialization event can be missed.
        blazorWebView.BlazorWebViewInitialized += OnBlazorWebViewInitialized;

        // Initialize all self-wiring services and register default chart series.
        SafeFireAndForget.Run(startupService.InitializeAsync, _logger, "AppStartup");
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // DefaultBackgroundColor is set in TransparentBlazorWebViewHandler.CreatePlatformView()
        // (Platforms/Windows/) before MAUI initiates WebView2 navigation.  Nothing needed here.

        // Subscribe to state changes once this page is fully attached to the native view tree.
        // Every store dispatch that changes state triggers a canvas repaint on the main thread.
        _stateSub = _store.StateStream.Subscribe(_ =>
            MainThread.BeginInvokeOnMainThread(() => _chartCanvas.InvalidateSurface()));

        // REDRAW EVENT: Triggered by DataOrchestrationService for high-frequency updates
        _redrawSub = _eventBus.Subscribe<RedrawEvent>(_ =>
            MainThread.BeginInvokeOnMainThread(() => _chartCanvas.InvalidateSurface()));

        // MODAL CANVAS HIDE/SHOW: The Skia canvas sits on top of the BlazorWebView in the XAML
        // Grid, so it covers modals visually. When any modal opens we hide the canvas (making
        // modals visible and mouse-reachable), and restore it when the last modal closes.
        // Keyboard + speech navigation is always unaffected regardless of canvas visibility.
        _modalSub = _eventBus.Subscribe<ModalStateChangedEvent>(e =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _openModalCount = Math.Max(0, _openModalCount + (e.IsOpen ? 1 : -1));
                _chartCanvas.IsVisible = _openModalCount == 0;
            }));
        
        // Update native window title when any identity field changes — symbol,
        // timeframe, or provider. Tracking only Symbol left the titlebar
        // stamped with a stale timeframe after a dropdown change.
        _titleSub = _store.StateStream.Subscribe(state =>
        {
            var sym = state.Identity.Symbol ?? "";
            var tf  = state.Identity.Timeframe ?? "";
            var prv = state.Identity.Provider ?? "";
            var key = $"{sym}|{tf}|{prv}";
            if (key != _lastTitleKey)
            {
                _lastTitleKey = key;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var window = Application.Current?.Windows.FirstOrDefault();
                    if (window != null)
                    {
                        window.Title = string.IsNullOrEmpty(sym)
                            ? "Accessible Trade Terminal"
                            : $"Accessible Trade Terminal: {sym} {tf} on {prv}";
                    }
                });
            }
        });

        // Force the first paint now that the native canvas is attached.
        MainThread.BeginInvokeOnMainThread(() => _chartCanvas.InvalidateSurface());

        // Theme changes also require a full repaint.
        _themeChangedHandler = (_, _) =>
            MainThread.BeginInvokeOnMainThread(() => _chartCanvas.InvalidateSurface());
        _themeService.ThemeChanged += _themeChangedHandler;

        // Track the Blazor ChartArea's live bounding rect and re-apply the canvas
        // margin on the main thread. Supersedes the hardcoded 185/100 DIP values
        // in MainPage.xaml once the first rect arrives from JS. The XAML values
        // remain as a fallback for the brief moment before canvasRegion.start
        // fires, and for test contexts that have no IJSRuntime.
        _canvasRegionSub = _canvasRegion.BoundsChanged.Subscribe(b =>
            MainThread.BeginInvokeOnMainThread(() =>
                _chartCanvas.Margin = new Thickness(0, b.TopDip, 0, b.BottomDip)));
    }

    private void OnCanvasPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        // Always clear to black first. If no data is loaded the canvas stays black,
        // which is the correct "no chart" state (dark background visible under the
        // transparent BlazorWebView overlay).
        canvas.Clear(SkiaSharp.SKColors.Black);

        var state = _store.State;
        if (state.Data != null && state.Data.Count > 0)
        {
            float density = (float)DeviceDisplay.MainDisplayInfo.Density;

            _renderer.Render(
                canvas,
                info.Width,
                info.Height,
                state.Data,
                state.ActiveSeries,
                state.CurrentDataIndex,
                state.ViewportStartIndex,
                state.ViewportLength,
                state.ViewportRange,
                state.PaneRanges,
                state.IsHeikinAshi,
                state.IsLogScale,
                density,
                state.PaneHeightRatios,
                state.IndicatorPaneScrollIndex,
                state.RightMarginBars);
        }
    }

#if WINDOWS
    private async void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        // Must be Transparent so the native SkiaSharp canvas behind the WebView can show through.
        // We rely on the SKCanvasView.Clear(Black) and the ChartArea.razor overlay for the blackout state.
        e.WebView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
        
        // Ensure the internal WinUI 3 control also has a transparent background to avoid
        // any opaque "white walls" during the initial composition phase.
        if (blazorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 platformWv2)
        {
            platformWv2.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
        }

        // CRITICAL: In MAUI Blazor there is typically only ONE NavigationCompleted event
        // (the initial page load), and it fires BEFORE BlazorWebViewInitialized fires.
        // Subscribing to NavigationCompleted here and waiting for a future event therefore
        // never executes the CSS injection for the initial load.
        //
        // Fix: execute the transparency script IMMEDIATELY now that Blazor is running and
        // the DOM is fully available.  Also subscribe for any future navigations (e.g. the
        // Blazor error-reload link) so they stay transparent too.
        const string transparencyScript =
            "document.documentElement.style.background='transparent';" +
            "document.body.style.background='transparent';" +
            "var a=document.getElementById('app');if(a)a.style.background='transparent';";

        try
        {
            await e.WebView.CoreWebView2.ExecuteScriptAsync(transparencyScript);
        }
        catch { /* non-critical — app.css inline style already sets these */ }

        e.WebView.CoreWebView2.NavigationCompleted += async (_, _) =>
        {
            try { await e.WebView.CoreWebView2.ExecuteScriptAsync(transparencyScript); }
            catch { }
        };
    }
#else
    private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        // Transparency on Android/iOS is set via BackgroundColor="Transparent" in XAML.
    }
#endif

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Final session snapshot — the periodic autosave samples every 30s, but
        // work done in the last window before close must not be lost.
        try
        {
            (Handler?.MauiContext?.Services.GetService(typeof(Core.Services.Workspace.ISessionAutosaveService))
                as Core.Services.Workspace.ISessionAutosaveService)?.SaveNow();
        }
        catch { /* teardown-order races must never block close */ }
        blazorWebView.BlazorWebViewInitialized -= OnBlazorWebViewInitialized;
        _stateSub?.Dispose();
        _redrawSub?.Dispose();
        _modalSub?.Dispose();
        _titleSub?.Dispose();
        _canvasRegionSub?.Dispose();
        if (_themeChangedHandler != null)
            _themeService.ThemeChanged -= _themeChangedHandler;
    }
}

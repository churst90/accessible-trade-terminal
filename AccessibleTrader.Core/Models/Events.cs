using System;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Models
{
    // ── Playback ─────────────────────────────────────────────────────────────
    public record PlaybackCommand(string Command, string Scope = "CHART", int StartIndex = -1); // "Play", "Stop", "Toggle"

    // ── Audio / Sonification ──────────────────────────────────────────────────
    public record VolumeChangeEvent(string Scope, float Delta); // Scope: "CHART", "SERIES", "COMPONENT"
    public record ToggleMuteEvent(string Scope = "AUTO", string? SeriesId = null);
    public record ToggleHideEvent(string Scope = "AUTO", string? SeriesId = null);

    // ── Tools ─────────────────────────────────────────────────────────────────
    public enum ToolType { Heatmap, VolumeProfile, MarketProfile }
    public record ToggleToolEvent(ToolType Tool);

    // ── Data / State ──────────────────────────────────────────────────────────
    public record RequestHistoryEvent();
    public record DeactivateEvent();
    public record SeriesStateChangedEvent(string Name, bool IsVisible, bool IsMuted);
    public record AnnouncementEvent(string Message, bool Interrupt = true);

    public enum FeedbackType { Navigation, SeriesSelection, ComponentSelection, PointFocus, Error, Alert, Info, StateChange, VolumeChange, ViewportChange, Boundary }
    public record FeedbackRequestEvent(
        FeedbackType Type,
        string? Message = null,
        bool Interrupt = true,
        bool IsUserInitiated = true,
        bool IncludeSonification = true,
        bool IsXMove = false,
        bool IsYMove = false,
        bool IsJump = false);

    public record ChartFocusEvent();
    public record RedrawEvent();
    public record ConnectionStatusEvent(string Provider, ConnectionState State, string Message);

    /// <summary>Fired when a navigation key (arrow) is released so the audio engine can stop the sustaining voice.</summary>
    public record NavKeyReleasedEvent();

    // ── UI Commands ───────────────────────────────────────────────────────────
    public record OpenSettingsEvent();
    public record OpenTradingDashboardEvent();
    public record OpenObjectTreeEvent();
    public record OpenHelpEvent();
    public record OpenApiKeysEvent();
    public record OpenOrderBookEvent();
    public record OpenAddIndicatorEvent();
    public record OpenDrawingToolsEvent();
    public record OpenPropertiesEvent(string? SeriesId = null);
    public record DeleteSeriesEvent(string? SeriesId = null);
    public record AddDrawingEvent(string DrawingType);
    public record CancelDrawingEvent();
    /// <summary>
    /// Fired by <see cref="CommandDispatcher"/> when the user confirms both anchors in Coordinate Entry mode.
    /// <see cref="DrawingInteractionManager"/> subscribes and calls <c>HandleAddDrawing</c> twice — once per anchor — to complete the drawing.
    /// </summary>
    public record CoordinateEntryCompleteEvent(string DrawingTypeName, int Anchor1DataIndex, int Anchor2DataIndex);
    public record IndicatorUpdatedEvent(string? SeriesId = null);

    public record AppErrorEvent(
        ErrorSeverity Severity,
        ErrorCategory Category,
        string Message,
        string Source,
        Exception? Exception = null,
        bool IsDuplicate = false
    );

    public record AlertFiredEvent(AlertFired Alert);

    // ── UI Modal Events ───────────────────────────────────────────────────────
    public record OpenAlertsEvent();
    public record OpenStrategiesEvent();
    public record OpenCustomScriptsEvent();
    public record OpenSoundDesignerEvent();
    public record OpenAIAnalystEvent();
    /// <summary>
    /// Fired by any modal when it opens (IsOpen=true) or closes (IsOpen=false).
    /// MainPage subscribes to hide/restore the native SkiaSharp canvas so modals
    /// are visually accessible even when the Skia layer is rendered on top.
    /// </summary>
    public record ModalStateChangedEvent(bool IsOpen);

    // ── Tab Events ────────────────────────────────────────────────────────────
    /// <summary>Fired after a tab switch completes so audio/sonification services can stop playback.</summary>
    public record TabSwitchedEvent(int NewTabIndex, string Label);

    // ── Live Bar Events ───────────────────────────────────────────────────────
    /// <summary>Fired when a live bar finalizes and a new one opens. ClosedBar = completed candle.</summary>
    public record NewBarEvent(Ohlcv ClosedBar, Ohlcv NewBar);
    /// <summary>Fired on every intra-bar tick (same bar count, last bar updated in place).</summary>
    public record IntraBarUpdateEvent(Ohlcv CurrentBar, Ohlcv? PreviousBar = null, Ohlcv? TwoBarsAgo = null);

    // ── Trading Order Events ──────────────────────────────────────────────────
    public record OrderFilledEvent(OrderUpdate Order);
    public record OrderPartialFillEvent(OrderUpdate Order);
    public record StopHitEvent(OrderUpdate Order);
    public record TakeProfitHitEvent(OrderUpdate Order);
    public record OrderRejectedEvent(OrderUpdate Order, string Reason);
    public record MarginWarningEvent(string Symbol, double MarginLevel, string Message);

    // ── Strategy Events ───────────────────────────────────────────────────────
    public record StrategySignalEvent(string StrategyName, StrategySignal Signal, string InstanceId);
    public record StrategyConfirmedEvent(string InstanceId);
    public record StrategyDismissedEvent(string InstanceId);
}

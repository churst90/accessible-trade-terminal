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
    public record OpenSaveWorkspaceEvent();
    public record OpenLoadWorkspaceEvent();
    public record OpenJournalEvent();
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

    // ── Composite Setup State Events ──────────────────────────────────────────
    /// <summary>
    /// Fired when a <c>ConfigurableStrategy</c> sees its full condition tree evaluate true
    /// for the first time and the <c>ResolvedRiskPlan</c> clears the minimum reward/risk gate.
    /// SetupSonifier rings <c>setup_long_bell</c> or <c>setup_short_bell</c> and speaks the rationale.
    /// </summary>
    public record SetupConfirmedEvent(
        string StrategyName,
        string InstanceId,
        AccessibleTrader.Sdk.Plugins.OrderSide Side,
        string Rationale,
        AccessibleTrader.Sdk.Strategies.ResolvedRiskPlan ResolvedPlan);

    /// <summary>
    /// Fired on every subsequent bar where an already-active setup's conditions still hold.
    /// SetupSonifier replays the setup bell at reduced volume and speaks a brief
    /// "still confirmed" message — the user wanted ongoing audio confirmation per their
    /// session A directive.
    /// </summary>
    public record SetupReconfirmedEvent(
        string StrategyName,
        string InstanceId,
        AccessibleTrader.Sdk.Plugins.OrderSide Side,
        int BarsSinceFirstConfirm);

    /// <summary>
    /// Fired when one or more leaves of an active setup flip from true to false. Carries the
    /// human-readable labels of the dropped leaves so the user hears exactly which condition
    /// failed (e.g. "Cipher A wave cross dropped off"). The setup itself may or may not still
    /// be active depending on the tree's logic.
    /// </summary>
    public record SetupDroppedEvent(
        string StrategyName,
        string InstanceId,
        IReadOnlyList<string> DroppedLeafLabels,
        bool SetupStillActive);

    /// <summary>
    /// Fired by <c>ConfigurableStrategy</c> when a setup's conditions clear and the resolved
    /// risk plan passes the R:R gate, but the entry trigger (e.g. OnPullbackToLevel) has not
    /// yet fired. The strategy enters the "Armed" state and waits for the entry trigger
    /// before emitting an actual order signal. SetupSonifier announces this with a lighter
    /// "armed" earcon distinct from the main setup bell — the user knows the setup is real
    /// but they aren't in a position yet.
    /// </summary>
    public record SetupArmedEvent(
        string StrategyName,
        string InstanceId,
        AccessibleTrader.Sdk.Plugins.OrderSide Side,
        string TriggerDescription,
        AccessibleTrader.Sdk.Strategies.ResolvedRiskPlan ResolvedPlan);

    /// <summary>
    /// Fired the moment the entry trigger of an Armed setup actually fires (e.g. price
    /// pulled back to the configured level). The order is placed on this bar; SetupSonifier
    /// plays an "entry reached" earcon (slightly brighter than Armed but lighter than the
    /// main setup bell) and announces the trigger price.
    /// </summary>
    public record SetupEntryReachedEvent(
        string StrategyName,
        string InstanceId,
        AccessibleTrader.Sdk.Plugins.OrderSide Side,
        double TriggerPrice,
        int BarsArmed);
}

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

    /// <summary>
    /// Asks <see cref="BlazorClient.Components.ChartArea"/> to programmatically move
    /// keyboard focus to the chart element. Published by <see cref="CommandDispatcher"/>
    /// when the user presses Ctrl+Alt+Shift+C, and by <see cref="ModalBase.CloseModal"/>
    /// to return focus to the chart after every modal closes. ChartArea responds by
    /// invoking <c>accessibleTrader.focusElement("chart-interact-zone")</c> via JS;
    /// the resulting native focus event then fires <see cref="ChartFocusEvent"/> as a
    /// side effect, which is what flips the chart-active gate in CommandDispatcher.
    /// </summary>
    public record RequestChartFocusEvent();

    /// <summary>
    /// Published when the user presses Ctrl+Alt+Shift+T (FocusTabBar). Asks the
    /// workspace <c>TabBar</c> to move keyboard focus onto the tab switcher bar so
    /// it can be operated with the arrow keys / Home / End / number row / Delete.
    /// Web-safe because Ctrl+Tab and Ctrl+Number are reserved by the browser; this
    /// three-modifier chord is not. The TabBar responds by JS-focusing its
    /// <c>workspace-tabbar</c> container (a no-op when only one tab is open).
    /// </summary>
    public record FocusTabBarEvent();

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
    /// Fired by <see cref="Accessibility.DrawingInteractionManager"/> when a right-click lands
    /// on an existing drawing's anchor handle. <see cref="BlazorClient.Components.DrawingContextMenu"/>
    /// subscribes and shows a floating menu anchored at <paramref name="ViewportX"/> /
    /// <paramref name="ViewportY"/> (CSS pixels, relative to the chart-interact-zone element).
    /// </summary>
    public record OpenDrawingContextMenuEvent(string SeriesId, double ViewportX, double ViewportY);
    /// <summary>
    /// Fired when a right-click on the chart does NOT land on a drawing anchor: the
    /// chart-level context menu (play from here, jump to latest, crosshair toggle, and
    /// a per-series action list — deliberately requiring no pixel-precise pointing).
    /// <paramref name="ViewportX"/>/<paramref name="ViewportY"/> are CSS pixels relative
    /// to the chart-interact-zone (NaN/NaN = keyboard origin, self-positions centrally);
    /// <paramref name="BarIndex"/> is the bar under the cursor, or -1 in the empty
    /// right margin (keyboard origin passes the current cursor index).
    /// </summary>
    public record OpenChartContextMenuEvent(
        double ViewportX, double ViewportY, int BarIndex,
        string? HitSeriesId = null, int HitComponentIndex = -1);
    /// <summary>
    /// Published by <see cref="Accessibility.EarconService"/> whenever an earcon
    /// actually plays (after the enable + throttle gates), so a visual channel can
    /// mirror the audio one for deaf/hard-of-hearing users.
    /// <see cref="BlazorClient.Components.VisualEarconOverlay"/> subscribes and — only
    /// when the opt-in "visual earcons" setting is on — shows a brief, single-fade
    /// badge (no strobing; WCAG 2.3.1 stays satisfied by design).
    /// <paramref name="Label"/> is the human-readable event name ("Order filled");
    /// <paramref name="Tone"/> is "positive" | "negative" | "alert" | "neutral" and
    /// drives the badge accent (blue/orange — colorblind-safe by default).
    /// </summary>
    public record EarconVisualEvent(string Label, string Tone);
    /// <summary>
    /// RESERVED / UNUSED. Never published or subscribed. Drawing placement actually flows through
    /// <see cref="AddDrawingEvent"/>: <see cref="Input.CommandDispatcher"/> publishes one
    /// <c>AddDrawingEvent</c> each time the user re-presses a drawing shortcut, and
    /// <see cref="ChartCommandManager"/> calls <c>HandleAddDrawing</c> once per press — the
    /// <see cref="Accessibility.DrawingInteractionManager"/> state machine advances one anchor per call.
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

    public record AlertFiredEvent(AlertFired Alert)
    {
        /// <summary>The symbol the alert fired against (from <see cref="AlertFired.Symbol"/>),
        /// surfaced on the event for delivery payloads + per-asset webhook routing.</summary>
        public string? Symbol => Alert.Symbol;
    }

    // ── UI Modal Events ───────────────────────────────────────────────────────
    public record OpenAlertsEvent();
    public record OpenStrategiesEvent();
    public record OpenCustomScriptsEvent();
    public record OpenSoundDesignerEvent();
    public record OpenAIAnalystEvent();
    public record OpenSaveWorkspaceEvent();
    public record OpenLoadWorkspaceEvent();
    public record OpenJournalEvent();

    /// <summary>Ctrl+Alt+Shift+M — ask the background monitoring service to speak its
    /// status summary ("Monitoring 3 workspaces: KAS current, 1 strategy. ...").</summary>
    public record AnnounceMonitoringStatusEvent();
    /// <summary>
    /// Fired by any modal when it opens (IsOpen=true) or closes (IsOpen=false).
    /// MainPage subscribes to hide/restore the native SkiaSharp canvas so modals
    /// are visually accessible even when the Skia layer is rendered on top.
    /// <para>
    /// <paramref name="ModalName"/>, when non-null, is announced via the ARIA
    /// live region so blind users hear which modal opened (e.g. "Help modal
    /// opened") rather than only the focus-moved heading. Legacy call sites
    /// that haven't been updated yet use the one-arg constructor and pass null.
    /// </para>
    /// </summary>
    public record ModalStateChangedEvent(bool IsOpen, string? ModalName = null);

    /// <summary>
    /// Published by <see cref="CommandDispatcher"/> when the user presses Escape with
    /// at least one modal open. <see cref="ModalName"/> identifies the topmost open
    /// modal — the dispatcher maintains a stack of <see cref="ModalStateChangedEvent"/>
    /// names and peeks the top on Escape. Each modal subscribes once at OnInitialized
    /// and self-closes only when both <c>_isVisible == true</c> and
    /// <c>e.ModalName == thisModalName</c>, so stacked modals close one-at-a-time.
    ///
    /// Without a single dispatcher case, every modal would have to re-implement Escape
    /// handling on its own keydown surface — error-prone and inconsistent (the audit
    /// found HelpModal's Escape silently failed on 2026-04-27 e18).
    /// </summary>
    public record CloseTopModalEvent(string? ModalName);

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

    /// <summary>Raised when the user toggles paper trading mode in settings.</summary>
    public record PaperModeToggledEvent(bool Enabled);

    /// <summary>Raised when the user toggles braille / tactile-display output in settings.</summary>
    public record BrailleModeToggledEvent(bool Enabled);

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
        AccessibleTrader.Sdk.Strategies.ResolvedRiskPlan ResolvedPlan,
        string Symbol = "");

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
        int BarsSinceFirstConfirm,
        string Symbol = "");

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
        bool SetupStillActive,
        string Symbol = "");

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
        AccessibleTrader.Sdk.Strategies.ResolvedRiskPlan ResolvedPlan,
        string Symbol = "");

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
        int BarsArmed,
        string Symbol = "");
}

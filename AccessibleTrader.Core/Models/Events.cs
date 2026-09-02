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
        bool IsJump = false,
        // Overrides the mute tier this message's Type would otherwise get.
        //
        // Every publisher used to inherit its channel from its FeedbackType, and for the
        // chart that is right: a zoom readout is Manual because zooming is something you
        // asked for. It is wrong for the one dialog that spends money. The live-order
        // review and the placement outcome were published as StateChange and Info, both
        // of which land on Manual — the tier F2 silences — while a REJECTION rode Error
        // to Critical and could not be silenced. So with speech off the terminal spoke
        // every refusal and no confirmation, and the pre-submit readback for a real-money
        // order said nothing at all.
        //
        // SpeechChannel.OrderEvent already exists for exactly this ("the one feedback you
        // never miss", FeedbackRouters.cs) and every ASYNCHRONOUS order outcome — fill,
        // partial, stop, take-profit, reject, cancel, expiry, replace — already uses it.
        // The synchronous ones could not reach it because this record had no way to say so.
        // Null keeps the Type's own default, so nothing that does not opt in changes.
        Services.Accessibility.SpeechChannel? Channel = null);

    public record ChartFocusEvent();

    /// <summary>
    /// Asks <c>ChartArea</c> to programmatically move
    /// keyboard focus to the chart element. Published by <see cref="Services.Input.CommandDispatcher"/>
    /// when the user presses Ctrl+Alt+Shift+C, and by <c>ModalBase.CloseModal</c>
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

    /// <summary>Alt+I. Carries nothing: the dossier reads the ACTIVE chart's identity, so there is
    /// no second symbol selection to drift out of sync with what the user is looking at.</summary>
    public record OpenAssetDossierEvent();
    public record OpenHelpEvent();
    public record OpenApiKeysEvent();

    /// <summary>
    /// An API key was saved, activated or removed.
    ///
    /// <para>
    /// Raised so the market cascade can recompute. Adding a key configures the
    /// provider, but the symbol list had already been filled with the "API key
    /// required" sentinel and nothing recomputed it — so the dropdown went on
    /// telling the user to add a key they had just added, and the only way out was
    /// to restart the app.
    /// </para>
    /// </summary>
    public record ApiKeysChangedEvent(string Provider);

    /// <summary>
    /// Open the deposit-address dialog. Only ever raised for a provider that
    /// implements <c>IWalletProvider</c>; equity brokers have no wallet and the
    /// button that raises this is absent for them.
    /// </summary>
    public record OpenWalletEvent();

    /// <summary>
    /// Open the withdrawal dialog. Only ever raised when
    /// <c>WithdrawalService.CanWithdrawAsync</c> is true — the provider implements
    /// <c>IWithdrawalProvider</c> AND a withdrawal-enabled key profile exists. The
    /// button that raises this is absent otherwise, so the dialog never opens onto
    /// a refusal it could have predicted.
    /// </summary>
    public record OpenWithdrawEvent();
    public record OpenOrderBookEvent();
    public record OpenAddIndicatorEvent();
    public record OpenDrawingToolsEvent();
    public record OpenPropertiesEvent(string? SeriesId = null);
    public record DeleteSeriesEvent(string? SeriesId = null);
    /// <summary>Ctrl+Z — reverse the last recorded chart edit.</summary>
    public record UndoChartEditEvent();
    /// <summary>Ctrl+Y — re-apply the last undone chart edit.</summary>
    public record RedoChartEditEvent();
    public record AddDrawingEvent(string DrawingType);
    public record CancelDrawingEvent();
    /// <summary>Place the next anchor of the in-progress drawing at the current cursor
    /// bar — lets a touch-only user complete a multi-point drawing without a keyboard.</summary>
    public record PlaceDrawingAnchorEvent();
    /// <summary>
    /// Fired by <see cref="Services.Accessibility.DrawingInteractionManager"/> when a right-click lands
    /// on an existing drawing's anchor handle. <c>DrawingContextMenu</c>
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
    /// Published by <see cref="Services.Accessibility.EarconService"/> whenever an earcon
    /// actually plays (after the enable + throttle gates), so a visual channel can
    /// mirror the audio one for deaf/hard-of-hearing users.
    /// <c>VisualEarconOverlay</c> subscribes and — only
    /// when the opt-in "visual earcons" setting is on — shows a brief, single-fade
    /// badge (no strobing; WCAG 2.3.1 stays satisfied by design).
    /// <paramref name="Label"/> is the human-readable event name ("Order filled");
    /// <paramref name="Tone"/> is "positive" | "negative" | "alert" | "neutral" and
    /// drives the badge accent (blue/orange — colorblind-safe by default).
    /// </summary>
    public record EarconVisualEvent(string Label, string Tone);
    /// <summary>Settings changed the touch-bar visibility mode ("auto"/"show"/"hide") —
    /// TouchNavBar re-evaluates immediately, no modal close or restart needed.</summary>
    public record TouchNavBarModeChangedEvent(string Mode);
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
    public record OpenMyDataEvent();
    /// <summary>Alt+M — open the market-watch modal (watchlist management + screener).</summary>
    public record OpenWatchlistEvent();
    /// <summary>Alt+R — open the respect report (ranked levels and moving averages).</summary>
    public record OpenLevelReportEvent();

    /// <summary>Opens the theme editor. A null id starts a new theme from the one in use.</summary>
    public record OpenThemeEditorEvent(string? PresetId = null);

    /// <summary>Asks the UI to collect the text for a Text Label that has just been placed.</summary>
    public record PromptForLabelTextEvent(string SeriesId);

    /// <summary>Carries the text back, or an empty string when the user cancelled.</summary>
    public record LabelTextEnteredEvent(string SeriesId, string Text);

    /// <summary>Bar-replay transport verbs.</summary>
    public enum ReplayCommand { Toggle, StepForward, StepBack, PlayPause }

    /// <summary>
    /// One-shot replay transport command. Goes over the EventBus rather than a direct call so
    /// <c>CommandDispatcher</c> keeps no dependency on the replay service — the same routing
    /// rule every other keyboard-driven feature follows.
    /// </summary>
    public record ReplayCommandEvent(ReplayCommand Command);

    /// <summary>Split-view verbs.</summary>
    public enum SplitViewCommand { Toggle, CycleSecondary, ToggleOrientation }

    /// <summary>One-shot split-view command, routed like the replay transport verbs.</summary>
    public record SplitViewCommandEvent(SplitViewCommand Command);
    /// <summary>Keyboard-driven "Load chart" (Ctrl+Alt+Shift+L) — the Toolbar owns the
    /// selection state and pre-flight warning, so the dispatcher just asks it to load.</summary>
    public record LoadChartRequestedEvent();
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
    /// Published by <see cref="Services.Input.CommandDispatcher"/> when the user presses Escape with
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
    /// <summary>An order left the book without filling — cancelled by the user,
    /// expired, or (on polled brokers, where the two are indistinguishable)
    /// rejected upstream. Announced so no order ever disappears silently.</summary>
    public record OrderCancelledEvent(OrderUpdate Order);
    /// <summary>The order's time-in-force ran out (IOC/FOK remainder, day order
    /// at the close). Not a cancel — nobody asked — and not a rejection — the
    /// venue accepted it. Announced distinctly so the trader knows their intent
    /// lapsed rather than was refused.</summary>
    public record OrderExpiredEvent(OrderUpdate Order);
    /// <summary>The order was modified and is STILL LIVE under a new id. Must
    /// never be announced as cancelled: a trader who hears "cancelled" believes
    /// they are flat, re-enters, and is double-sized with the original resting.</summary>
    public record OrderReplacedEvent(OrderUpdate Order);
    public record MarginWarningEvent(string Symbol, double MarginLevel, string Message);

    /// <summary>
    /// A background workspace monitor finished a poll and has fresh bars for a
    /// chart the user is NOT currently looking at.
    ///
    /// <para>
    /// The monitors were fetching these bars all along and spending them only on
    /// alerts and strategies. The paper broker listened to the focused chart
    /// alone, so a resting order in any other tab could not fill and an open
    /// position there reported a frozen price — the case where a trader is side
    /// tracked and forgets an open position is exactly the case that broke.
    /// </para>
    /// </summary>
    public record MonitoredBarEvent(ChartIdentity Identity, Ohlcv Latest);

    /// <summary>Raised when the user toggles paper trading mode in settings.</summary>
    public record PaperModeToggledEvent(bool Enabled);

    /// <summary>Raised when the user toggles braille / tactile-display output in settings.</summary>
    public record BrailleModeToggledEvent(bool Enabled);
    /// <summary>F4 pressed: flip braille output. Handled by TactileCanvasCoordinator,
    /// which owns the setting, the platform check, and the spoken confirmation.</summary>
    public record BrailleToggleRequestedEvent();

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

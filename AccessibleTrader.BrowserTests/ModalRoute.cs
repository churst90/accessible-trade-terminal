namespace AccessibleTrader.BrowserTests;

/// <summary>How a modal is opened from a cold start.</summary>
internal enum OpenBy
{
    /// <summary>A keyboard chord sent to the document, as a keyboard-first user would.</summary>
    Shortcut,
    /// <summary>A click on a toolbar button, located by its accessible name.</summary>
    ToolbarButton,
}

/// <summary>
/// One route into one dialog, plus what SHOULD happen when you take it.
///
/// <para>
/// <c>ExpectedFocusId</c> is the point of the whole file. A2 (2026-08-26) showed that the
/// existing bUnit contract asserts focus went <em>somewhere valid</em> — it passes when a modal's
/// own <c>focusElement</c> call is deleted, because <c>ModalBase.ShowModalAsync</c> already
/// focused the heading. Declaring the target here, by hand, turns "a focus call happened" into
/// "the user is standing on the amount field", which is the thing a screen-reader user actually
/// experiences.
/// </para>
///
/// <para>
/// The declarations are deliberately written from what each dialog is FOR, not copied from what
/// its code currently does — a table generated from the source would agree with the source by
/// construction and could never disagree with it.
/// </para>
/// </summary>
internal sealed record ModalRoute(
    string Modal,
    OpenBy How,
    string Trigger,
    string ExpectedFocusId,
    string Why,
    bool NeedsChartFocus = false,
    bool ColdStartReachable = true)
{
    public string Name => $"{Modal} via {(How == OpenBy.Shortcut ? Trigger : "toolbar")}";
}

internal static class ModalRoutes
{
    /// <summary>
    /// The eight dialogs a keyboard user can reach without touching the toolbar. Chords are in
    /// Playwright syntax; they are the same bindings <c>ShortcutManager.InitializeDefaultProfile</c>
    /// installs, written out by hand so a change to that table shows up here as a failure rather
    /// than being silently mirrored.
    /// </summary>
    public static readonly IReadOnlyList<ModalRoute> Keyboard = new ModalRoute[]
    {
        new("HelpModal",             OpenBy.Shortcut, "F1",
            "help-title",           "F1 is the universal help key; landing on the heading names the dialog."),
        new("SettingsModal",         OpenBy.Shortcut, "F12",
            "settings-title",       "Settings opens on its heading, with the tablist one Tab away."),
        // Chart-scoped, and that is a deliberate design decision rather than an oversight:
        // CommandDispatcher.IsChartScopedCommand lists OpenProperties as "the F-key exception",
        // because Properties acts on the focused series. The first survey pressed Shift+F12 with
        // focus on <body> and reported the dialog as broken — it was the route declaration that
        // was wrong. Recorded here so the next reader does not re-file it.
        //
        // Not cold-start reachable for a SECOND reason: PropertiesModal.ShowAsync resolves the
        // focused series and returns silently when there is none, so on a fresh install the
        // keystroke does nothing and says nothing. That silence is filed as an A3 finding; the
        // route is excluded from the contract theories rather than left failing, because what it
        // would be reporting is "there is no series", not "the dialog is broken".
        new("PropertiesModal",       OpenBy.Shortcut, "Shift+F12",
            "props-title",          "Properties of the focused series.",
            NeedsChartFocus: true, ColdStartReachable: false),
        new("ObjectTreeModal",       OpenBy.Shortcut, "Alt+o",
            "objtree-title",        "Alt+O — the series/object tree."),
        new("TradingDashboardModal", OpenBy.Shortcut, "Alt+t",
            "trade-title",          "Alt+T. THE regression case: it opened without moving focus at all until 2026-08-25."),
        new("OrderBookModal",        OpenBy.Shortcut, "Alt+b",
            "orderbook-title",      "Alt+B — depth of market."),
        new("ApiKeysModal",          OpenBy.Shortcut, "Alt+k",
            "apikeys-title",        "Alt+K — credential store."),
        new("DrawingToolsModal",     OpenBy.Shortcut, "Alt+d",
            "drawing-tools-title",  "Alt+D — drawing tool picker."),
    };

    /// <summary>
    /// Every dialog reachable by clicking a toolbar button, keyed by the button's accessible
    /// name — which is what a screen-reader user hears and therefore the only honest selector.
    /// </summary>
    public static readonly IReadOnlyList<ModalRoute> Toolbar = new ModalRoute[]
    {
        new("ObjectTreeModal",       OpenBy.ToolbarButton, "Object tree", "objtree-title",         "heading names the dialog"),
        // Drawings moved to the INDICATOR BAR under the chart on 2026-09-06. The route is
        // unchanged because it is keyed by accessible name and the name did not move with it —
        // which is the argument for naming a control by what it is rather than by where it sits.
        new("DrawingToolsModal",     OpenBy.ToolbarButton, "Drawings",                 "drawing-tools-title",   "heading names the dialog"),
        new("SoundDesignerModal",    OpenBy.ToolbarButton, "Sound designer",          "sound-designer-title",  "heading names the dialog"),
        new("TradingDashboardModal", OpenBy.ToolbarButton, "Trade dashboard",          "trade-title",           "heading names the dialog"),
        // BACK, 2026-09-06: the button is ungated again. It was gated on the provider having a
        // book for one day, which left Alt+B opening a dialog whose button had vanished — and
        // this harness loads ZERO providers (every plugin DLL under bin/ is refused by the trust
        // allow-list, measured: "Total Unique Loaded Data Providers: 0"), so the button was
        // absent here too. It is present now, and the dialog it opens says the venue publishes
        // no depth, which is a perfectly good thing for this route to arrive at: the contract
        // theories assert the heading and where focus lands, not what the panel contains.
        new("OrderBookModal",        OpenBy.ToolbarButton, "Order book",              "orderbook-title",       "heading names the dialog"),
        new("StrategyModal",         OpenBy.ToolbarButton, "Strategies",              "strategy-title",        "heading names the dialog"),
        new("WatchlistModal",        OpenBy.ToolbarButton, "Watch lists", "watchlist-title",       "heading names the dialog"),
        new("LevelReportModal",      OpenBy.ToolbarButton, "Levels", "levelreport-title",     "heading names the dialog"),
        new("JournalModal",          OpenBy.ToolbarButton, "Trade journal",           "journal-title",         "heading names the dialog"),
        new("AIAnalystModal",        OpenBy.ToolbarButton, "AI analyst",              "ai-analyst-title",      "heading names the dialog"),
        new("AlertsModal",           OpenBy.ToolbarButton, "Alerts",                  "alerts-title",          "heading names the dialog"),
        new("ApiKeysModal",          OpenBy.ToolbarButton, "API keys",                "apikeys-title",         "heading names the dialog"),
        // Both toolbar buttons are gated on a provider that actually has a wallet, so they are
        // absent from a credential-free session entirely. Kept in the catalog with the gate
        // recorded: a sweep that lists what it covered has to also list what it could not.
        new("WalletModal",           OpenBy.ToolbarButton, "Deposit",         "wallet-asset",
            "money dialog: open on the asset picker, not the title", ColdStartReachable: false),
        new("WithdrawModal",         OpenBy.ToolbarButton, "Withdraw",          "withdraw-asset",
            "money dialog: open on the asset picker, not the title", ColdStartReachable: false),
        new("SaveWorkspaceModal",    OpenBy.ToolbarButton, "Save workspace",          "save-workspace-title",  "heading names the dialog"),
        new("LoadWorkspaceModal",    OpenBy.ToolbarButton, "Load workspace",          "load-workspace-title",  "heading names the dialog"),
        new("SettingsModal",         OpenBy.ToolbarButton, "Settings",                "settings-title",        "heading names the dialog"),
        new("HelpModal",             OpenBy.ToolbarButton, "Help",                    "help-title",            "heading names the dialog"),
        new("MyDataModal",           OpenBy.ToolbarButton, "Import",        "mydata-title",          "heading names the dialog"),
    };

    /// <summary>
    /// Dialogs in the RCL with no route from a cold start, recorded rather than quietly dropped.
    /// A sweep that lists 25 modals and exercises 21 is a sweep that reports 84% as 100%.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NoColdStartRoute =
        new Dictionary<string, string>
        {
            ["AddIndicatorModal"]  = "opened from the indicator bar, which needs a loaded series",
            ["AssetDossierModal"]  = "opened from a watchlist row / chart context menu on a loaded symbol",
            ["CustomScriptsModal"] = "Alt+comma, and the scripting surface needs the script worker",
            ["LabelTextModal"]     = "prompted mid-gesture while placing a Label drawing",
            ["ThemeEditorModal"]   = "opened from the Settings appearance tab, not from the toolbar",
            // Not a *Modal.razor at all — it is inline in Toolbar.razor — so the completeness test
            // would never ask about it. Listed anyway: this is the one alertdialog in the app, the
            // one the Tab trap could not see until 2026-09-02, and the sweep still cannot open it.
            ["Toolbar shape-change warning (alertdialog)"] =
                "shown by Load when the selected provider is analytics-shaped AND the current tab " +
                "holds a non-core series; a cold start has neither a loaded chart nor a network. " +
                "Its trap behaviour is pinned by keyboard-tests.mjs (a fixed, offsetParent-less " +
                "node) and was observed in a standalone Chromium page over CDP.",
        };

    public static IEnumerable<ModalRoute> All => Keyboard.Concat(Toolbar);
}

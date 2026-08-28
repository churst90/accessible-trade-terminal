// M11 / A2 finding F4 — "the Wallet modal opens without moving focus into it".
//
// Deleting WalletModal's own
//     await JSRuntime.InvokeVoidAsync("accessibleTrader.focusElement", "wallet-asset");
// left the whole suite green, in A2 (2026-08-26) and again in the 2026-08-28 re-measurement.
// ModalAccessibilityContractTests asks "did focus go somewhere valid?" and the answer is still
// yes: ModalBase.ShowModalAsync has already focused the heading, so the *last* focus target
// silently changes from the asset field to the title and every assertion in the file holds.
// Every ModalBase modal has that hole; Wallet is only where the mutant happened to land.
//
// This file asks the other question: did focus land where THIS dialog is supposed to put it.
// That needs a target declared outside the component, which is what DeclaredFocusTargets is —
// the bUnit twin of AccessibleTrader.BrowserTests' ModalRoutes.ExpectedFocusId, and it exists
// separately for a reason: the browser harness can only reach a dialog that has a route from a
// cold start, and seven dialogs do not (Wallet and Withdraw are gated on a provider with a
// wallet; five more need a loaded series or a mid-gesture prompt). Those seven include the two
// money dialogs, which are exactly the ones whose declared target is a field rather than a
// heading. So the browser layer cannot close M11 on its own and this layer can.
//
// THE RULE THE TABLE ENCODES, stated before reading any component, so that the declarations
// can disagree with the code rather than agreeing with it by construction:
//
//   A dialog opens on its own heading — that is what names it to a screen reader — UNLESS the
//   dialog exists to collect one specific value and shows nothing else the user must read
//   first, in which case it opens on that field.
//
// Three dialogs meet the exception (Wallet, Withdraw, LabelText). Where the rule and the code
// disagree, the disagreement is recorded in the Why string rather than being resolved by
// copying the code.

using Bunit;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>Where one dialog must put the keyboard when it opens, and why.</summary>
public sealed record DeclaredFocusTarget(string Modal, string ElementId, string Why);

public class ModalFocusTargetContractTests
{
    public static readonly IReadOnlyList<DeclaredFocusTarget> Declared = new DeclaredFocusTarget[]
    {
        // --- Opens on its heading: the dialog has content to read before acting. ---
        new("AddIndicatorModal",     "modal-title",
            "the indicator picker; the heading names it before the search box below it"),
        new("AIAnalystModal",        "ai-analyst-title",
            "an analysis surface, not a form"),
        new("AlertsModal",           "alerts-title",
            "opens on the list of existing alerts, which must be announced before the editor"),
        new("ApiKeysModal",          "apikeys-title",
            "credential store; the heading names the dialog before any secret field"),
        new("AssetDossierModal",     "dossier-title",
            "a report about one symbol — heading, then the report"),
        new("CustomScriptsModal",    "scripts-title",
            "a script library with a list; the heading names it"),
        new("DrawingToolsModal",     "drawing-tools-title",
            "a tool picker whose heading distinguishes it from the chart underneath"),
        new("HelpModal",             "help-title",
            "F1 is the universal help key; landing on the heading names the dialog"),
        new("JournalModal",          "journal-title",
            "a trade log — the heading, then the entries"),
        new("LevelReportModal",      "levelreport-title",
            "a report; nothing to type"),
        new("LoadWorkspaceModal",    "load-workspace-title",
            "a chooser over a list, not a single-value form: the list is the content"),
        new("MyDataModal",           "mydata-title",
            "file import, multi-step; the heading frames the steps"),
        new("ObjectTreeModal",       "objtree-title",
            "the series/object tree; the heading names it before the tree"),
        new("OrderBookModal",        "orderbook-title",
            "depth of market — a live readout, nothing to type"),
        new("PropertiesModal",       "props-title",
            "properties of the focused series; the heading says which dialog opened"),
        // Reads like the exception below (it exists to collect a name) but is not: it also
        // renders the list of existing profiles to overwrite, and overwriting a saved workspace
        // by typing a name that silently matches one is the mistake worth guarding against.
        // The heading, then the field one Tab away, keeps that list in the reading order.
        new("SaveWorkspaceModal",    "save-workspace-title",
            "collects a name, but the existing-profiles list must be readable before typing"),
        new("SettingsModal",         "settings-title",
            "the heading, with the tablist one Tab away"),
        new("SoundDesignerModal",    "sound-designer-title",
            "an editor with tabs; the heading names it"),
        new("StrategyModal",         "strategy-title",
            "a strategy library with tabs"),
        new("ThemeEditorModal",      "theme-editor-title",
            "an editor over an existing theme"),
        new("TradingDashboardModal", "trade-title",
            "Alt+T. THE regression case: it opened without moving focus at all until 2026-08-25"),
        new("WatchlistModal",        "watchlist-title",
            "watchlists and screener; the heading names which of the two opened"),

        // --- Opens on a field: the dialog exists to collect one value. ---
        new("LabelTextModal",        "label-text-input",
            "prompted mid-gesture while placing a Label: the only thing to do is type the text"),
        new("WalletModal",           "wallet-asset",
            "money dialog. Nothing is retrievable until an asset is named, and the address it "
            + "then shows is what the user pastes into a withdrawal elsewhere — landing on the "
            + "heading costs a Tab on every use and, more to the point, is indistinguishable "
            + "from the modal never having focused anything of its own. THIS IS M11."),
        new("WithdrawModal",         "withdraw-asset",
            "money dialog, same shape as Wallet: the asset picker gates every field below it"),
    };

    private static DeclaredFocusTarget For(string modal) => Declared.Single(d => d.Modal == modal);

    /// <summary>
    /// The kill. Not "focus went somewhere valid" — <em>which</em> element the user is standing
    /// on, against a target this file declares and the component cannot influence.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Dialog_OnOpen_LeavesFocusOnItsDeclaredTarget(string name)
    {
        var declared = For(name);
        using var h = new BlazorTestHarness();
        var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog(name));

        // Vacuity guard, same as the sibling contract: every assertion below is satisfied by a
        // dialog that never opened.
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));

        // A modal may focus twice (heading, then its own field) and the second call arrives from
        // a post-render continuation, so the arrival is waited on rather than sampled — sampling
        // it is a race the renderer wins on a loaded CI box.
        h.WaitForFocus(declared.ElementId, timeoutSeconds: 5);

        var actual = h.FocusedElementIds[^1];
        Assert.True(actual == declared.ElementId,
            $"{name} opened and then left focus on '#{actual}', not on '#{declared.ElementId}'. "
            + $"Why that target: {declared.Why}. Focus calls in order: "
            + string.Join(" -> ", h.FocusedElementIds));

        // The declared target has to be real and reachable, or "focus landed there" is a claim
        // about a JS call that silently did nothing.
        var el = cut.FindAll($"[id='{declared.ElementId}']").SingleOrDefault();
        Assert.True(el != null,
            $"{name} declares '#{declared.ElementId}' as its focus target but no element with "
            + "that id is in its markup.");
        bool nativelyFocusable = el!.TagName.ToLowerInvariant()
            is "input" or "select" or "textarea" or "button" or "a";
        Assert.True(nativelyFocusable || el.HasAttribute("tabindex"),
            $"{name}'s declared target <{el.TagName.ToLowerInvariant()} id='{declared.ElementId}'> "
            + "is neither natively focusable nor carries a tabindex — focusElement no-ops on it.");
    }

    /// <summary>
    /// A declaration table is only worth what it covers. A new dialog enrolled in ModalCatalog
    /// but not declared here would otherwise be exempt from the contract by omission — which is
    /// how M11 survived a suite that already had a focus contract.
    /// </summary>
    [Fact]
    public void EveryCatalogDialogHasADeclaredTarget()
    {
        var enrolled = ModalCatalog.Dialogs.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        var declared = Declared.Select(d => d.Modal).ToHashSet(StringComparer.Ordinal);

        Assert.True(enrolled.Except(declared).Count() == 0,
            "Dialogs in ModalCatalog with no declared focus target: "
            + string.Join(", ", enrolled.Except(declared).Order())
            + ". Declare where each one should put the keyboard, and why.");

        Assert.True(declared.Except(enrolled).Count() == 0,
            "Focus targets declared for dialogs that are no longer in ModalCatalog: "
            + string.Join(", ", declared.Except(enrolled).Order())
            + ". A stale declaration is never asserted against anything.");
    }

    /// <summary>
    /// The vacuity check on the table itself. If every declaration were a heading id, the
    /// contract would collapse back into "ModalBase focused the heading" — which is precisely
    /// the assertion M11 walked through. At least one dialog must declare a non-heading target,
    /// and Wallet is the one the mutant lands on.
    /// </summary>
    [Fact]
    public void TheTableDeclaresTargetsThatAreNotJustHeadings()
    {
        var fields = Declared.Where(d => !d.ElementId.EndsWith("title", StringComparison.Ordinal))
                             .Select(d => d.Modal).ToList();

        Assert.Contains("WalletModal", fields);
        Assert.True(fields.Count >= 3,
            "Only " + fields.Count + " dialog(s) declare a target other than their heading. If "
            + "that ever reaches zero this suite proves nothing beyond ModalBase's own call: "
            + "declared " + string.Join(", ", fields));
    }

    public static TheoryData<string> Names => ModalCatalog.DialogNames;
}

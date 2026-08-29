// The ShowModalAsync dead-region sweep, stated as tests.
//
// THE HISTORY. Until 2026-08-28, bUnit's planned void handler for `accessibleTrader.focusElement`
// recorded its invocation and never completed it, so `await ShowModalAsync(...)` PARKED — and
// every line a modal ran after that await was dead code in every bUnit test in the suite. M11
// (WalletModal's second focus call) was one consequence. The open question left on the queue was
// the rest of it: what else lives after that await with no test that could fail?
//
// THE SWEEP'S ANSWER. Thirteen modals call `await ShowModalAsync(...)`. Six do work afterwards.
// Three of those six move focus a second time (LabelText, Wallet, Withdraw) and are already
// pinned by ModalFocusTargetContractTests' declared per-modal targets. The other three LOAD THE
// DIALOG'S CONTENT, and nothing anywhere asserted that the load happens at all:
//
//   AssetDossierModal  → await RefreshAsync()   → IAssetDossierService.BuildAsync
//   LevelReportModal   → await RefreshAsync()   → the level/MA measurement
//   AIAnalystModal     → await RunAnalysis()    → IAIAnalystService.AnalyseAsync
//
// Delete any one of those lines and the dialog opens permanently empty. For a sighted user that
// is a blank panel; for a blind user it is a dialog that announces its title and then has nothing
// to read, with no error and no clue that a fetch was ever meant to happen. The suite was green
// against all three.
//
// (ModalCatalog's own comment said AssetDossierModal was "the only one" that loads on open. It
// was written from the CI incident, not from a sweep, and it is two short. Corrected there.)
//
// Each test here is proved by deleting the post-await line it guards and watching it go red.

using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class ModalOpenLoadsItsContentTests
{
    [Fact]
    public void AssetDossierModal_BuildsTheDossier_WhenItOpens()
    {
        using var h = new BlazorTestHarness();
        var dossier = h.Ctx.Services.GetRequiredService<IAssetDossierService>();
        dossier.BuildAsync(Arg.Any<string>(), Arg.Any<string>(),
                           Arg.Any<IReadOnlyList<AccessibleTrader.Sdk.Models.Ohlcv>?>(),
                           Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(new AssetDossier(
                   "BTC/USD", "Crypto", "Headline for the dossier",
                   Array.Empty<DossierSection>(), Array.Empty<DossierFlag>(), DateTime.UtcNow)));

        var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog("AssetDossierModal"));

        cut.WaitForAssertion(() =>
        {
            dossier.Received().BuildAsync(Arg.Any<string>(), Arg.Any<string>(),
                                          Arg.Any<IReadOnlyList<AccessibleTrader.Sdk.Models.Ohlcv>?>(),
                                          Arg.Any<CancellationToken>());
            // And the answer reached the screen, not just the service. Asserting only the call
            // would pass against a modal that fetched and then rendered nothing.
            Assert.Contains("Headline for the dossier", cut.Markup);
        });
    }

    [Fact]
    public void AIAnalystModal_RunsTheAnalysis_WhenItOpens()
    {
        using var h = new BlazorTestHarness();
        var ai = h.Ctx.Services.GetRequiredService<IAIAnalystService>();
        ai.AnalyseAsync().Returns(Task.FromResult<string?>("The market is doing a thing."));

        var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog("AIAnalystModal"));

        cut.WaitForAssertion(() =>
        {
            ai.Received().AnalyseAsync();
            Assert.Contains("The market is doing a thing.", cut.Markup);
        });
    }

    [Fact]
    public void LevelReportModal_MeasuresWhenItOpens_AndSaysWhyItCannot()
    {
        // The harness's seeded chart carries no bars, so the measurement takes its
        // not-enough-history branch — which is exactly what makes this assertable without
        // building 60 candles: that sentence is written BY RefreshAsync and by nothing else, so
        // its presence is proof the load ran, and its wording is what a user hears instead of
        // silence when the chart is too short to measure.
        using var h = new BlazorTestHarness();

        var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog("LevelReportModal"));

        cut.WaitForAssertion(() =>
            Assert.Contains("not enough history to measure anything", cut.Markup));
    }
}

// Does disposing a modal while its open is still loading leak its subscriptions?
//
// The question is not academic and it is not hypothetical: it is the first hypothesis for a CI-only
// failure on 2026-08-28. `ModalDisposeLeakTests` went red for AssetDossierModal on the runner —
// "leaked EventBus subscriptions after dispose: CloseTopModalEvent, OpenAssetDossierEvent" — in the
// first push after `BlazorTestHarness` started completing its `focusElement` stub. That change made
// `await ShowModalAsync(...)` return for the first time, so `AssetDossierModal.ShowAsync`'s second
// line — `await RefreshAsync()`, the only data load any catalog modal performs on open — began
// running in tests at all. Disposing the renderer out from under an in-flight continuation was the
// obvious suspect.
//
// THIS FILE REFUTES THAT. The load is stretched with a real delay so the dispose lands squarely in
// the middle of it, and the subscriptions come back released every time, locally, in both configs.
// So the mechanism is sound and the CI red is something else — it was NOT reproduced locally under
// repeated runs or under CPU contention, and it is recorded as unexplained rather than as fixed.
// `ModalCatalog.OpenDialog` now settles the dispatcher before returning, which removes one source
// of nondeterminism from every catalog suite; that is a narrowing, not a diagnosis.
//
// The test is kept because the property it states is worth holding on its own. A user closing a
// workspace tab while the dossier is still building is an ordinary Tuesday, and a modal that
// survives that as a live subscriber goes on handling events on a dead component.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class ModalDisposeDuringLoadTests
{
    private static List<string> TypesWithObservers(EventBus bus)
    {
        var field = typeof(EventBus).GetField("_subjects", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field); // EventBus internals moved — update this helper
        var subjects = (ConcurrentDictionary<Type, object>)field!.GetValue(bus)!;

        var leaked = new List<string>();
        foreach (var (evtType, subject) in subjects)
        {
            var hasObservers = (bool)subject.GetType()
                .GetProperty("HasObservers", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(subject)!;
            if (hasObservers) leaked.Add(evtType.Name);
        }
        return leaked;
    }

    /// <param name="delayMs">
    /// 0 is the shape the suite already runs (the substitute answers immediately). 150 puts the
    /// dispose unambiguously inside the load — long enough that no amount of dispatcher draining
    /// can have finished it, so this really is teardown-under-flight and not teardown-after.
    /// </param>
    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    public void ADossierDisposedMidLoadStillReleasesItsSubscriptions(int delayMs)
    {
        using var h = new BlazorTestHarness();

        var dossier = (IAssetDossierService)h.Ctx.Services.GetService(typeof(IAssetDossierService))!;
        dossier.BuildAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Ohlcv>?>())
               .Returns(async _ => { await Task.Delay(delayMs); return (AssetDossier?)null!; });

        ModalCatalog.OpenDialog(h, ModalCatalog.Dialog("AssetDossierModal"));

        var bus = Assert.IsType<EventBus>(h.EventBus);
        // Vacuity: it has to be subscribed to something while alive, or the release below is
        // asserting that nothing became nothing.
        Assert.NotEmpty(TypesWithObservers(bus));

        h.Ctx.Dispose();

        var sw = Stopwatch.StartNew();
        List<string> leaked;
        while ((leaked = TypesWithObservers(bus)).Count > 0 && sw.ElapsedMilliseconds < 2000)
            Thread.Sleep(10);

        Assert.True(leaked.Count == 0,
            $"AssetDossierModal was disposed {delayMs} ms into its load and left these EventBus "
            + $"subscriptions live: {string.Join(", ", leaked)}. A publish on any of them now "
            + "invokes a handler on a dead component.");
    }
}

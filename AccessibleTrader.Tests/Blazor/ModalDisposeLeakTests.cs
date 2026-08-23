// Dispose-leak sweep over every catalog component.
//
// The failure mode: a component subscribes to the EventBus in OnInitialized and
// forgets to dispose the token (or overrides Dispose without calling
// base.Dispose(), or never implements IDisposable at all). The handler then
// outlives the component; the next event invokes it against a dead component —
// historically a silent no-op in production and an intermittent
// ObjectDisposedException in tests.
//
// Rather than publishing events post-dispose and waiting to observe nothing
// (a fixed-delay negative assertion — the flake class this repo bans), we assert
// the structural fact directly: EventBus is Subject<T>-backed, so after the
// renderer tears the component tree down, every subject must report zero
// observers. No observers ⇒ no handler can ever run.
//
// bUnit 1.40 notes, verified empirically before this file was written:
//   - TestContext.DisposeComponents() does NOT dispose component instances —
//     subscriptions survive it indefinitely. Do not use it here.
//   - TestContext.Dispose() DOES dispose the whole tree (children included),
//     but completes asynchronously on the renderer's dispatcher (~10 ms).
//     So we wait FOR the expected zero-observer state — a positive-direction
//     wait per the suite's WaitFor convention — with a hard bound that only a
//     real leak ever reaches.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Microsoft.AspNetCore.Components;

namespace AccessibleTrader.Tests.Blazor;

public class ModalDisposeLeakTests
{
    /// <summary>Event types on <paramref name="bus"/> that still have live
    /// observers, via reflection over EventBus._subjects → Subject&lt;T&gt;.HasObservers.</summary>
    private static List<string> TypesWithObservers(EventBus bus)
    {
        var field = typeof(EventBus).GetField("_subjects", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field); // EventBus internals moved — update this reflection helper
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

    private static List<string> WaitForRelease(EventBus bus)
    {
        var sw = Stopwatch.StartNew();
        List<string> leaked;
        while ((leaked = TypesWithObservers(bus)).Count > 0 && sw.ElapsedMilliseconds < 2000)
            Thread.Sleep(10);
        return leaked;
    }

    private static void AssertNoLeakAfterTeardown(BlazorTestHarness h, string name)
    {
        var bus = Assert.IsType<EventBus>(h.EventBus);

        h.Ctx.Dispose(); // renderer teardown → disposes the whole component tree

        var leaked = WaitForRelease(bus);
        Assert.True(leaked.Count == 0,
            $"{name} leaked EventBus subscriptions after dispose: {string.Join(", ", leaked)}. " +
            "A publish on any of these now invokes a handler on a dead component.");
    }

    [Theory]
    [MemberData(nameof(DialogNames))]
    public void Dialog_DisposeReleasesEveryEventBusSubscription(string name)
    {
        using var h = new BlazorTestHarness();
        // Open before disposing: ShowModalAsync arms extra subscriptions
        // (CloseTopModalEvent) beyond OnInitialized, and an opened modal is the
        // state a real workspace teardown disposes from.
        ModalCatalog.OpenDialog(h, ModalCatalog.Dialog(name));

        // Vacuity: an enrolled dialog must be subscribed to SOMETHING while
        // alive (its Open* event at minimum), or this sweep is not exercising it.
        var bus = Assert.IsType<EventBus>(h.EventBus);
        Assert.True(TypesWithObservers(bus).Count > 0,
            $"{name} rendered without any EventBus subscription — this sweep is not exercising it.");

        AssertNoLeakAfterTeardown(h, name);
    }

    [Theory]
    [MemberData(nameof(BareNames))]
    public void BareComponent_DisposeReleasesEveryEventBusSubscription(string name)
    {
        // No subscription-count vacuity here: bars may legitimately only publish.
        using var h = new BlazorTestHarness();
        var c = ModalCatalog.Bare(name);
        c.Seed?.Invoke(h);
        c.Render(h.Ctx);
        AssertNoLeakAfterTeardown(h, name);
    }

    /// <summary>A component with the exact bug this sweep exists to catch:
    /// subscribes in OnInitialized, implements no IDisposable.</summary>
    private sealed class LeakyComponent : ComponentBase
    {
        [Inject] public IEventBus Bus { get; set; } = null!;
        protected override void OnInitialized() =>
            Bus.Subscribe<ModalStateChangedEvent>(_ => { });
    }

    /// <summary>Proves the detector fires — guard tests are only trusted
    /// red-first in this repo. LeakyComponent's token is held by nothing that
    /// teardown can reach, so it can never be released; the sweep must still
    /// report it after the full teardown wait.</summary>
    [Fact]
    public void Detector_ReportsALeakedSubscription()
    {
        using var h = new BlazorTestHarness();
        var bus = Assert.IsType<EventBus>(h.EventBus);
        h.Ctx.RenderComponent<LeakyComponent>();
        Assert.Contains(nameof(ModalStateChangedEvent), TypesWithObservers(bus));

        h.Ctx.Dispose();

        Assert.Contains(nameof(ModalStateChangedEvent), WaitForRelease(bus));
    }

    public static TheoryData<string> DialogNames => ModalCatalog.DialogNames;
    public static TheoryData<string> BareNames => ModalCatalog.BareNames;
}

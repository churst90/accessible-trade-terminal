// Escape-to-close regression tests. CommandDispatcher reroutes Escape to
// CloseTopModalEvent({topmost name}); every modal must self-close on a name
// match. The ModalBase-derived modals lost this on 2026-07-15 by overriding
// OnInitialized without calling base.OnInitialized() (the CloseTopModalEvent
// subscription lived only there) — SaveWorkspaceModal was the user-visible
// case. ModalBase now also arms the subscription from ShowModalAsync, and
// these tests pin the behaviour for each ModalBase modal.

using AccessibleTrader.Core.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class ModalEscapeCloseTests
{
    [Fact]
    public void SaveWorkspaceModal_ClosesOnCloseTopModalEvent()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceLibrary.GetAvailableProfiles().Returns(new List<string>());

        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.SaveWorkspaceModal>(
            bus => bus.Publish(new OpenSaveWorkspaceEvent()));
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));

        cut.InvokeAsync(() => h.EventBus.Publish(new CloseTopModalEvent("Save workspace")));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")));
    }

    [Fact]
    public void LoadWorkspaceModal_ClosesOnCloseTopModalEvent()
    {
        using var h = new BlazorTestHarness();
        h.With(Substitute.For<AccessibleTrader.Core.Services.IWorkspaceInitializer>());
        h.WorkspaceLibrary.GetAvailableProfiles().Returns(new List<string>());

        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.LoadWorkspaceModal>(
            bus => bus.Publish(new OpenLoadWorkspaceEvent()));
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));

        cut.InvokeAsync(() => h.EventBus.Publish(new CloseTopModalEvent("Load workspace")));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")));
    }

    [Fact]
    public void SaveWorkspaceModal_IgnoresCloseEventForOtherModal()
    {
        // Stacked-modal safety: only the topmost (named) modal may close.
        using var h = new BlazorTestHarness();
        h.WorkspaceLibrary.GetAvailableProfiles().Returns(new List<string>());

        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.SaveWorkspaceModal>(
            bus => bus.Publish(new OpenSaveWorkspaceEvent()));

        // Blocking, not WaitForAssertion: this is a NEGATIVE assertion (a mismatched modal
        // name must not close this dialog). A wait would pass instantly on the dialog that
        // is still there and prove nothing about whether the close lands a moment later.
        // Settling the dispatch first makes "still open" a real claim.
        cut.InvokeAsync(() => h.EventBus.Publish(new CloseTopModalEvent("Help"))).GetAwaiter().GetResult();

        Assert.NotEmpty(cut.FindAll("[role='dialog']"));
    }
}

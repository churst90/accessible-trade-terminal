// Enter-to-save regression tests.
//
// SaveWorkspaceModal's Enter handler used to be `await Task.Run(() => Save())`.
// Save() calls Close() → ModalBase.CloseModal() → StateHasChanged(), which asserts
// Blazor dispatcher affinity — so on the thread pool it threw. An unhandled
// exception out of an `async Task` event handler is fatal to a WebHost circuit:
// chart, tabs and unsaved layout all gone, with nothing spoken to explain it.
//
// The @onclick path never had the wrapper, so ONLY the Enter key was broken —
// which is exactly the path a keyboard-only user takes, and exactly the reason it
// survived review. These tests exercise the key, not the button.

using AccessibleTrader.Core.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class SaveWorkspaceEnterKeyTests
{
    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.SaveWorkspaceModal>
        OpenWith(BlazorTestHarness h)
    {
        h.WorkspaceLibrary.GetAvailableProfiles().Returns(new List<string>());
        return h.OpenModal<AccessibleTrader.BlazorClient.Components.SaveWorkspaceModal>(
            bus => bus.Publish(new OpenSaveWorkspaceEvent()));
    }

    [Fact]
    public void Enter_SavesTheWorkspace()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenWith(h);

        var input = cut.Find("#workspace-name-input");
        input.Change("BTC Scalping Setup");
        input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        h.WorkspaceLibrary.Received(1).SaveWorkspaceProfile("BTC Scalping Setup", Arg.Any<AccessibleTrader.Core.Services.IWorkspaceStore>());
    }

    [Fact]
    public void Enter_ClosesTheModal_WithoutTearingDownTheCircuit()
    {
        // The failure mode was not "save did not happen" — it was the dispatcher
        // exception thrown while CLOSING. So the close is the assertion that matters:
        // reaching an empty dialog means StateHasChanged ran on the right thread.
        using var h = new BlazorTestHarness();
        var cut = OpenWith(h);

        var input = cut.Find("#workspace-name-input");
        input.Change("Layout A");
        input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")));
    }

    [Fact]
    public void Enter_OnAnEmptyName_DoesNothing()
    {
        // Guard against "fix the throw by always closing". An empty name is not a
        // save, and the dialog must stay up rather than silently discarding the
        // keystroke — a keyboard user gets no other signal that nothing happened.
        using var h = new BlazorTestHarness();
        var cut = OpenWith(h);

        cut.Find("#workspace-name-input")
           .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        h.WorkspaceLibrary.DidNotReceive().SaveWorkspaceProfile(Arg.Any<string>(), Arg.Any<AccessibleTrader.Core.Services.IWorkspaceStore>());
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));
    }

    [Fact]
    public void Escape_StillCloses()
    {
        // The other branch of the same handler, so a change to one cannot quietly
        // drop the other.
        using var h = new BlazorTestHarness();
        var cut = OpenWith(h);

        cut.Find("#workspace-name-input")
           .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")));
    }
}

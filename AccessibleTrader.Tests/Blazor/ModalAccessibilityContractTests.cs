// The dialog accessibility contract, asserted for every dialog in ModalCatalog.
//
// Before this suite, BlazorTestHarness answered accessibleTrader.focusElement
// with a blind void stub, so a modal could ask focus to move to a non-existent
// element — or nowhere at all — and every test stayed green. bUnit records the
// stubbed invocations, so these tests assert WHERE focus was sent and that the
// target actually exists in the rendered markup and is focusable.
//
// Contract per dialog, on open via its Open* event:
//   1. It renders a [role='dialog'] with aria-modal="true".
//   2. aria-labelledby on that dialog references an element that exists.
//   3. accessibleTrader.focusElement was invoked, and the LAST target (some
//      modals move focus twice: heading, then first field) is an element that
//      exists and is focusable — tabindex present or natively focusable tag.

using AngleSharp.Dom;
using Bunit;

namespace AccessibleTrader.Tests.Blazor;

public class ModalAccessibilityContractTests
{
    [Theory]
    [MemberData(nameof(Names))]
    public void Dialog_OnOpen_MovesFocusToExistingFocusableElement(string name)
    {
        using var h = new BlazorTestHarness();
        var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog(name));

        // Vacuity guard: the dialog must actually have opened, otherwise the
        // focus assertions below would pass against a closed modal.
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));

        // focusElement fires from a post-render hook, which can lag the open
        // render on starved CI runners; poll instead of asserting a single
        // frame (this exact assertion flaked two CI runs in a row, always
        // green in isolation).
        cut.WaitForAssertion(() =>
            Assert.True(h.FocusedElementIds.Count > 0,
                $"{name} opened without calling accessibleTrader.focusElement — a screen reader " +
                "user is left wherever they were, with no announcement of the new dialog."));

        var target = h.FocusedElementIds[^1];
        var el = cut.FindAll($"[id='{target}']").SingleOrDefault();
        Assert.True(el != null,
            $"{name} sent focus to '#{target}' but no element with that id exists in its markup.");

        bool nativelyFocusable = el!.TagName.ToLowerInvariant()
            is "input" or "select" or "textarea" or "button" or "a";
        Assert.True(nativelyFocusable || el.HasAttribute("tabindex"),
            $"{name} sent focus to <{el.TagName.ToLowerInvariant()} id='{target}'> which is neither " +
            "natively focusable nor carries a tabindex — focusElement silently no-ops on it.");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Dialog_HasAriaModal_AndLabelledByResolves(string name)
    {
        using var h = new BlazorTestHarness();
        var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog(name));

        var dialog = Assert.Single(cut.FindAll("[role='dialog']"));

        Assert.Equal("true", dialog.GetAttribute("aria-modal"));

        var labelledBy = dialog.GetAttribute("aria-labelledby");
        Assert.False(string.IsNullOrWhiteSpace(labelledBy),
            $"{name}'s dialog has no aria-labelledby — screen readers announce it as an unnamed dialog.");
        foreach (var id in labelledBy!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var label = cut.FindAll($"[id='{id}']").SingleOrDefault();
            Assert.True(label != null,
                $"{name}'s dialog aria-labelledby references '#{id}' which does not exist in its markup.");
            Assert.False(string.IsNullOrWhiteSpace(label!.TextContent),
                $"{name}'s dialog label '#{id}' is empty — the dialog is announced with a blank name.");
        }
    }

    public static TheoryData<string> Names => ModalCatalog.DialogNames;
}

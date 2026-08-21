using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;

namespace AccessibleTrader.BlazorClient.Components
{
    /// <summary>
    /// Base class for all application modals. Enforces the ModalStateChangedEvent
    /// contract so MainPage.xaml.cs can reliably hide/restore the native SkiaSharp
    /// canvas whenever any modal is open.
    ///
    /// USAGE:
    ///   1. Add @inherits ModalBase to the modal's .razor file.
    ///   2. Remove the @inject IEventBus and @inject IJSRuntime directives — they are
    ///      already injected by this base class.
    ///   3. Remove the private _isVisible field — it is declared here.
    ///   4. In ShowAsync(), call await ShowModalAsync("your-h2-element-id") to
    ///      set _isVisible, publish the open event, and focus the heading.
    ///   5. In Close(), call CloseModal() to clear _isVisible and publish the close event.
    ///   6. If your modal has subscriptions, override Dispose() and call base.Dispose()
    ///      to ensure _eventSub is cleaned up automatically.
    ///
    /// WHY NOT AN INTERFACE OR CONVENTION?
    ///   Forgetting EventBus.Publish(new ModalStateChangedEvent(false)) in Close() causes
    ///   the chart canvas to disappear until the app is restarted — a silent, hard-to-diagnose
    ///   bug. A base class makes the correct behaviour the path of least resistance.
    /// </summary>
    public abstract class ModalBase : ComponentBase, IDisposable
    {
        [Inject] protected IEventBus EventBus { get; set; } = null!;
        [Inject] protected IJSRuntime JSRuntime { get; set; } = null!;

        /// <summary>Visibility state shared with the .razor template via @if (_isVisible).</summary>
        protected bool _isVisible;

        /// <summary>
        /// Optional: assign the EventBus subscription token here so Dispose() cleans
        /// it up automatically. Concrete classes with additional subscriptions should
        /// override Dispose() and call base.Dispose().
        /// </summary>
        protected IDisposable? _eventSub;

        /// <summary>
        /// Subscription to <see cref="CloseTopModalEvent"/>. CommandDispatcher publishes
        /// this on Escape with the topmost modal's name; we self-close when the match
        /// fires. Stacked modals close one-at-a-time. Disposed automatically with the rest.
        /// </summary>
        private IDisposable? _closeRequestSub;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            ArmCloseRequestSubscription();
        }

        /// <summary>
        /// Subscribes to CloseTopModalEvent (idempotent). Called from OnInitialized AND
        /// from ShowModalAsync as a safety net: a concrete modal that overrides
        /// OnInitialized without calling base.OnInitialized() would otherwise silently
        /// lose Escape-to-close (the SaveWorkspaceModal bug, 2026-07-15).
        /// </summary>
        private void ArmCloseRequestSubscription()
        {
            _closeRequestSub ??= EventBus.Subscribe<CloseTopModalEvent>(e =>
            {
                if (_isVisible && e.ModalName == ModalName)
                    InvokeAsync(CloseModal);
            });
        }

        /// <summary>
        /// Opens the modal: sets _isVisible, publishes ModalStateChangedEvent(true),
        /// triggers a re-render, then shifts keyboard focus to the modal's heading element.
        /// <para>
        /// Always call this from your concrete ShowAsync() method. Perform any additional
        /// data preparation BEFORE calling ShowModalAsync so the data is ready when Blazor
        /// renders.
        /// </para>
        /// </summary>
        /// <param name="headingElementId">
        /// The HTML id of the modal's h2 heading element (e.g. "settings-title").
        /// Screen readers announce this heading when focus lands on it, giving users
        /// immediate context about which modal has opened.
        /// </param>
        protected async Task ShowModalAsync(string headingElementId)
        {
            ArmCloseRequestSubscription(); // no-op when already armed via OnInitialized
            _isVisible = true;
            EventBus.Publish(new ModalStateChangedEvent(true, ModalName));
            StateHasChanged();
            await Task.Yield();
            try { await JSRuntime.InvokeVoidAsync("accessibleTrader.focusElement", headingElementId); }
            catch { /* non-critical — focus is best-effort; modal is still usable via Tab */ }
            // Tab trap arming lives in MainLayout, which subscribes to
            // ModalStateChangedEvent so direct-publish modals (not inheriting from
            // ModalBase) are covered too.
        }

        /// <summary>
        /// Arrow / Home / End navigation for a <c>role="tablist"</c>, wired the same way in
        /// every modal that has one.
        ///
        /// <para>
        /// Call from the tablist container's <c>@onkeydown</c>. Returns the index to select,
        /// or <c>null</c> when the key was not a tablist key — in which case the caller must
        /// leave its state alone so Tab, Escape and everything else still work.
        /// </para>
        ///
        /// <para>
        /// Focus is moved explicitly because selection follows focus in a tablist: changing
        /// which button is selected is not the same as putting the user on it, and a screen
        /// reader announces the tab it is focused on, not the one the app considers active.
        /// Focus is best-effort, exactly as in <see cref="ShowModalAsync"/> — a JS failure
        /// must not take the keystroke with it.
        /// </para>
        /// </summary>
        /// <param name="key">The pressed key, from <c>KeyboardEventArgs.Key</c>.</param>
        /// <param name="current">Index of the currently selected tab.</param>
        /// <param name="tabElementIds">The tab buttons' HTML ids, in visual order.</param>
        /// <param name="vertical">True for a vertically stacked tablist (Up/Down instead of Left/Right).</param>
        protected async Task<int?> NavigateTablistAsync(
            string? key, int current, IReadOnlyList<string> tabElementIds, bool vertical = false)
        {
            int? target = TablistNavigator.Target(key, current, tabElementIds?.Count ?? 0, vertical);
            if (target == null) return null;

            StateHasChanged();
            await Task.Yield();
            try { await JSRuntime.InvokeVoidAsync("accessibleTrader.focusElement", tabElementIds![target.Value]); }
            catch { /* non-critical — the tab is still selected and reachable by Tab */ }
            return target;
        }

        /// <summary>
        /// Closes the modal: clears _isVisible, publishes ModalStateChangedEvent(false),
        /// and triggers a re-render.
        /// </summary>
        protected void CloseModal()
        {
            _isVisible = false;
            EventBus.Publish(new ModalStateChangedEvent(false, ModalName));
            StateHasChanged();
        }

        /// <summary>
        /// Short human-readable name for this modal, announced via ARIA live when the
        /// modal opens or closes. Override in concrete classes. Defaults to the class
        /// name with a "Modal" suffix stripped ("HelpModal" → "Help").
        /// </summary>
        protected virtual string ModalName
        {
            get
            {
                var name = GetType().Name;
                const string suffix = "Modal";
                return name.EndsWith(suffix, StringComparison.Ordinal)
                    ? name[..^suffix.Length]
                    : name;
            }
        }

        /// <summary>
        /// Disposes the base event subscription. Override in concrete classes that have
        /// additional subscriptions; always call base.Dispose() in the override.
        /// </summary>
        public virtual void Dispose()
        {
            _eventSub?.Dispose();
            _closeRequestSub?.Dispose();
        }
    }
}

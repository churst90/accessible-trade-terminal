using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;

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
            _isVisible = true;
            EventBus.Publish(new ModalStateChangedEvent(true));
            StateHasChanged();
            await Task.Yield();
            try { await JSRuntime.InvokeVoidAsync("accessibleTrader.focusElement", headingElementId); }
            catch { /* non-critical — focus is best-effort; modal is still usable via Tab */ }
        }

        /// <summary>
        /// Closes the modal: clears _isVisible, publishes ModalStateChangedEvent(false),
        /// and triggers a re-render.
        /// </summary>
        protected void CloseModal()
        {
            _isVisible = false;
            EventBus.Publish(new ModalStateChangedEvent(false));
            StateHasChanged();
        }

        /// <summary>
        /// Disposes the base event subscription. Override in concrete classes that have
        /// additional subscriptions; always call base.Dispose() in the override.
        /// </summary>
        public virtual void Dispose()
        {
            _eventSub?.Dispose();
        }
    }
}

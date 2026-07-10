using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// Bridges the JavaScript keyboard handler to the .NET input pipeline.
    ///
    /// WHY THIS EXISTS: Blazor's built-in keyboard events only fire when a focused
    /// element is inside the WebView. For a keyboard-first app, we need global key
    /// capture (F1–F12, arrows, etc.) regardless of focus. A window-level JS listener
    /// calls back into this service via JSInvokable, and we forward to IInputService.
    /// </summary>
    public class GlobalInputService : IDisposable
    {
        private readonly IInputService _inputService;
        private readonly IEventBus _eventBus;
        private readonly IWorkspaceStore _store;
        private DotNetObjectReference<GlobalInputService>? _dotNetRef;

        // Dedupe window. The window-level JS keyboard handler and the Blazor
        // `@onkeydown` binding on <ChartArea> both forward into ProcessKey so we
        // stay resilient when one of the two pipelines is quiet — screen readers
        // in browse mode can dispatch synthetic keydowns at element scope only,
        // and unit tests run without the JS bridge. Whichever side fires first
        // within this window short-circuits the other.
        private static readonly TimeSpan DedupeWindow = TimeSpan.FromMilliseconds(50);
        private readonly object _dedupeLock = new();
        private string? _lastKey;
        private bool _lastShift, _lastCtrl, _lastAlt;
        private DateTime _lastStamp;

        public GlobalInputService(IInputService inputService, IEventBus eventBus, IWorkspaceStore store)
        {
            _inputService = inputService;
            _eventBus = eventBus;
            _store = store;
        }

        public async Task InitializeAsync(IJSRuntime jsRuntime)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            await jsRuntime.InvokeVoidAsync("accessibleTrader.registerKeyboardHandler", _dotNetRef);
            await jsRuntime.InvokeVoidAsync("accessibleTrader.registerMouseHandler", _dotNetRef, "chart-interact-zone");
        }

        [JSInvokable]
        public void OnKeyDown(string key, bool shift, bool ctrl, bool alt)
        {
            TryProcessKey(key, shift, ctrl, alt);
        }

        /// <summary>
        /// Forwards a keydown through the input pipeline, suppressing a duplicate
        /// that arrives from the other pipeline (window JS vs element Blazor)
        /// inside <see cref="DedupeWindow"/>. Returns true when processed.
        /// </summary>
        public bool TryProcessKey(string key, bool shift, bool ctrl, bool alt)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (_dedupeLock)
            {
                var now = DateTime.UtcNow;
                if (_lastKey == key && _lastShift == shift && _lastCtrl == ctrl && _lastAlt == alt
                    && (now - _lastStamp) <= DedupeWindow)
                {
                    return false;
                }
                _lastKey = key;
                _lastShift = shift;
                _lastCtrl = ctrl;
                _lastAlt = alt;
                _lastStamp = now;
            }
            _inputService.ProcessKey(key, shift, ctrl, alt);
            return true;
        }

        /// <summary>Called from JS keyup for navigation keys to stop the sustaining audio voice.</summary>
        [JSInvokable]
        public void OnKeyUp(string key)
        {
            _eventBus.Publish(new NavKeyReleasedEvent());
        }

        [JSInvokable]
        public void OnMouseEvent(double x, double y, string type, double width, double height)
        {
            _inputService.ProcessMouse(x, y, type, width, height);
        }

        /// <summary>
        /// Forwarded from the JS <c>contextmenu</c> handler on the chart-interact-zone.
        /// The handler preventDefault's the browser menu; this bridge emits a distinct
        /// "ContextMenu" mouse type through the same channel so DrawingInteractionManager
        /// can hit-test without a parallel event path.
        /// </summary>
        [JSInvokable]
        public void OnContextMenu(double x, double y, double width, double height)
        {
            _inputService.ProcessMouse(x, y, "ContextMenu", width, height);
        }

        /// <summary>
        /// Forwarded from the JS <c>wheel</c> handler on the chart-interact-zone.
        /// Dispatches <see cref="WheelZoomAction"/> directly so the reducer can re-anchor
        /// the viewport start around the cursor bar. <paramref name="direction"/> is
        /// +1 for wheel-up (zoom in) and -1 for wheel-down (zoom out);
        /// <paramref name="anchorFraction"/> is the cursor X as a fraction of viewport width.
        /// </summary>
        [JSInvokable]
        public void OnWheel(int direction, double anchorFraction)
        {
            _store.Dispatch(new WheelZoomAction(direction, anchorFraction));
        }

        /// <summary>
        /// Forwarded from the JS <c>wheel</c> handler when Shift is held or the wheel
        /// event is a horizontal trackpad swipe: pans the viewport through time instead
        /// of zooming. A motor-friendly alternative to click-drag panning — no button
        /// hold required. Uses <see cref="WorkspacePanEvent"/> so the step honours the
        /// user's configured panning granularity, exactly like the toolbar pan buttons.
        /// <paramref name="direction"/> +1 = forward in time (newer bars), -1 = back.
        /// Panning toward the data edge requests a history backfill like drag-pan does.
        /// </summary>
        [JSInvokable]
        public void OnWheelPan(int direction)
        {
            if (direction < 0 && _store.State.ViewportStartIndex < 50)
                _eventBus.Publish(new RequestHistoryEvent());
            _store.Dispatch(new WorkspacePanEvent(direction));
        }

        /// <summary>
        /// Forwarded from the JS <c>dblclick</c> handler on the chart: jump to the live
        /// edge (latest bar), mirroring the keyboard's jump-to-live command. The standard
        /// navigation feedback fires, so the latest bar is spoken and sonified.
        /// </summary>
        [JSInvokable]
        public void OnDoubleClick()
        {
            _store.Dispatch(new JumpToLatestAction());
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "", true, IsXMove: true, IsJump: true));
        }

        /// <summary>
        /// Normalises a raw <see cref="Microsoft.AspNetCore.Components.Web.KeyboardEventArgs.Key"/>
        /// value to the upper-case token ShortcutManager recognises. Mirrors the
        /// mapping in wwwroot/js/keyboard.js so the element-level Blazor fallback
        /// feeds the pipeline in the exact same vocabulary as the window JS path.
        /// </summary>
        public static string NormalizeKey(string key) => key switch
        {
            "ArrowLeft" => "LEFT",
            "ArrowRight" => "RIGHT",
            "ArrowUp" => "UP",
            "ArrowDown" => "DOWN",
            " " => "SPACE",
            "[" or "{" => "OEM4",
            "]" or "}" => "OEM6",
            "\\" => "OEM5",
            "-" or "_" => "OEMMINUS",
            "=" or "+" => "OEMPLUS",
            "Delete" => "DELETE",
            "Escape" => "ESCAPE",
            _ => key.ToUpperInvariant()
        };

        public void Dispose()
        {
            _dotNetRef?.Dispose();
        }
    }
}

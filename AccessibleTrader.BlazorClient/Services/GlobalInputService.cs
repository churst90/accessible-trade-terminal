using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Models;

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
        private DotNetObjectReference<GlobalInputService>? _dotNetRef;

        public GlobalInputService(IInputService inputService, IEventBus eventBus)
        {
            _inputService = inputService;
            _eventBus = eventBus;
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
            _inputService.ProcessKey(key, shift, ctrl, alt);
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

        public void Dispose()
        {
            _dotNetRef?.Dispose();
        }
    }
}

using System;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Platform input service contract. Each platform (Blazor JS bridge, Android,
    /// iOS, Windows global hook) implements this to feed raw key events into the
    /// routing pipeline.
    /// </summary>
    public interface IInputService
    {
        /// <summary>Fired whenever a key event is captured by the platform input source.</summary>
        event Action<string, bool, bool, bool>? KeyPressed;
        
        /// <summary>Fired whenever a mouse event is captured on the chart.</summary>
        event Action<double, double, string, double, double>? MouseEvent;

        /// <summary>
        /// Programmatically inject a key event. Used by the JS→.NET keyboard bridge
        /// (GlobalInputService) to route browser keydown events into the pipeline.
        /// </summary>
        void ProcessKey(string key, bool shift, bool ctrl, bool alt);

        /// <summary>
        /// Programmatically inject a mouse event.
        /// </summary>
        void ProcessMouse(double x, double y, string type, double width, double height);
    }
}

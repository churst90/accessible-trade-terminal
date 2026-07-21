using System;
using System.Threading.Tasks;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface ITactileDriver
    {
        bool IsConnected { get; }
        string DeviceName { get; }

        /// <summary>False when the platform cannot drive a tactile display at all
        /// (vendor SDK missing — e.g. Linux, browsers). Gates the F4 toggle's
        /// "not available" message. Default true for implementations that exist
        /// only on capable platforms.</summary>
        bool IsAvailable => true;

        /// <summary>Width of the device's graphic area in dots. Zero until ConnectAsync resolves the device.</summary>
        int DisplayWidth { get; }

        /// <summary>Height of the device's graphic area in dots. Zero until ConnectAsync resolves the device.</summary>
        int DisplayHeight { get; }

        /// <summary>Number of separate braille cells on the text strip (0 if the device has no strip).</summary>
        int BrailleCellCount { get; }

        /// <summary>Raised when the user activates a panning key on the device (pan-all / pan-left / pan-right).</summary>
        event EventHandler<TactileKeyEvent>? KeyPressed;

        /// <summary>
        /// Raised when the device's connection state changes — either an established
        /// connection (e.g. a hot-plug detected while running) or a device-initiated
        /// disconnect (the display was unplugged). Carries the device's friendly name
        /// so the coordinator can announce it. NOT raised for an app-initiated
        /// <see cref="DisconnectAsync"/> (e.g. the user turning braille off), so that
        /// path can announce its own "braille disabled" feedback without a duplicate.
        /// </summary>
        event EventHandler<TactileConnectionEvent>? ConnectionChanged;

        Task ConnectAsync();
        Task DisconnectAsync();

        /// <summary>Renders a DisplayWidth×DisplayHeight window of a larger virtual canvas onto the physical device's graphic area.</summary>
        Task RenderViewportAsync(bool[,] virtualCanvas, int startX, int startY);

        /// <summary>Renders plain text to the device's braille text strip. The driver is responsible for any text→braille translation.</summary>
        Task RenderBrailleTextAsync(string text);
    }

    /// <summary>Device-agnostic key event surfaced by ITactileDriver implementations.</summary>
    public enum TactileKey
    {
        PanLeft,
        PanRight,
        PanAll,
        Function1,
        Function2,
        Function3,
        Function4,
        Other
    }

    public sealed class TactileKeyEvent : EventArgs
    {
        public TactileKey Key { get; }
        public string? RawCode { get; }
        public TactileKeyEvent(TactileKey key, string? rawCode = null)
        {
            Key = key;
            RawCode = rawCode;
        }
    }

    /// <summary>Device connect/disconnect transition surfaced by ITactileDriver implementations.</summary>
    public sealed class TactileConnectionEvent : EventArgs
    {
        /// <summary>True if the device just connected; false if it just disconnected.</summary>
        public bool Connected { get; }
        /// <summary>The device's friendly name (e.g. "Dot Pad"), for announcement.</summary>
        public string DeviceName { get; }
        public TactileConnectionEvent(bool connected, string deviceName)
        {
            Connected = connected;
            DeviceName = deviceName;
        }
    }
}

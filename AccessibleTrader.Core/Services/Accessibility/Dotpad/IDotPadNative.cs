namespace AccessibleTrader.Core.Services.Accessibility.Dotpad
{
    /// <summary>
    /// Platform-agnostic surface over the DotPadSDK native library. Each OS provides
    /// its own implementation (Windows = LoadLibrary on DotPadSDK-3.0.0.dll; Linux =
    /// dlopen on a shared-lib build; macOS = dlopen on a framework). DotpadTactileDriver
    /// calls only this interface so the rest of the codebase never has to know which
    /// platform we are on.
    ///
    /// The method set mirrors the full public API in DotSDKAPI.h v3.0.0 so callers
    /// outside the driver (tools, diagnostics, future features) can reach every SDK
    /// function without re-binding it themselves.
    /// </summary>
    public interface IDotPadNative : IDisposable
    {
        /// <summary>True if the native library was loaded and all required exports were resolved.</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Optional language/grade configuration. The official sample passes language
        /// per-call to <see cref="DisplayBrailleText"/> instead of using this, so most
        /// callers can leave this alone.
        /// </summary>
        void SetLanguage(DotPadLanguage language, DotPadBrailleGrade grade);

        // ── Discovery ──────────────────────────────────────────────────────────
        /// <summary>Starts USB scan. Callback fires once per discovered COM port name (e.g. "COM3").</summary>
        void StartUsbScan(Action<string> onPortDiscovered);

        /// <summary>Starts BLE scan. Callback fires once per discovered device name (e.g. "DotPad320-1234").</summary>
        void StartBleScan(Action<string> onDeviceDiscovered);
        void StopBleScan();

        // ── Connection ─────────────────────────────────────────────────────────
        void ConnectSerial(string portName);
        void ConnectBle(string deviceName);

        /// <summary>Disconnects the given device handle. Pass IntPtr.Zero to disconnect all devices.</summary>
        bool Disconnect(IntPtr deviceHandle);

        int GetConnectedDeviceCount();
        bool TryGetConnectedDeviceHandle(int index, out IntPtr deviceHandle);

        // ── Info queries (results delivered via the message callback) ─────────
        /// <summary>Queries the device's graphic-area dot dimensions and braille-cell count.</summary>
        bool TryGetDisplayInfo(IntPtr deviceHandle, out int width, out int height, out int brailleCellCount);

        /// <summary>Requests the device's friendly name. Result arrives via the message callback as <see cref="DotPadDataCode.DeviceName"/>.</summary>
        bool RequestDeviceName(IntPtr deviceHandle);

        /// <summary>Requests the firmware version. Result arrives via the message callback as <see cref="DotPadDataCode.DeviceFwVersion"/>.</summary>
        bool RequestFirmwareVersion(IntPtr deviceHandle);

        /// <summary>Requests the hardware version. Result arrives via the message callback as <see cref="DotPadDataCode.DeviceHwVersion"/>.</summary>
        bool RequestHardwareVersion(IntPtr deviceHandle);

        // ── Rendering ──────────────────────────────────────────────────────────
        /// <summary>Pushes raw packed dot data to the graphic display area (one byte per cell).</summary>
        bool DisplayGraphicData(IntPtr deviceHandle, byte[] data);

        /// <summary>Plays a Dot Inc .dtm graphic file from disk. The SDK streams the file directly.</summary>
        bool DisplayGraphicFile(IntPtr deviceHandle, string dtmFilePath);

        /// <summary>Clears the graphic display area.</summary>
        bool ResetDisplay(IntPtr deviceHandle);

        /// <summary>Renders text to the braille text strip. SDK invokes its bundled LibLouis translator.</summary>
        bool DisplayBrailleText(IntPtr deviceHandle, string text, DotPadLanguage language, DotPadBrailleGrade grade);

        /// <summary>Pushes already-translated braille cell bytes to the text strip (skips the LibLouis pass).</summary>
        bool DisplayBrailleData(IntPtr deviceHandle, byte[] brailleCells);

        /// <summary>Renders a Braille ASCII string (each char maps to a single 6-dot cell pattern).</summary>
        bool DisplayBrailleAscii(IntPtr deviceHandle, string brailleAscii);

        bool ResetBrailleDisplay(IntPtr deviceHandle);

        // ── Callbacks ──────────────────────────────────────────────────────────
        /// <summary>Registers a callback for device lifecycle / metadata messages.</summary>
        void RegisterMessageCallback(Action<IntPtr, DotPadDataCode, string?> callback);

        /// <summary>Registers a callback for hardware key events.</summary>
        void RegisterKeyCallback(Action<IntPtr, DotPadKeyCode, string?> callback);

        /// <summary>Registers a callback that fires after the device finishes physically actuating dots.</summary>
        void RegisterDisplayCompleteCallback(Action<IntPtr> callback);
    }
}

namespace AccessibleTrader.Core.Services.Accessibility.Dotpad
{
    /// <summary>
    /// Stand-in for IDotPadNative on platforms where the Dot Pad SDK isn't bound yet
    /// (Linux WebHost build, macOS, Android, iOS). Always reports unavailable so the
    /// DotpadTactileDriver short-circuits cleanly. Replace at the DI layer with a real
    /// implementation when the platform-specific binding lands.
    /// </summary>
    public sealed class NullDotPadNative : IDotPadNative
    {
        public bool IsAvailable => false;

        public void SetLanguage(DotPadLanguage language, DotPadBrailleGrade grade) { }
        public void StartUsbScan(Action<string> onPortDiscovered) { }
        public void StartBleScan(Action<string> onDeviceDiscovered) { }
        public void StopBleScan() { }
        public void ConnectSerial(string portName) { }
        public void ConnectBle(string deviceName) { }
        public bool Disconnect(IntPtr deviceHandle) => false;
        public int GetConnectedDeviceCount() => 0;
        public bool TryGetConnectedDeviceHandle(int index, out IntPtr deviceHandle) { deviceHandle = IntPtr.Zero; return false; }
        public bool TryGetDisplayInfo(IntPtr deviceHandle, out int width, out int height, out int brailleCellCount)
        { width = 0; height = 0; brailleCellCount = 0; return false; }
        public bool DisplayGraphicData(IntPtr deviceHandle, byte[] data) => false;
        public bool DisplayGraphicFile(IntPtr deviceHandle, string dtmFilePath) => false;
        public bool ResetDisplay(IntPtr deviceHandle) => false;
        public bool DisplayBrailleText(IntPtr deviceHandle, string text, DotPadLanguage language, DotPadBrailleGrade grade) => false;
        public bool DisplayBrailleData(IntPtr deviceHandle, byte[] brailleCells) => false;
        public bool DisplayBrailleAscii(IntPtr deviceHandle, string brailleAscii) => false;
        public bool ResetBrailleDisplay(IntPtr deviceHandle) => false;
        public bool RequestDeviceName(IntPtr deviceHandle) => false;
        public bool RequestFirmwareVersion(IntPtr deviceHandle) => false;
        public bool RequestHardwareVersion(IntPtr deviceHandle) => false;
        public void RegisterMessageCallback(Action<IntPtr, DotPadDataCode, string?> callback) { }
        public void RegisterKeyCallback(Action<IntPtr, DotPadKeyCode, string?> callback) { }
        public void RegisterDisplayCompleteCallback(Action<IntPtr> callback) { }
        public void Dispose() { }
    }
}

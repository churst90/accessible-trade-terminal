using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Accessibility.Dotpad
{
    /// <summary>
    /// Windows implementation of IDotPadNative. Dynamically loads DotPadSDK-3.0.0.dll
    /// from the app's runtime directory (or an explicit path) and resolves the function
    /// pointers documented in DotSDKAPI.h.
    ///
    /// Uses LoadLibrary + GetProcAddress rather than static [DllImport] so that:
    ///   - A missing DLL degrades to IsAvailable=false instead of a TypeInitializationException.
    ///   - We can probe both plain C names and possibly-mangled namespace-qualified names
    ///     without recompiling.
    /// </summary>
    public sealed class WindowsDotPadNative : IDotPadNative
    {
        private const string DefaultDllName = "DotPadSDK-3.0.0.dll";

        private readonly ILogger<WindowsDotPadNative> _logger;
        private readonly IntPtr _dllHandle;

        // ── Native delegate signatures (mirror dot_pad_sdk.h) ─────────────────
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate void ConnectBleFn([MarshalAs(UnmanagedType.LPWStr)] string deviceName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate void ConnectSerialFn([MarshalAs(UnmanagedType.LPWStr)] string portName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool DisconnectFn(IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetConnectedDeviceCountFn();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool GetConnectedDeviceHandleFn(int index, out IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate void ScanFn(IntPtr scanCallback);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void ScanStopFn();

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate void ScanCallback([MarshalAs(UnmanagedType.LPWStr)] string name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool DisplayDataFn([In] byte[] data, int len, IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate bool DisplayFileFn([MarshalAs(UnmanagedType.LPStr)] string displayFile, IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool ResetDisplayFn(IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate bool BrailleDisplayFn(
            [MarshalAs(UnmanagedType.LPWStr)] string text,
            int language,
            int grade,
            int englishGradeIfKorean,
            IntPtr deviceHandle,
            IntPtr translationCallback);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool BrailleDisplayDataFn([In] byte[] brailleData, UIntPtr dataSize, IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate bool BrailleAsciiDisplayFn([MarshalAs(UnmanagedType.LPStr)] string brailleAscii, IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool ResetBrailleDisplayFn(IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool GetDeviceNameFn(IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool GetFwVersionFn(IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool GetHwVersionFn(IntPtr deviceHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void SetLanguageFn(int language, int grade);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool GetDisplayInfoFn(IntPtr deviceHandle, out int width, out int height, out int braille);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RegisterCallbackFn(IntPtr callback);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void KeyCallback(IntPtr deviceHandle, int keyCode, [MarshalAs(UnmanagedType.LPStr)] string? message);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void MessageCallback(IntPtr deviceHandle, int dataCode, [MarshalAs(UnmanagedType.LPStr)] string? message);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DisplayCompleteCallback(IntPtr deviceHandle);

        // ── Resolved function pointers ─────────────────────────────────────────
        private readonly ConnectBleFn? _connectBle;
        private readonly ConnectSerialFn? _connectSerial;
        private readonly DisconnectFn? _disconnect;
        private readonly GetConnectedDeviceCountFn? _getCount;
        private readonly GetConnectedDeviceHandleFn? _getHandle;
        private readonly ScanFn? _bleScan;
        private readonly ScanStopFn? _bleScanStop;
        private readonly ScanFn? _usbScan;
        private readonly DisplayDataFn? _displayData;
        private readonly DisplayFileFn? _displayFile;
        private readonly ResetDisplayFn? _resetDisplay;
        private readonly BrailleDisplayFn? _brailleDisplay;
        private readonly BrailleDisplayDataFn? _brailleDisplayData;
        private readonly BrailleAsciiDisplayFn? _brailleAsciiDisplay;
        private readonly ResetBrailleDisplayFn? _resetBrailleDisplay;
        private readonly SetLanguageFn? _setLanguage;
        private readonly GetDisplayInfoFn? _getDisplayInfo;
        private readonly GetDeviceNameFn? _getDeviceName;
        private readonly GetFwVersionFn? _getFwVersion;
        private readonly GetHwVersionFn? _getHwVersion;
        private readonly RegisterCallbackFn? _registerKeyCb;
        private readonly RegisterCallbackFn? _registerMsgCb;
        private readonly RegisterCallbackFn? _registerDisplayCb;

        // Rooted delegate references — without these, the GC can move/collect the
        // managed callbacks while native code still holds the function pointer.
        // Every callback we hand to the SDK has a field here.
        private ScanCallback? _rootedUsbScanCb;
        private ScanCallback? _rootedBleScanCb;
        private KeyCallback? _rootedKeyCb;
        private MessageCallback? _rootedMsgCb;
        private DisplayCompleteCallback? _rootedDisplayCb;

        public bool IsAvailable { get; }

        public WindowsDotPadNative(ILogger<WindowsDotPadNative> logger, string? dllPath = null)
        {
            _logger = logger;
            string path = dllPath ?? Path.Combine(AppContext.BaseDirectory, DefaultDllName);

            DotPadDiagnostics.Log($"WindowsDotPadNative ctor — BaseDirectory={AppContext.BaseDirectory}");
            DotPadDiagnostics.Log($"Looking for SDK DLL at: {path}");
            DotPadDiagnostics.Log($"  File.Exists(path) = {File.Exists(path)}");

            if (!NativeLibrary.TryLoad(path, out _dllHandle))
            {
                DotPadDiagnostics.Log($"NativeLibrary.TryLoad failed for absolute path. Trying default search.");
                // Fall back to the default search order so users can put the DLL on PATH.
                if (!NativeLibrary.TryLoad(DefaultDllName, out _dllHandle))
                {
                    DotPadDiagnostics.Log($"NativeLibrary.TryLoad failed for default search ({DefaultDllName}). Tactile DISABLED.");
                    _logger.LogWarning("DotPadSDK native library not found (looked for {Path} and on PATH). Tactile display disabled.", path);
                    IsAvailable = false;
                    return;
                }
            }

            DotPadDiagnostics.Log($"SDK DLL loaded. Handle=0x{_dllHandle:X}");

            _connectBle             = Resolve<ConnectBleFn>("DOT_PAD_CONNECT_BLE");
            _connectSerial          = Resolve<ConnectSerialFn>("DOT_PAD_CONNECT_SERIAL");
            _disconnect             = Resolve<DisconnectFn>("DOT_PAD_DISCONNECT");
            _getCount               = Resolve<GetConnectedDeviceCountFn>("DOT_PAD_GET_CONNECTED_DEVICE_COUNT");
            _getHandle              = Resolve<GetConnectedDeviceHandleFn>("DOT_PAD_GET_CONNECTED_DEVICE_HANDLE");
            _bleScan                = Resolve<ScanFn>("DOT_PAD_BLE_SCAN");
            _bleScanStop            = Resolve<ScanStopFn>("DOT_PAD_BLE_SCAN_STOP");
            _usbScan                = Resolve<ScanFn>("DOT_PAD_USB_SCAN");
            _displayData            = Resolve<DisplayDataFn>("DOT_PAD_DISPLAY_DATA");
            _displayFile            = Resolve<DisplayFileFn>("DOT_PAD_DISPLAY_FILE");
            _resetDisplay           = Resolve<ResetDisplayFn>("DOT_PAD_RESET_DISPLAY");
            _brailleDisplay         = Resolve<BrailleDisplayFn>("DOT_PAD_BRAILLE_DISPLAY");
            _brailleDisplayData     = Resolve<BrailleDisplayDataFn>("DOT_PAD_BRAILLE_DISPLAY_DATA");
            _brailleAsciiDisplay    = Resolve<BrailleAsciiDisplayFn>("DOT_PAD_BRAILLE_ASCII_DISPLAY");
            _resetBrailleDisplay    = Resolve<ResetBrailleDisplayFn>("DOT_PAD_RESET_BRAILLE_DISPLAY");
            _setLanguage            = Resolve<SetLanguageFn>("DOT_PAD_SET_LANGUAGE");
            _getDisplayInfo         = Resolve<GetDisplayInfoFn>("DOT_PAD_GET_DISPLAY_INFO");
            _getDeviceName          = Resolve<GetDeviceNameFn>("DOT_PAD_GET_DEVICE_NAME");
            _getFwVersion           = Resolve<GetFwVersionFn>("DOT_PAD_GET_FW_VERSION");
            _getHwVersion           = Resolve<GetHwVersionFn>("DOT_PAD_GET_HW_VERSION");
            _registerKeyCb          = Resolve<RegisterCallbackFn>("DOT_PAD_REGISTER_KEY_CALLBACK");
            _registerMsgCb          = Resolve<RegisterCallbackFn>("DOT_PAD_REGISTER_MESSAGE_CALLBACK");
            _registerDisplayCb      = Resolve<RegisterCallbackFn>("DOT_PAD_REGISTER_DISPLAY_CALLBACK");

            // The minimum surface needed to do anything useful.
            bool minimumExports =
                _connectSerial != null && _disconnect != null && _usbScan != null
                && _displayData != null && _getDisplayInfo != null && _brailleDisplay != null
                && _registerMsgCb != null && _registerDisplayCb != null;

            IsAvailable = minimumExports;
            DotPadDiagnostics.Log(
                $"Export resolution: connectSerial={_connectSerial != null}, disconnect={_disconnect != null}, " +
                $"usbScan={_usbScan != null}, displayData={_displayData != null}, displayFile={_displayFile != null}, " +
                $"getDisplayInfo={_getDisplayInfo != null}, brailleDisplay={_brailleDisplay != null}, " +
                $"brailleDisplayData={_brailleDisplayData != null}, brailleAsciiDisplay={_brailleAsciiDisplay != null}, " +
                $"registerMsgCb={_registerMsgCb != null}, registerDisplayCb={_registerDisplayCb != null}, " +
                $"registerKeyCb={_registerKeyCb != null}, setLanguage={_setLanguage != null}, " +
                $"connectBle={_connectBle != null}, bleScan={_bleScan != null}, getCount={_getCount != null}, " +
                $"getHandle={_getHandle != null}, resetDisplay={_resetDisplay != null}, " +
                $"resetBrailleDisplay={_resetBrailleDisplay != null}, " +
                $"getDeviceName={_getDeviceName != null}, getFwVersion={_getFwVersion != null}, getHwVersion={_getHwVersion != null}");
            if (!IsAvailable)
            {
                DotPadDiagnostics.Log("MINIMUM EXPORTS MISSING — tactile DISABLED. Run `dumpbin /exports` on the DLL and update the Resolve() candidates list.");
                _logger.LogWarning("DotPadSDK loaded from {Path} but one or more required exports were missing. Tactile display disabled.", path);
            }
            else
            {
                DotPadDiagnostics.Log("All required exports resolved. Tactile ENABLED.");
                _logger.LogInformation("DotPadSDK loaded from {Path}.", path);
            }
        }

        private T? Resolve<T>(string exportName) where T : Delegate
        {
            // Try the plain C name first; if that fails, try a few common name-mangling
            // variants. Namespaces inside extern "C" technically aren't standard, so
            // MSVC may export the names with prefixes depending on how the DLL was built.
            string[] candidates =
            {
                exportName,
                $"_{exportName}",
                $"DOT_PAD_SDK_API@{exportName}",
            };
            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryGetExport(_dllHandle, candidate, out var ptr))
                {
                    return Marshal.GetDelegateForFunctionPointer<T>(ptr);
                }
            }
            return null;
        }

        public void SetLanguage(DotPadLanguage language, DotPadBrailleGrade grade)
            => _setLanguage?.Invoke((int)language, (int)grade);

        public void StartUsbScan(Action<string> onPortDiscovered)
        {
            if (_usbScan is null) return;
            _rootedUsbScanCb = name => SafelyInvoke(() => onPortDiscovered(name));
            _usbScan(Marshal.GetFunctionPointerForDelegate(_rootedUsbScanCb));
        }

        public void StartBleScan(Action<string> onDeviceDiscovered)
        {
            if (_bleScan is null) return;
            _rootedBleScanCb = name => SafelyInvoke(() => onDeviceDiscovered(name));
            _bleScan(Marshal.GetFunctionPointerForDelegate(_rootedBleScanCb));
        }

        public void StopBleScan() => _bleScanStop?.Invoke();

        public void ConnectSerial(string portName) => _connectSerial?.Invoke(portName);
        public void ConnectBle(string deviceName) => _connectBle?.Invoke(deviceName);

        public bool Disconnect(IntPtr deviceHandle) => _disconnect?.Invoke(deviceHandle) ?? false;

        public int GetConnectedDeviceCount() => _getCount?.Invoke() ?? 0;

        public bool TryGetConnectedDeviceHandle(int index, out IntPtr deviceHandle)
        {
            deviceHandle = IntPtr.Zero;
            return _getHandle?.Invoke(index, out deviceHandle) ?? false;
        }

        public bool TryGetDisplayInfo(IntPtr deviceHandle, out int width, out int height, out int brailleCellCount)
        {
            width = 0; height = 0; brailleCellCount = 0;
            return _getDisplayInfo?.Invoke(deviceHandle, out width, out height, out brailleCellCount) ?? false;
        }

        public bool DisplayGraphicData(IntPtr deviceHandle, byte[] data)
            => _displayData?.Invoke(data, data.Length, deviceHandle) ?? false;

        public bool DisplayGraphicFile(IntPtr deviceHandle, string dtmFilePath)
            => _displayFile?.Invoke(dtmFilePath, deviceHandle) ?? false;

        public bool ResetDisplay(IntPtr deviceHandle) => _resetDisplay?.Invoke(deviceHandle) ?? false;

        public bool DisplayBrailleText(IntPtr deviceHandle, string text, DotPadLanguage language, DotPadBrailleGrade grade)
            => _brailleDisplay?.Invoke(text, (int)language, (int)grade, (int)DotPadBrailleGrade.Grade2, deviceHandle, IntPtr.Zero) ?? false;

        public bool DisplayBrailleData(IntPtr deviceHandle, byte[] brailleCells)
            => _brailleDisplayData?.Invoke(brailleCells, (UIntPtr)brailleCells.Length, deviceHandle) ?? false;

        public bool DisplayBrailleAscii(IntPtr deviceHandle, string brailleAscii)
            => _brailleAsciiDisplay?.Invoke(brailleAscii, deviceHandle) ?? false;

        public bool ResetBrailleDisplay(IntPtr deviceHandle) => _resetBrailleDisplay?.Invoke(deviceHandle) ?? false;

        public bool RequestDeviceName(IntPtr deviceHandle) => _getDeviceName?.Invoke(deviceHandle) ?? false;
        public bool RequestFirmwareVersion(IntPtr deviceHandle) => _getFwVersion?.Invoke(deviceHandle) ?? false;
        public bool RequestHardwareVersion(IntPtr deviceHandle) => _getHwVersion?.Invoke(deviceHandle) ?? false;

        public void RegisterMessageCallback(Action<IntPtr, DotPadDataCode, string?> callback)
        {
            if (_registerMsgCb is null) return;
            _rootedMsgCb = (handle, code, msg) => SafelyInvoke(() => callback(handle, (DotPadDataCode)code, msg));
            _registerMsgCb(Marshal.GetFunctionPointerForDelegate(_rootedMsgCb));
        }

        public void RegisterKeyCallback(Action<IntPtr, DotPadKeyCode, string?> callback)
        {
            if (_registerKeyCb is null) return;
            _rootedKeyCb = (handle, code, msg) => SafelyInvoke(() => callback(handle, (DotPadKeyCode)code, msg));
            _registerKeyCb(Marshal.GetFunctionPointerForDelegate(_rootedKeyCb));
        }

        public void RegisterDisplayCompleteCallback(Action<IntPtr> callback)
        {
            if (_registerDisplayCb is null) return;
            _rootedDisplayCb = handle => SafelyInvoke(() => callback(handle));
            _registerDisplayCb(Marshal.GetFunctionPointerForDelegate(_rootedDisplayCb));
        }

        // Callbacks must never throw across the native boundary — a managed exception
        // inside a P/Invoke callback is undefined behaviour in some runtimes and will
        // at best produce an unintelligible crash. We log and swallow.
        private void SafelyInvoke(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DotPad native callback threw — swallowed to protect the SDK thread.");
            }
        }

        public void Dispose()
        {
            try { _disconnect?.Invoke(IntPtr.Zero); }
            catch (Exception ex) { _logger.LogWarning(ex, "DotPad disconnect-all failed during dispose."); }

            if (_dllHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(_dllHandle);
            }
        }
    }
}

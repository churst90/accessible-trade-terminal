using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Accessibility.Dotpad
{
    /// <summary>
    /// ITactileDriver backed by the Dot Pad 2nd-gen via the DotPadSDK native library.
    ///
    /// Render flow per frame:
    ///   1. ResetDisplay (atomic "drop all pins") → wait for device quiet
    ///   2. DisplayGraphicData (300-byte buffer streamed line-by-line) → wait for quiet
    ///
    /// Why both, even though Dot Inc's canonical Windows DemoApp does neither?
    /// The DemoApp is button-driven — buttons fire one frame manually with several
    /// seconds of human reaction time between presses, so its per-frame timing is
    /// fundamentally different from a charting app sending frames continuously.
    /// DOT_PAD_DISPLAY_DATA streams 300 bytes line-by-line with per-line acks; if
    /// any line ack is missed, those pins are stale. DOT_PAD_RESET_DISPLAY is a
    /// single atomic command — far less prone to per-line transmission errors —
    /// so resetting first gets the device into a known-all-down state before the
    /// (unreliable) per-line stream begins. WaitForQuiet is the real synchronization
    /// gate: the SDK fires display-complete-line callbacks repeatedly during a
    /// frame, and only stops once the device is idle. Waiting for QuietPeriod of
    /// callback silence is the right "device is done" signal.
    ///
    /// Earlier versions also sent each frame TWICE in the name of reliability —
    /// that turned out to be wrong, because the SDK detects unchanged buffers
    /// (DOT_ERROR_DISPLAY_DATA_UNCHAGNED in DotSDKError.h) and the second send
    /// becomes a no-op or collides with the in-flight first send. Single send is
    /// correct; reset-before + wait-for-quiet are correct.
    /// </summary>
    public sealed class DotpadTactileDriver : ITactileDriver, IDisposable
    {
        private static readonly TimeSpan ScanCollectWindow = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan PerPortConnectTimeout = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan FrameSemaphoreTimeout = TimeSpan.FromSeconds(8);

        // The SDK fires OnDisplayComplete repeatedly during a single frame (once per
        // line), not once at frame-done. We treat "no callback for QuietPeriod" as
        // the real signal the device has finished and is ready for the next command.
        // MaxFrameWait caps any single wait so a missed callback can't hang the
        // render pipeline indefinitely.
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan MaxFrameWait = TimeSpan.FromSeconds(6);

        // Dot Pad graphic cell — 8-dot cell, 2 dots wide × 4 dots tall.
        // Empirically verified via the calibrator tool: sending 0x3F to a single
        // cell raises all 4 left-column pins plus the top 2 right-column pins,
        // proving the cell is 2×4 (not the 6-dot 2×3 used elsewhere in braille).
        internal const int DotsPerCellWidth = 2;
        internal const int DotsPerCellHeight = 4;

        private readonly IDotPadNative _native;
        private readonly ILogger<DotpadTactileDriver> _logger;
        private readonly SemaphoreSlim _renderSerializer = new(1, 1);
        private readonly object _connectionLock = new();

        private IntPtr _deviceHandle = IntPtr.Zero;
        private TaskCompletionSource<bool>? _connectTcs;
        private long _lastDisplayCompleteTicks; // UTC ticks; updated by OnDisplayComplete
        private string? _lastBrailleText;

        public bool IsAvailable => _native.IsAvailable;

        public bool IsConnected { get; private set; }
        public string DeviceName { get; private set; } = "Dot Pad";

        /// <summary>Firmware version reported by the device. "(unknown)" until learned via the message callback.</summary>
        public string FirmwareVersion { get; private set; } = "(unknown)";

        /// <summary>Hardware version reported by the device. "(unknown)" until learned via the message callback.</summary>
        public string HardwareVersion { get; private set; } = "(unknown)";

        /// <summary>Number of braille cells across the graphic area (e.g. 30 on Dot Pad 2nd gen).</summary>
        public int DisplayCellWidth { get; private set; }
        /// <summary>Number of braille cells down the graphic area (e.g. 10 on Dot Pad 2nd gen).</summary>
        public int DisplayCellHeight { get; private set; }

        /// <summary>
        /// Width in INDIVIDUAL DOTS (not cells). Each cell is 2 dots wide.
        /// Callers render into a bool[DisplayWidth, DisplayHeight] canvas at dot resolution
        /// for thin-pin-style charts; the driver packs the dots into per-cell bit patterns.
        /// </summary>
        public int DisplayWidth => DisplayCellWidth * DotsPerCellWidth;
        /// <summary>Height in INDIVIDUAL DOTS (not cells). Each cell is 4 dots tall.</summary>
        public int DisplayHeight => DisplayCellHeight * DotsPerCellHeight;

        public int BrailleCellCount { get; private set; }

        public event EventHandler<TactileKeyEvent>? KeyPressed;
        public event EventHandler<TactileConnectionEvent>? ConnectionChanged;

        public DotpadTactileDriver(IDotPadNative native, ILogger<DotpadTactileDriver> logger)
        {
            _native = native;
            _logger = logger;

            DotPadDiagnostics.Log($"DotpadTactileDriver ctor — native.IsAvailable={_native.IsAvailable}");
            if (!_native.IsAvailable)
            {
                _logger.LogInformation("Dot Pad native library unavailable; driver will report not-connected.");
                return;
            }

            // Startup callback registration: key + message only. Display-complete is
            // deferred until post-connect (an earlier attempt to register it up-front
            // appeared to prevent CONNECTED from firing in response to ConnectSerial).
            _native.RegisterKeyCallback(OnNativeKey);
            _native.RegisterMessageCallback(OnNativeMessage);
            DotPadDiagnostics.Log("Startup callbacks registered (key, message). Display-complete deferred until post-connect.");
        }

        public async Task ConnectAsync()
        {
            DotPadDiagnostics.Log($"ConnectAsync called. native.IsAvailable={_native.IsAvailable}, IsConnected={IsConnected}");
            if (!_native.IsAvailable) return;
            if (IsConnected) return;

            // ── Phase 1: identify candidate Dot Pad ports.
            //
            // We try the SDK's own USB_SCAN first (it filters down to ports the SDK
            // believes are Dot Pads — in practice the two interfaces of the composite
            // USB device). If for any reason the SDK scan returns nothing, fall back
            // to the full Windows COM port list via SerialPort.GetPortNames(), which
            // is slower-to-probe but at least won't miss the device.
            var discovered = new List<string>();
            DotPadDiagnostics.Log("Calling StartUsbScan to identify candidate ports…");
            _native.StartUsbScan(port =>
            {
                DotPadDiagnostics.Log($"  USB scan callback fired with port='{port}'");
                if (string.IsNullOrEmpty(port)) return;
                lock (discovered)
                {
                    if (!discovered.Contains(port)) discovered.Add(port);
                }
            });
            await Task.Delay(ScanCollectWindow).ConfigureAwait(false);

            List<string> ports;
            lock (discovered) { ports = new List<string>(discovered); }

            if (ports.Count == 0)
            {
                DotPadDiagnostics.Log("SDK USB_SCAN returned nothing. Falling back to full Windows COM enumeration.");
                try
                {
                    ports = SerialPort.GetPortNames()
                        .Where(p => !string.IsNullOrEmpty(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception ex)
                {
                    DotPadDiagnostics.Log($"SerialPort.GetPortNames() threw: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (ports.Count == 0)
            {
                DotPadDiagnostics.Log("No candidate ports anywhere. Is the device enumerated in Device Manager → Ports (COM & LPT)?");
                _logger.LogWarning("No COM ports found.");
                return;
            }

            DotPadDiagnostics.Log($"Trying {ports.Count} candidate port(s): {string.Join(", ", ports)} (timeout {PerPortConnectTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s each)");

            // ── Phase 2: try each port until one acknowledges with CONNECTED.
            foreach (var port in ports)
            {
                DotPadDiagnostics.Log($"Attempting ConnectSerial on '{port}' (timeout {PerPortConnectTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s)…");
                lock (_connectionLock) { _connectTcs = new TaskCompletionSource<bool>(); }
                _native.ConnectSerial(port);

                bool connected;
                try
                {
                    await _connectTcs.Task.WaitAsync(PerPortConnectTimeout).ConfigureAwait(false);
                    connected = true;
                }
                catch (TimeoutException)
                {
                    DotPadDiagnostics.Log($"  → {port} did not acknowledge CONNECTED. Disconnecting and trying next port.");
                    try { _native.Disconnect(IntPtr.Zero); } catch { /* best-effort cleanup */ }
                    connected = false;
                }

                if (!connected) continue;

                int deviceCount = _native.GetConnectedDeviceCount();
                DotPadDiagnostics.Log($"  → {port} CONNECTED. GetConnectedDeviceCount()={deviceCount}");
                for (int i = 0; i < deviceCount; i++)
                {
                    if (_native.TryGetConnectedDeviceHandle(i, out var handle))
                    {
                        _deviceHandle = handle;
                        DotPadDiagnostics.Log($"  → Got device handle at index {i}: 0x{handle:X}");
                        break;
                    }
                }
                if (_deviceHandle != IntPtr.Zero) break;

                DotPadDiagnostics.Log($"  → {port} acknowledged but no handle returned. Disconnecting and trying next.");
                try { _native.Disconnect(IntPtr.Zero); } catch { }
            }

            if (_deviceHandle == IntPtr.Zero)
            {
                DotPadDiagnostics.Log($"Tried all {ports.Count} discovered port(s); none yielded a usable device handle.");
                _logger.LogWarning("Dot Pad found {Count} COM port(s) but none completed handshake.", ports.Count);
                return;
            }

            if (_native.TryGetDisplayInfo(_deviceHandle, out int w, out int h, out int cells))
            {
                DisplayCellWidth = w;
                DisplayCellHeight = h;
                BrailleCellCount = cells;
                DotPadDiagnostics.Log($"Display info — {w}x{h} cells ({DisplayWidth}x{DisplayHeight} dots), {cells} braille text cells.");
                _logger.LogInformation("Dot Pad display info — {W}×{H} cells ({Dw}×{Dh} dots), {Cells} braille text cells.",
                    w, h, DisplayWidth, DisplayHeight, cells);
            }
            else
            {
                DotPadDiagnostics.Log("GET_DISPLAY_INFO returned false. Using fallback 30x10 cells + 20 cells.");
                _logger.LogWarning("DOT_PAD_GET_DISPLAY_INFO returned false; using fallback dimensions.");
                DisplayCellWidth = 30; DisplayCellHeight = 10; BrailleCellCount = 20;
            }

            // NOW that the device is real and acknowledged, register the display-complete
            // callback that drives WaitForQuietAsync. It is intentionally NOT registered
            // before connect — earlier attempts at that appeared to prevent CONNECTED
            // from firing in response to ConnectSerial.
            _native.RegisterDisplayCompleteCallback(OnDisplayComplete);
            DotPadDiagnostics.Log("Post-connect setup done: display-complete callback registered.");

            // Clear any pins left raised by a previous session. Without this, stale pins
            // persist across USB reconnects until the first frame happens to overwrite
            // them — visible to the user as random dots that don't match the rendered chart.
            try
            {
                _native.ResetDisplay(_deviceHandle);
                _native.ResetBrailleDisplay(_deviceHandle);
                DotPadDiagnostics.Log("Display + braille strip reset to clear stale pins.");
            }
            catch (Exception ex)
            {
                DotPadDiagnostics.Log($"Display reset threw (non-fatal): {ex.GetType().Name}: {ex.Message}");
            }

            // Ask the device for its friendly name so the connect announcement can
            // use it. The reply arrives asynchronously via the message callback
            // (DotPadDataCode.DeviceName); if it hasn't landed by the time we announce,
            // DeviceName is still the sensible default "Dot Pad".
            try { _native.RequestDeviceName(_deviceHandle); } catch { /* best-effort */ }

            IsConnected = true;
            DotPadDiagnostics.Log("Driver fully connected. Ready for frames.");
            RaiseConnectionChanged(connected: true);
        }

        private void RaiseConnectionChanged(bool connected)
        {
            try { ConnectionChanged?.Invoke(this, new TactileConnectionEvent(connected, DeviceName)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Subscriber to ITactileDriver.ConnectionChanged threw."); }
        }

        public Task DisconnectAsync()
        {
            if (!IsConnected) return Task.CompletedTask;
            try
            {
                _native.Disconnect(_deviceHandle);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dot Pad disconnect threw.");
            }
            finally
            {
                IsConnected = false;
                _deviceHandle = IntPtr.Zero;
            }
            return Task.CompletedTask;
        }

        public async Task RenderViewportAsync(bool[,] virtualCanvas, int startX, int startY)
        {
            if (!IsConnected || DisplayWidth == 0 || DisplayHeight == 0) return;
            if (virtualCanvas is null) return;

            var packed = PackViewport(virtualCanvas, startX, startY, DisplayWidth, DisplayHeight);

            // Back-pressure: only one render in flight at a time. Held across the
            // entire reset→wait→display→wait sequence so two callers can't ever
            // collide on the wire.
            bool acquired = await _renderSerializer.WaitAsync(FrameSemaphoreTimeout).ConfigureAwait(false);
            if (!acquired)
            {
                _logger.LogWarning("Dot Pad render semaphore timed out; dropping frame.");
                return;
            }

            try
            {
                // Step 1 — atomic clear. ResetDisplay is a single command (not 300 streamed
                // bytes) and is the only way to reliably drop all pins. Without it, any
                // pins from the previous frame that the new frame doesn't explicitly set
                // can linger if their per-line update goes missing in transmission.
                try { _native.ResetDisplay(_deviceHandle); }
                catch (Exception ex) { DotPadDiagnostics.Log($"ResetDisplay before frame threw (non-fatal): {ex.GetType().Name}: {ex.Message}"); }
                await WaitForQuietAsync("reset").ConfigureAwait(false);

                // Step 2 — push the new buffer. Single send: the SDK detects unchanged
                // buffers (DOT_ERROR_DISPLAY_DATA_UNCHAGNED) so a "retry by re-sending"
                // is a no-op anyway, and a fast re-send can collide with the in-flight
                // line-by-line transmission of the first send.
                DotPadDiagnostics.Log($"DisplayGraphicData — {packed.Length} bytes, viewport {DisplayWidth}x{DisplayHeight}, startX={startX}, startY={startY}");
                bool sent;
                try { sent = _native.DisplayGraphicData(_deviceHandle, packed); }
                catch (Exception ex)
                {
                    DotPadDiagnostics.Log($"DisplayGraphicData threw: {ex.GetType().Name}: {ex.Message}");
                    return;
                }
                if (!sent)
                {
                    // false here typically means DOT_ERROR_DISPLAY_IN_PROGRESS or
                    // DOT_ERROR_DISPLAY_DATA_UNCHAGNED — neither is fatal, and the
                    // throttled coordinator will issue another frame shortly.
                    DotPadDiagnostics.Log("DisplayGraphicData returned false (likely DISPLAY_IN_PROGRESS or DATA_UNCHANGED).");
                    return;
                }
                await WaitForQuietAsync("display").ConfigureAwait(false);
            }
            finally
            {
                _renderSerializer.Release();
            }
        }

        /// <summary>
        /// Waits until <see cref="QuietPeriod"/> has elapsed since the last
        /// <see cref="OnDisplayComplete"/> callback, or until <see cref="MaxFrameWait"/>
        /// elapses from the start of the wait — whichever comes first.
        ///
        /// The SDK fires display-complete repeatedly during a frame (once per display
        /// line), so the real "device is idle" signal is callback silence — not the
        /// first callback. We seed the timestamp to "now" at the start of the wait so
        /// the device is given at least one QuietPeriod even if no callback fires.
        /// </summary>
        private async Task WaitForQuietAsync(string label)
        {
            var startTicks = DateTime.UtcNow.Ticks;
            var maxWaitTicks = startTicks + MaxFrameWait.Ticks;
            // Treat the start of the wait as if a callback just fired, so we don't
            // declare "quiet" before the device has had a chance to respond.
            Interlocked.Exchange(ref _lastDisplayCompleteTicks, startTicks);

            while (true)
            {
                long lastCb = Interlocked.Read(ref _lastDisplayCompleteTicks);
                long nowTicks = DateTime.UtcNow.Ticks;
                long sinceLastCbTicks = nowTicks - lastCb;
                if (sinceLastCbTicks >= QuietPeriod.Ticks)
                {
                    DotPadDiagnostics.Log($"WaitForQuietAsync({label}): quiet after {(nowTicks - startTicks) / TimeSpan.TicksPerMillisecond}ms");
                    return;
                }
                if (nowTicks >= maxWaitTicks)
                {
                    DotPadDiagnostics.Log($"WaitForQuietAsync({label}): hit MaxFrameWait ({MaxFrameWait.TotalMilliseconds}ms); proceeding.");
                    return;
                }
                long remainingTicks = QuietPeriod.Ticks - sinceLastCbTicks;
                int delayMs = Math.Max(20, (int)(remainingTicks / TimeSpan.TicksPerMillisecond));
                await Task.Delay(delayMs).ConfigureAwait(false);
            }
        }

        public Task RenderBrailleTextAsync(string text)
        {
            if (!IsConnected || string.IsNullOrEmpty(text)) return Task.CompletedTask;
            if (BrailleCellCount == 0) return Task.CompletedTask;
            if (text == _lastBrailleText) return Task.CompletedTask; // dedup repeat updates

            _lastBrailleText = text;

            // Reset before each strip update — DISPLAY_BRAILLE_TEXT only writes as many
            // cells as the new text uses; when the new string is shorter, cells past
            // the new string's length stay raised from the old text.
            try { _native.ResetBrailleDisplay(_deviceHandle); }
            catch (Exception ex) { DotPadDiagnostics.Log($"ResetBrailleDisplay before update threw (non-fatal): {ex.GetType().Name}: {ex.Message}"); }

            bool sent;
            try { sent = _native.DisplayBrailleText(_deviceHandle, text, DotPadLanguage.English, DotPadBrailleGrade.Grade2); }
            catch (Exception ex)
            {
                DotPadDiagnostics.Log($"DisplayBrailleText threw: {ex.GetType().Name}: {ex.Message}");
                return Task.CompletedTask;
            }
            if (!sent)
            {
                DotPadDiagnostics.Log($"DisplayBrailleText returned false for text='{text}'.");
            }
            return Task.CompletedTask;
        }

        // ── Native callbacks (already wrapped in SafelyInvoke by WindowsDotPadNative) ──

        private void OnNativeMessage(IntPtr handle, DotPadDataCode code, string? message)
        {
            DotPadDiagnostics.Log($"OnNativeMessage: handle=0x{handle:X}, code={code}, message='{message}'");
            switch (code)
            {
                case DotPadDataCode.Connected:
                    lock (_connectionLock) { _connectTcs?.TrySetResult(true); }
                    break;

                case DotPadDataCode.Disconnected:
                    // Device-initiated disconnect (the display was unplugged). Distinct
                    // from DisconnectAsync (app-initiated), which deliberately does NOT
                    // raise this event. The coordinator announces "{name} disconnected"
                    // and its hot-plug watch will reconnect if the device returns.
                    IsConnected = false;
                    _deviceHandle = IntPtr.Zero;
                    _logger.LogInformation("Dot Pad disconnected.");
                    RaiseConnectionChanged(connected: false);
                    break;

                case DotPadDataCode.DeviceName when !string.IsNullOrEmpty(message):
                    DeviceName = message!;
                    _logger.LogInformation("Dot Pad device name: {Name}", DeviceName);
                    break;

                case DotPadDataCode.DeviceFwVersion when !string.IsNullOrEmpty(message):
                    FirmwareVersion = message!;
                    _logger.LogInformation("Dot Pad firmware version: {Version}", FirmwareVersion);
                    break;

                case DotPadDataCode.DeviceHwVersion when !string.IsNullOrEmpty(message):
                    HardwareVersion = message!;
                    _logger.LogInformation("Dot Pad hardware version: {Version}", HardwareVersion);
                    break;

                case DotPadDataCode.BoardInfo when !string.IsNullOrEmpty(message):
                    _logger.LogInformation("Dot Pad board info: {Info}", message);
                    break;

                case DotPadDataCode.BleMacAddress when !string.IsNullOrEmpty(message):
                    _logger.LogInformation("Dot Pad BLE MAC: {Mac}", message);
                    break;

                case DotPadDataCode.CommandError:
                    // The SDK uses CommandError to report DOT_ERROR_* values from
                    // DotSDKError.h: COM port failures, DISPLAY_IN_PROGRESS, invalid
                    // memory access, etc. Log loudly — this is our only window into
                    // why DISPLAY_DATA might be silently dropping bytes.
                    _logger.LogWarning("Dot Pad reported command error: {Msg}", message ?? "(no detail)");
                    DotPadDiagnostics.Log($"!! COMMAND_ERROR from device: {message ?? "(no detail)"}");
                    break;

                case DotPadDataCode.ResponseDisplayLineAck:
                case DotPadDataCode.ResponseDisplayLineNonAck:
                case DotPadDataCode.ResponseDisplayLineComplete:
                    // Per-line transmission status. Not actionable from here, but
                    // logged via the catch-all above so we can see how many lines
                    // acked vs didn't on a given frame.
                    break;
            }
        }

        private void OnNativeKey(IntPtr handle, DotPadKeyCode code, string? message)
        {
            TactileKey key = code switch
            {
                DotPadKeyCode.PanningLeft  => TactileKey.PanLeft,
                DotPadKeyCode.PanningRight => TactileKey.PanRight,
                DotPadKeyCode.PanningAll   => TactileKey.PanAll,
                DotPadKeyCode.Function1    => TactileKey.Function1,
                DotPadKeyCode.Function2    => TactileKey.Function2,
                DotPadKeyCode.Function3    => TactileKey.Function3,
                DotPadKeyCode.Function4    => TactileKey.Function4,
                _ => TactileKey.Other,
            };
            try { KeyPressed?.Invoke(this, new TactileKeyEvent(key, code.ToString())); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Subscriber to ITactileDriver.KeyPressed threw.");
            }
        }

        private void OnDisplayComplete(IntPtr handle)
        {
            // Fires once per display LINE during a frame, not once per frame.
            // WaitForQuietAsync watches this timestamp — when QuietPeriod elapses
            // with no new callback, the device has finished processing the last op.
            Interlocked.Exchange(ref _lastDisplayCompleteTicks, DateTime.UtcNow.Ticks);
        }

        // ── Packer ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Packs a DOT-LEVEL canvas into the Dot Pad's cell-byte buffer for DOT_PAD_DISPLAY_DATA.
        ///
        /// Input: bool[,] sized to (cells_wide × DotsPerCellWidth) × (cells_tall × DotsPerCellHeight)
        ///        — i.e. 60×40 dots on a 30×10-cell Dot Pad 2nd gen.
        /// Output: width*height bytes (= cells_wide × cells_tall = 300 for the 2nd gen).
        ///
        /// Each cell byte uses an 8-dot columnar bit layout (verified empirically):
        ///   bit 0 = top-left      bit 4 = top-right
        ///   bit 1 = upper-mid-L   bit 5 = upper-mid-R
        ///   bit 2 = lower-mid-L   bit 6 = lower-mid-R
        ///   bit 3 = bottom-left   bit 7 = bottom-right
        ///
        /// Bytes are row-major top-to-bottom, left-to-right across cells.
        ///
        /// The width/height parameters are DOT dimensions (not cell dimensions). They
        /// must each be divisible by DotsPerCellWidth/DotsPerCellHeight respectively.
        /// </summary>
        internal static byte[] PackViewport(bool[,] dotCanvas, int startX, int startY, int width, int height)
        {
            int cellsX = width / DotsPerCellWidth;
            int cellsY = height / DotsPerCellHeight;
            var output = new byte[cellsX * cellsY];

            int srcW = dotCanvas.GetLength(0);
            int srcH = dotCanvas.GetLength(1);

            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int sx = startX + dx;
                    int sy = startY + dy;
                    if (sx < 0 || sx >= srcW || sy < 0 || sy >= srcH) continue;
                    if (!dotCanvas[sx, sy]) continue;

                    int cellX = dx / DotsPerCellWidth;
                    int cellY = dy / DotsPerCellHeight;
                    int subX = dx % DotsPerCellWidth;            // 0 = left column, 1 = right column
                    int subY = dy % DotsPerCellHeight;           // 0 = top, 1..2 = middle, 3 = bottom
                    int bit = subY + (subX * DotsPerCellHeight); // left column = bits 0..3, right column = bits 4..7
                    int byteIdx = cellY * cellsX + cellX;
                    output[byteIdx] |= (byte)(1 << bit);
                }
            }
            return output;
        }

        public void Dispose()
        {
            try { DisconnectAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Dot Pad dispose-disconnect failed."); }
            _renderSerializer.Dispose();
        }
    }
}

using System.IO.Ports;
using AccessibleTrader.Core.Services.Accessibility.Dotpad;
using Microsoft.Extensions.Logging.Abstractions;

// =============================================================================
// Dot Pad Calibrator
// =============================================================================
// Empirical probe tool. Uses WindowsDotPadNative directly (not DotpadTactileDriver)
// so we can push raw cell-byte buffers and observe which physical pin actually
// rises.
//
// Render flow per frame (mirrors DotpadTactileDriver):
//   1. ResetDisplay  (atomic clear)
//   2. WaitForQuiet  (wait until device idle — display-complete callbacks stop)
//   3. DisplayData   (300-byte buffer; SDK streams it line-by-line with per-line acks)
//   4. WaitForQuiet  (wait until device idle again before allowing next frame)
//
// Why both reset and display, when Dot Inc's canonical Windows DemoApp does
// neither? The DemoApp is button-driven with several seconds of human reaction
// time between presses, so its per-frame timing is fundamentally different.
// DOT_PAD_DISPLAY_DATA streams 300 bytes with per-line acks; if any line ack
// is missed, those pins are stale. DOT_PAD_RESET_DISPLAY is a single atomic
// command — far less prone to per-line transmission errors — so resetting
// first gets the device into a known-all-down state before the (unreliable)
// per-line stream begins.
//
// Cell layout (8-dot, 2 wide × 4 tall, verified empirically):
//   bit 0 = dot 1 (top-left)        bit 4 = dot 4 (top-right)
//   bit 1 = dot 2                   bit 5 = dot 5
//   bit 2 = dot 3                   bit 6 = dot 6
//   bit 3 = dot 7 (bottom-left)     bit 7 = dot 8 (bottom-right)
// Bytes are row-major: byte 0 = top-left cell, byte 30 = leftmost cell of
// the second cell-row on a 30×10-cell device.
// =============================================================================

var native = new WindowsDotPadNative(NullLogger<WindowsDotPadNative>.Instance);
if (!native.IsAvailable)
{
    Console.WriteLine("Dot Pad SDK native library is not available.");
    Console.WriteLine("Check that DotPadSDK-3.0.0.dll and its dependencies are next to the executable.");
    return;
}

var connectedTcs = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
long lastDisplayCompleteTicks = 0;
bool keyListenMode = false;

// The SDK fires display-complete once PER LINE while drawing a frame, not once
// per frame. To know when the device is actually idle, we watch for a quiet
// period — QuietPeriod elapsed since the last display-complete callback.
TimeSpan quietPeriod = TimeSpan.FromMilliseconds(500);
TimeSpan maxFrameWait = TimeSpan.FromSeconds(6);

// ── Wire callbacks BEFORE attempting to connect ────────────────────────────────
native.RegisterMessageCallback((handle, code, msg) =>
{
    Console.WriteLine($"MSG: {code} handle=0x{handle:X} msg={msg ?? "(null)"}");
    if (code == DotPadDataCode.Connected)
    {
        IntPtr resolved = handle;
        try
        {
            int count = native.GetConnectedDeviceCount();
            if (count > 0 && native.TryGetConnectedDeviceHandle(0, out var h0))
                resolved = h0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (could not resolve device handle: {ex.GetType().Name}: {ex.Message})");
        }
        connectedTcs.TrySetResult(resolved);
    }
    else if (code == DotPadDataCode.CommandError)
    {
        Console.WriteLine($"  !! COMMAND_ERROR from device: {msg ?? "(no detail)"}");
    }
});

native.RegisterKeyCallback((handle, code, msg) =>
{
    Console.WriteLine($"KEY: {code} handle=0x{handle:X} msg={msg ?? "(null)"}");
    if (keyListenMode)
    {
        Console.WriteLine($"  (in listen mode — TactileKey mapping TBD; this is what the SDK reported)");
    }
});

native.RegisterDisplayCompleteCallback(handle =>
{
    // Fires per LINE during a frame. WaitForQuietAsync uses this as a heartbeat
    // and infers "frame done" by watching for a QuietPeriod of callback silence.
    Interlocked.Exchange(ref lastDisplayCompleteTicks, DateTime.UtcNow.Ticks);
});

// ── Port discovery ─────────────────────────────────────────────────────────────
var ports = new List<string>();
Console.WriteLine("Scanning for USB ports (2 seconds)...");
try
{
    native.StartUsbScan(port =>
    {
        Console.WriteLine($"  USB_SCAN reported: {port}");
        if (!ports.Contains(port)) ports.Add(port);
    });
    await Task.Delay(TimeSpan.FromSeconds(2));
}
catch (Exception ex)
{
    Console.WriteLine($"  StartUsbScan failed ({ex.GetType().Name}: {ex.Message})");
}

if (ports.Count == 0)
{
    Console.WriteLine("USB scan returned no ports; falling back to SerialPort.GetPortNames().");
    try
    {
        foreach (var p in SerialPort.GetPortNames())
        {
            Console.WriteLine($"  System port: {p}");
            if (!ports.Contains(p)) ports.Add(p);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  SerialPort.GetPortNames() failed ({ex.GetType().Name}: {ex.Message})");
    }
}

if (ports.Count == 0)
{
    Console.WriteLine("No serial ports found. Plug in the Dot Pad and try again.");
    return;
}

// ── Try each port until one says Connected ────────────────────────────────────
IntPtr deviceHandle = IntPtr.Zero;
bool connected = false;
foreach (var port in ports)
{
    Console.WriteLine($"Trying {port}...");
    connectedTcs = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
    try
    {
        native.ConnectSerial(port);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ConnectSerial threw: {ex.GetType().Name}: {ex.Message}");
        continue;
    }

    var winner = await Task.WhenAny(connectedTcs.Task, Task.Delay(TimeSpan.FromSeconds(4)));
    if (winner == connectedTcs.Task)
    {
        deviceHandle = connectedTcs.Task.Result;
        connected = true;
        Console.WriteLine($"Connected on {port} — handle=0x{deviceHandle:X}");
        break;
    }

    Console.WriteLine($"  Timed out on {port}. Disconnecting and trying next.");
    try { native.Disconnect(IntPtr.Zero); }
    catch (Exception ex) { Console.WriteLine($"  Disconnect-all threw: {ex.GetType().Name}: {ex.Message}"); }
}

if (!connected)
{
    Console.WriteLine("Failed to connect to any port. Exiting.");
    return;
}

// ── Display info ───────────────────────────────────────────────────────────────
int cellsX = 30, cellsY = 10, brailleCells = 20;
try
{
    if (native.TryGetDisplayInfo(deviceHandle, out int w, out int h, out int braille))
    {
        cellsX = w;
        cellsY = h;
        brailleCells = braille;
    }
    else
    {
        Console.WriteLine("TryGetDisplayInfo returned false — using fallback 30x10 cells, 20 strip cells.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"TryGetDisplayInfo threw ({ex.GetType().Name}: {ex.Message}) — using fallback 30x10/20.");
}

int dotsX = cellsX * 2;          // 2 dots per cell wide
int dotsY = cellsY * 4;          // 4 dots per cell tall (8-dot cell — was * 3 under the old 6-dot assumption)
int byteCount = cellsX * cellsY;

Console.WriteLine($"Display: {cellsX}x{cellsY} cells = {dotsX}x{dotsY} dots, {brailleCells} text cells");

// ── Helpers ────────────────────────────────────────────────────────────────────
// Per-frame flow: ResetDisplay → WaitForQuiet → DisplayData → WaitForQuiet.
// This mirrors DotpadTactileDriver.RenderViewportAsync. The reset is the
// atomic-clear that pre-empts random-pin-failure caused by missed per-line acks
// during the streamed DisplayData. WaitForQuiet is the real synchronization
// gate, watching for QuietPeriod of callback silence from the SDK.

async Task WaitForQuietAsync(string label)
{
    var startTicks = DateTime.UtcNow.Ticks;
    var maxWaitTicks = startTicks + maxFrameWait.Ticks;
    Interlocked.Exchange(ref lastDisplayCompleteTicks, startTicks);
    while (true)
    {
        long lastCb = Interlocked.Read(ref lastDisplayCompleteTicks);
        long nowTicks = DateTime.UtcNow.Ticks;
        long sinceLastCbTicks = nowTicks - lastCb;
        if (sinceLastCbTicks >= quietPeriod.Ticks)
        {
            Console.WriteLine($"  quiet ({label}) after {(nowTicks - startTicks) / TimeSpan.TicksPerMillisecond}ms");
            return;
        }
        if (nowTicks >= maxWaitTicks)
        {
            Console.WriteLine($"  (max wait hit for {label} at {maxFrameWait.TotalMilliseconds}ms — continuing)");
            return;
        }
        long remainingTicks = quietPeriod.Ticks - sinceLastCbTicks;
        int delayMs = Math.Max(20, (int)(remainingTicks / TimeSpan.TicksPerMillisecond));
        await Task.Delay(delayMs);
    }
}

async Task SendRaw(byte[] buffer, string description)
{
    Console.WriteLine($"SEND: {description}");

    // Atomic clear first. Without this, pins from the previous test (or stale
    // state from a wedged earlier session) leak through because per-line acks
    // can be missed during the streamed DisplayData below.
    try { native.ResetDisplay(deviceHandle); }
    catch (Exception ex) { Console.WriteLine($"  ResetDisplay threw (non-fatal): {ex.GetType().Name}: {ex.Message}"); }
    await WaitForQuietAsync("reset");

    // Single DisplayData. Re-sending the same buffer is detected by the SDK as
    // DOT_ERROR_DISPLAY_DATA_UNCHAGNED (no-op) and can collide with the in-flight
    // per-line transmission of the first send — earlier versions did this in the
    // name of reliability and it made things worse.
    try
    {
        bool ok = native.DisplayGraphicData(deviceHandle, buffer);
        if (!ok)
        {
            Console.WriteLine("  DisplayGraphicData returned false (likely DISPLAY_IN_PROGRESS or DATA_UNCHANGED).");
            return;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  DisplayGraphicData threw: {ex.GetType().Name}: {ex.Message}");
        return;
    }
    await WaitForQuietAsync("display");
}

byte[] OnlyCell(int byteIdx, byte value)
{
    var buf = new byte[byteCount];
    if (byteIdx >= 0 && byteIdx < byteCount) buf[byteIdx] = value;
    return buf;
}

byte[] AllCells(byte value)
{
    var buf = new byte[byteCount];
    for (int i = 0; i < buf.Length; i++) buf[i] = value;
    return buf;
}

static string Position(int bit) => bit switch
{
    0 => "dot 1 (top-left)",
    1 => "dot 2 (upper-middle-left)",
    2 => "dot 3 (lower-middle-left)",
    3 => "dot 7 (bottom-left)",
    4 => "dot 4 (top-right)",
    5 => "dot 5 (upper-middle-right)",
    6 => "dot 6 (lower-middle-right)",
    7 => "dot 8 (bottom-right)",
    _ => "?"
};

void NoteDifferentPin()
{
    Console.WriteLine("  >> If a DIFFERENT pin rises, the SDK uses a different convention —");
    Console.WriteLine("  >> please tell Claude exactly which pin actually went up (row from top, column from left).");
}

void WaitEnter(string prompt = "Press Enter for next...")
{
    Console.Write(prompt);
    Console.ReadLine();
}

// ── Menu loop ──────────────────────────────────────────────────────────────────
while (true)
{
    Console.WriteLine();
    Console.WriteLine("=== Dot Pad Calibrator ===");
    Console.WriteLine($"Display: {cellsX}x{cellsY} cells = {dotsX}x{dotsY} dots, {brailleCells} text cells");
    Console.WriteLine();
    Console.WriteLine("  Graphic area:");
    Console.WriteLine("    1. Clear display (all pins down)");
    Console.WriteLine("    2. Fill display (all pins up — 0xFF in every cell)");
    Console.WriteLine("    3. Bit-order probe — light each bit (0..7) of cell 0 in turn");
    Console.WriteLine("    4. Cell-index probe — light cell N as 0xFF (you pick N)");
    Console.WriteLine("    5. Coordinate dot probe — type x,y in dot space");
    Console.WriteLine("    6. Stripe — left column (x=0)");
    Console.WriteLine("    7. Stripe — top row (y=0)");
    Console.WriteLine("    8. Stripe — right column");
    Console.WriteLine("    9. Stripe — bottom row");
    Console.WriteLine("    d. Diagonal — dot at (i,i) for i=0..min(dotsX,dotsY)-1");
    Console.WriteLine("    r. Reset graphic display only (DOT_PAD_RESET_DISPLAY)");
    Console.WriteLine("    F. Load .dtm graphic file (DOT_PAD_DISPLAY_FILE)");
    Console.WriteLine();
    Console.WriteLine("  Braille strip:");
    Console.WriteLine("    s. Strip text via translator (DOT_PAD_BRAILLE_DISPLAY)");
    Console.WriteLine("    a. Strip braille-ASCII directly (DOT_PAD_BRAILLE_ASCII_DISPLAY)");
    Console.WriteLine("    R. Reset braille strip (DOT_PAD_RESET_BRAILLE_DISPLAY)");
    Console.WriteLine();
    Console.WriteLine("  Info queries (result arrives via MSG callback):");
    Console.WriteLine("    n. Device name");
    Console.WriteLine("    f. Firmware version");
    Console.WriteLine("    h. Hardware version");
    Console.WriteLine();
    Console.WriteLine("  k. Key-listen mode — press keys on device; press Enter here to exit");
    Console.WriteLine("  q. Quit");
    Console.Write("> ");

    var raw = Console.ReadLine();
    if (raw is null) continue;
    var choice = raw.Trim();

    if (choice == "1")
    {
        Console.WriteLine("EXPECT: all pins lowered (no dots raised anywhere).");
        NoteDifferentPin();
        await SendRaw(AllCells(0x00), "All-zero 300-byte buffer");
    }
    else if (choice == "2")
    {
        Console.WriteLine("EXPECT: every pin in the graphic area raised — solid pin field, no gaps.");
        NoteDifferentPin();
        await SendRaw(AllCells(0xFF), "All-cells 0xFF (every of 8 dots raised in every cell)");
    }
    else if (choice == "3")
    {
        for (int bit = 0; bit < 8; bit++)
        {
            byte val = (byte)(1 << bit);
            Console.WriteLine();
            Console.WriteLine($"Cell 0 bit {bit} = 0x{val:X2}.");
            Console.WriteLine($"Under our 8-dot layout this is {Position(bit)} of the top-left cell.");
            Console.WriteLine("EXPECT: a single pin raised in the very top-left cell, at the position above.");
            NoteDifferentPin();
            await SendRaw(OnlyCell(0, val), $"Cell 0 = 0x{val:X2}");
            WaitEnter();
        }
    }
    else if (choice == "4")
    {
        Console.Write($"Cell index 0..{byteCount - 1} (or blank to cancel): ");
        var line = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(line) && int.TryParse(line.Trim(), out int n) && n >= 0 && n < byteCount)
        {
            int rowMajorRow = n / cellsX;
            int rowMajorCol = n % cellsX;
            int colMajorRow = n % cellsY;
            int colMajorCol = n / cellsY;
            Console.WriteLine($"Sending cell {n} = 0xFF (all 8 pins of that cell raised).");
            Console.WriteLine($"  If row-major (cells go ACROSS then down): this is cell at row {rowMajorRow}, col {rowMajorCol}.");
            Console.WriteLine($"  If column-major (cells go DOWN then across): row {colMajorRow}, col {colMajorCol}.");
            Console.WriteLine("EXPECT (row-major assumption): a single 2x4 dot cluster raised at row/col above.");
            NoteDifferentPin();
            await SendRaw(OnlyCell(n, 0xFF), $"Cell {n} = 0xFF");
        }
        else
        {
            Console.WriteLine("Cancelled / invalid input.");
        }
    }
    else if (choice == "5")
    {
        Console.Write($"x,y in dot space (x=0..{dotsX - 1}, y=0..{dotsY - 1}): ");
        var line = Console.ReadLine();
        if (line is not null)
        {
            var parts = line.Split(',', 2);
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out int x)
                && int.TryParse(parts[1].Trim(), out int y)
                && x >= 0 && x < dotsX && y >= 0 && y < dotsY)
            {
                int cellX = x / 2, cellY = y / 4;
                int subX = x % 2, subY = y % 4;
                int bit = subY + (subX * 4);
                int byteIdx = cellY * cellsX + cellX;
                Console.WriteLine($"Lit dot ({x},{y}) → cell ({cellX},{cellY}) sub ({subX},{subY}) → byte {byteIdx} bit {bit}.");
                Console.WriteLine($"Expected physical pin: row {y} from the top, column {x} from the left ({Position(bit)} of cell {cellX},{cellY}).");
                NoteDifferentPin();
                await SendRaw(OnlyCell(byteIdx, (byte)(1 << bit)), $"Dot ({x},{y})");
            }
            else
            {
                Console.WriteLine("Invalid input. Use the form: 5,7");
            }
        }
    }
    else if (choice == "6")
    {
        // Left column of dots: in every cell-row, set the LEFT column of cell-column 0.
        // Left column dots = 1, 2, 3, 7 = bits 0, 1, 2, 3 = 0x0F.
        var buf = new byte[byteCount];
        for (int cy = 0; cy < cellsY; cy++)
        {
            int byteIdx = cy * cellsX + 0;
            buf[byteIdx] = 0x0F;
        }
        Console.WriteLine("EXPECT: a continuous single-pin line down the entire left edge (column x=0), no gaps.");
        NoteDifferentPin();
        await SendRaw(buf, "Left-column stripe (cellX=0, bits 0..3 = dots 1,2,3,7 → 0x0F)");
    }
    else if (choice == "7")
    {
        // Top row of dots: in every cell of the top cell-row, set the TOP row pins.
        // Top row dots = 1, 4 = bits 0, 4 = 0x11.
        var buf = new byte[byteCount];
        for (int cx = 0; cx < cellsX; cx++)
        {
            int byteIdx = 0 * cellsX + cx;
            buf[byteIdx] = 0x11;
        }
        Console.WriteLine("EXPECT: a single-pin line across the entire top edge (row y=0), no second row below.");
        NoteDifferentPin();
        await SendRaw(buf, "Top-row stripe (cellY=0, bits 0,4 = dots 1,4 → 0x11)");
    }
    else if (choice == "8")
    {
        // Right column of dots: in every cell-row, set the RIGHT column of the last cell-column.
        // Right column dots = 4, 5, 6, 8 = bits 4, 5, 6, 7 = 0xF0.
        var buf = new byte[byteCount];
        for (int cy = 0; cy < cellsY; cy++)
        {
            int byteIdx = cy * cellsX + (cellsX - 1);
            buf[byteIdx] = 0xF0;
        }
        Console.WriteLine($"EXPECT: a continuous single-pin line down the entire right edge (column x={dotsX - 1}), no gaps.");
        NoteDifferentPin();
        await SendRaw(buf, "Right-column stripe (cellX=cellsX-1, bits 4..7 = dots 4,5,6,8 → 0xF0)");
    }
    else if (choice == "9")
    {
        // Bottom row of dots: in every cell of the bottom cell-row, set the BOTTOM row pins.
        // Bottom row dots = 7, 8 = bits 3, 7 = 0x88.
        var buf = new byte[byteCount];
        for (int cx = 0; cx < cellsX; cx++)
        {
            int byteIdx = (cellsY - 1) * cellsX + cx;
            buf[byteIdx] = 0x88;
        }
        Console.WriteLine($"EXPECT: a single-pin line across the entire bottom edge (row y={dotsY - 1}), no second row above.");
        NoteDifferentPin();
        await SendRaw(buf, "Bottom-row stripe (cellY=cellsY-1, bits 3,7 = dots 7,8 → 0x88)");
    }
    else if (choice == "d")
    {
        var buf = new byte[byteCount];
        int n = Math.Min(dotsX, dotsY);
        for (int i = 0; i < n; i++)
        {
            int x = i, y = i;
            int cellX = x / 2, cellY = y / 4;
            int subX = x % 2, subY = y % 4;
            int bit = subY + (subX * 4);
            int byteIdx = cellY * cellsX + cellX;
            buf[byteIdx] |= (byte)(1 << bit);
        }
        Console.WriteLine($"EXPECT: a diagonal line of {n} dots from top-left toward (min(dotsX,dotsY)-1, same).");
        NoteDifferentPin();
        await SendRaw(buf, $"Diagonal (i,i) for i=0..{n - 1}");
    }
    else if (choice == "r")
    {
        Console.WriteLine("Resetting graphic display only (DOT_PAD_RESET_DISPLAY).");
        try
        {
            bool ok = native.ResetDisplay(deviceHandle);
            Console.WriteLine($"  ResetDisplay returned {ok}.");
            await WaitForQuietAsync("reset-standalone");
        }
        catch (Exception ex) { Console.WriteLine($"  ResetDisplay threw: {ex.GetType().Name}: {ex.Message}"); }
    }
    else if (choice == "F")
    {
        Console.Write("Path to .dtm file: ");
        var path = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                bool ok = native.DisplayGraphicFile(deviceHandle, path.Trim());
                Console.WriteLine($"  DisplayGraphicFile returned {ok}.");
                await WaitForQuietAsync("display-file");
            }
            catch (Exception ex) { Console.WriteLine($"  DisplayGraphicFile threw: {ex.GetType().Name}: {ex.Message}"); }
        }
    }
    else if (choice == "s")
    {
        Console.Write("Text to send to the strip (translator pass): ");
        var text = Console.ReadLine();
        if (text is not null)
        {
            Console.WriteLine($"Sending {text.Length} chars via DOT_PAD_BRAILLE_DISPLAY ({brailleCells} cells available).");
            try { native.ResetBrailleDisplay(deviceHandle); }
            catch (Exception ex) { Console.WriteLine($"  ResetBrailleDisplay threw (non-fatal): {ex.GetType().Name}: {ex.Message}"); }
            try
            {
                bool ok = native.DisplayBrailleText(deviceHandle, text, DotPadLanguage.English, DotPadBrailleGrade.Grade2);
                Console.WriteLine($"  DisplayBrailleText returned {ok}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  DisplayBrailleText threw: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
    else if (choice == "a")
    {
        Console.Write("Braille ASCII string (each char = one 6-dot cell): ");
        var text = Console.ReadLine();
        if (text is not null)
        {
            try { native.ResetBrailleDisplay(deviceHandle); }
            catch (Exception ex) { Console.WriteLine($"  ResetBrailleDisplay threw (non-fatal): {ex.GetType().Name}: {ex.Message}"); }
            try
            {
                bool ok = native.DisplayBrailleAscii(deviceHandle, text);
                Console.WriteLine($"  DisplayBrailleAscii returned {ok}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  DisplayBrailleAscii threw: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
    else if (choice == "R")
    {
        try
        {
            bool ok = native.ResetBrailleDisplay(deviceHandle);
            Console.WriteLine($"  ResetBrailleDisplay returned {ok}.");
        }
        catch (Exception ex) { Console.WriteLine($"  ResetBrailleDisplay threw: {ex.GetType().Name}: {ex.Message}"); }
    }
    else if (choice == "n")
    {
        try
        {
            bool ok = native.RequestDeviceName(deviceHandle);
            Console.WriteLine($"  RequestDeviceName returned {ok}. Result arrives via MSG: DeviceName callback.");
        }
        catch (Exception ex) { Console.WriteLine($"  RequestDeviceName threw: {ex.GetType().Name}: {ex.Message}"); }
    }
    else if (choice == "f")
    {
        try
        {
            bool ok = native.RequestFirmwareVersion(deviceHandle);
            Console.WriteLine($"  RequestFirmwareVersion returned {ok}. Result arrives via MSG: DeviceFwVersion callback.");
        }
        catch (Exception ex) { Console.WriteLine($"  RequestFirmwareVersion threw: {ex.GetType().Name}: {ex.Message}"); }
    }
    else if (choice == "h")
    {
        try
        {
            bool ok = native.RequestHardwareVersion(deviceHandle);
            Console.WriteLine($"  RequestHardwareVersion returned {ok}. Result arrives via MSG: DeviceHwVersion callback.");
        }
        catch (Exception ex) { Console.WriteLine($"  RequestHardwareVersion threw: {ex.GetType().Name}: {ex.Message}"); }
    }
    else if (choice == "k")
    {
        Console.WriteLine("Press keys on the device. Press Enter on this terminal to exit listen mode.");
        keyListenMode = true;
        Console.ReadLine();
        keyListenMode = false;
        Console.WriteLine("Exited listen mode.");
    }
    else if (choice == "q")
    {
        Console.WriteLine("Disconnecting...");
        try { native.Disconnect(deviceHandle); }
        catch (Exception ex) { Console.WriteLine($"Disconnect threw: {ex.GetType().Name}: {ex.Message}"); }
        Console.WriteLine("Disconnected.");
        return;
    }
    else
    {
        Console.WriteLine($"Unknown option: '{choice}'");
    }
}

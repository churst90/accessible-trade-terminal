using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AccessibleTrader.WebHost.Services.Tray
{
    /// <summary>
    /// Windows tray via Shell_NotifyIcon and a native popup menu, driven on a dedicated
    /// message-pump thread so it never touches the Kestrel thread. The popup is a real Win32
    /// menu, so NVDA/JAWS read it natively; "speech" is delivered as a balloon notification
    /// (which screen readers announce) rather than depending on SAPI.
    ///
    /// UNVERIFIED ON THIS BUILD BOX (Linux): compiles on net10.0 (DllImport is TFM-agnostic)
    /// and is guarded by OperatingSystem.IsWindows(); needs a Windows smoke test before trust.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsTrayPlatform : ITrayPlatform
    {
        private const int WM_APP_TRAY = 0x0400 + 1;
        private const int WM_RBUTTONUP = 0x0205, WM_LBUTTONUP = 0x0202, WM_CONTEXTMENU = 0x007B;
        private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
        private const int NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10;
        private const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002;
        private const int WM_QUIT = 0x0012;

        private readonly ILogger _logger;
        private TrayModel? _model;
        private IntPtr _hwnd;
        private Thread? _pump;
        private volatile bool _running;
        private string _tip = "Accessible Trade Terminal";
        private WndProcDelegate? _wndProc; // kept alive against GC

        public WindowsTrayPlatform(ILogger logger) => _logger = logger;

        public bool Initialize(TrayModel model)
        {
            if (!OperatingSystem.IsWindows()) return false;
            _model = model;
            _tip = model.InitialTitle;
            var ready = new ManualResetEventSlim(false);
            bool ok = false;
            _pump = new Thread(() =>
            {
                try { ok = CreateWindowAndIcon(); }
                finally { ready.Set(); }
                if (ok) MessageLoop();
            }) { IsBackground = true, Name = "att-tray" };
            _pump.SetApartmentState(ApartmentState.STA);
            _pump.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
            _running = ok;
            return ok;
        }

        private bool CreateWindowAndIcon()
        {
            try
            {
                _wndProc = WndProc;
                var wc = new WNDCLASS
                {
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = GetModuleHandle(null),
                    lpszClassName = "AttTrayWindow",
                };
                RegisterClass(ref wc);
                _hwnd = CreateWindowEx(0, "AttTrayWindow", "ATT", 0, 0, 0, 0, 0,
                    new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
                if (_hwnd == IntPtr.Zero) return false;

                var nid = NewIconData();
                nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
                nid.uCallbackMessage = WM_APP_TRAY;
                nid.hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
                nid.szTip = _tip;
                return Shell_NotifyIcon(NIM_ADD, ref nid);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Windows tray init failed."); return false; }
        }

        private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_APP_TRAY)
            {
                int e = (int)(lParam.ToInt64() & 0xFFFF);
                if (e == WM_RBUTTONUP || e == WM_CONTEXTMENU || e == WM_LBUTTONUP) ShowMenu();
                return IntPtr.Zero;
            }
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private void ShowMenu()
        {
            if (_model == null) return;
            IntPtr menu = CreatePopupMenu();
            foreach (var item in _model.Items)
                AppendMenu(menu, 0x0000 /* MF_STRING */, (uint)item.Id, item.Label());

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hwnd);
            uint cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            DestroyMenu(menu);
            if (cmd != 0)
            {
                var chosen = _model.Items.FirstOrDefault(i => i.Id == (int)cmd);
                chosen?.OnActivate();
            }
        }

        public void UpdateLabel(string title)
        {
            _tip = title;
            if (!_running) return;
            try
            {
                var nid = NewIconData();
                nid.uFlags = NIF_TIP;
                nid.szTip = title;
                Shell_NotifyIcon(NIM_MODIFY, ref nid);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Windows tray label update failed."); }
        }

        public void Speak(string text)
        {
            // A balloon notification — screen readers announce it, no SAPI dependency.
            if (!_running) return;
            try
            {
                var nid = NewIconData();
                nid.uFlags = NIF_INFO;
                nid.szInfo = text;
                nid.szInfoTitle = "Accessible Trade Terminal";
                Shell_NotifyIcon(NIM_MODIFY, ref nid);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Windows tray balloon failed."); }
        }

        public void OpenUrl(string url) => TryStart(url, useShell: true);
        public void CopyToClipboard(string text) => PipeToProcess("clip", text);

        private void MessageLoop()
        {
            while (GetMessage(out MSG m, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref m);
                DispatchMessage(ref m);
            }
        }

        private NOTIFYICONDATA NewIconData() => new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
        };

        private void TryStart(string target, bool useShell)
        {
            try { using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = useShell }); }
            catch (Exception ex) { _logger.LogDebug(ex, "Windows tray open {Target} failed.", target); }
        }

        private void PipeToProcess(string file, string stdin)
        {
            try
            {
                var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p != null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Windows clipboard failed."); }
        }

        public void Dispose()
        {
            if (!_running) return;
            _running = false;
            try
            {
                var nid = NewIconData();
                Shell_NotifyIcon(NIM_DELETE, ref nid);
                if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Windows tray dispose failed."); }
        }

        // ── P/Invoke ─────────────────────────────────────────────────────────

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct WNDCLASS
        {
            public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
            public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize; public IntPtr hWnd; public uint uID; public uint uFlags;
            public uint uCallbackMessage; public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState; public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MSG
        {
            public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WNDCLASS wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, uint id, string item);
        [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr inst, IntPtr name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    }
}

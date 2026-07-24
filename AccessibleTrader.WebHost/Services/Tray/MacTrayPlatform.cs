using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.WebHost.Services.Tray
{
    /// <summary>
    /// macOS platform actions (speech via <c>say</c>, browser via <c>open</c>, clipboard via
    /// <c>pbcopy</c>). The panel STATUS ITEM itself is deliberately NOT created here.
    ///
    /// Why: an NSStatusItem needs AppKit's run loop, which lives on the process main thread —
    /// the Kestrel host owns that in the WebHost server. Driving it would require unverified
    /// Objective-C interop (objc_msgSend), and a single wrong selector / type-encoding there is
    /// a native crash that takes the whole server down — strictly worse than no icon. The
    /// correct home for a native macOS tray is the MAUI Mac Catalyst head, which already owns
    /// the AppKit main thread. So on macOS the WebHost keeps the background monitor + these
    /// actions, and Initialize returns false (no icon) rather than risking a segfault.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    public sealed class MacTrayPlatform : ITrayPlatform
    {
        private readonly ILogger _logger;
        public MacTrayPlatform(ILogger logger) => _logger = logger;

        public bool Initialize(TrayModel model)
        {
            _logger.LogInformation(
                "macOS: WebHost runs without a panel icon (AppKit main-thread run loop can't be " +
                "hosted safely from the server). Use the MAUI Mac head for a native tray; the " +
                "background monitor and alerts still run here.");
            return false;
        }

        public void UpdateLabel(string title) { /* no icon on macOS WebHost */ }
        public void Speak(string text) => Run("say", text);
        public void OpenUrl(string url) => Run("open", url);
        public void CopyToClipboard(string text) => Pipe("pbcopy", text);

        private void Run(string file, string arg)
        {
            try { using var _ = Process.Start(new ProcessStartInfo(file, arg) { UseShellExecute = false, CreateNoWindow = true }); }
            catch (Exception ex) { _logger.LogDebug(ex, "macOS command {File} failed.", file); }
        }

        private void Pipe(string file, string stdin)
        {
            try
            {
                var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p != null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "macOS clipboard failed."); }
        }

        public void Dispose() { }
    }
}

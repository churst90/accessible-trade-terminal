using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AccessibleTrader.Launcher;

/// <summary>
/// Starts the desktop app from the <c>app</c> folder beside this exe, passing on any arguments,
/// and exits. The working directory is the app's own folder, as it is when the app's exe is
/// double-clicked: the Dot Pad SDK opens a MeCab file by a path relative to it.
///
/// <para>
/// Built with <c>-p:LauncherApp=WebHost</c> it starts the WebHost instead, whose content root
/// (wwwroot, appsettings.json) is its working directory, so the same rule applies. The WebHost
/// is a console program and this exe has no console, so Windows gives the server its own
/// console window: that window is the running server, and closing it stops it.
/// </para>
/// </summary>
internal static partial class Program
{
    internal const string AppFolder = "app";
#if WEBHOST
    internal const string AppExe = "AccessibleTrader.WebHost.exe";
#else
    internal const string AppExe = "AccessibleTrader.BlazorClient.exe";
#endif

    private static int Main(string[] args)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, AppFolder);
        string exe = Path.Combine(dir, AppExe);

        if (!File.Exists(exe))
        {
            Show($"Accessible Trader could not start: {exe} is missing.\n\n"
               + "Unzip the whole download, keeping the app folder beside Accessible Trader.exe, "
               + "or download it again.");
            return 1;
        }

        var start = new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = false };
        foreach (string a in args) start.ArgumentList.Add(a);

        try
        {
#if WEBHOST
            using var server = Process.Start(start);
            return server is null ? 0 : WatchServerStart(server);
#else
            Process.Start(start)?.Dispose();
            return 0;
#endif
        }
        catch (Exception ex)
        {
            Show($"Accessible Trader could not start {exe}.\n\n{ex.Message}");
            return 1;
        }
    }

#if WEBHOST
    internal const int StartupSeconds = 30;

    /// <summary>
    /// The server's console window is its only voice, and it closes the moment the server exits.
    /// A server that fails as it starts (port 5145 taken, most often by a copy already running)
    /// would flash a window and leave a screen reader user with nothing, so a failure in the
    /// first <see cref="StartupSeconds"/> seconds is said in a message box. After that, an exit
    /// is the user closing the window, and says nothing.
    /// </summary>
    private static int WatchServerStart(Process server)
    {
        if (!server.WaitForExit(TimeSpan.FromSeconds(StartupSeconds)) || server.ExitCode == 0) return 0;
        Show($"The Accessible Trader server stopped as it started (exit code {server.ExitCode}).\n\n"
           + "If it is already running, use the browser tab it opened, or go to http://localhost:5145. "
           + "If something else is using port 5145, close it and try again.\n\n"
           + $"To read the server's own message, open a command prompt in {Path.Combine(AppContext.BaseDirectory, AppFolder)} "
           + $"and run {AppExe}.");
        return server.ExitCode;
    }
#endif

    private static void Show(string text)
    {
        // A message box, not a console line: this is a windowed exe, so a screen reader user
        // would otherwise hear nothing at all when it fails.
        if (OperatingSystem.IsWindows())
            MessageBoxW(IntPtr.Zero, text, "Accessible Trader", MbOkIconError);
        else
            Console.Error.WriteLine(text);
    }

    private const uint MbOkIconError = 0x10;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}

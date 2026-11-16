using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AccessibleTrader.Launcher;

/// <summary>
/// Starts the desktop app from the <c>app</c> folder beside this exe, passing on any arguments,
/// and exits. The working directory is the app's own folder, as it is when the app's exe is
/// double-clicked: the Dot Pad SDK opens a MeCab file by a path relative to it.
/// </summary>
internal static partial class Program
{
    internal const string AppFolder = "app";
    internal const string AppExe = "AccessibleTrader.BlazorClient.exe";

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
            Process.Start(start)?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            Show($"Accessible Trader could not start {exe}.\n\n{ex.Message}");
            return 1;
        }
    }

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

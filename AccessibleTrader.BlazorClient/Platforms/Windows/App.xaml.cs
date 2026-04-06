using Microsoft.UI.Xaml;

namespace AccessibleTrader.BlazorClient.WinUI;

public partial class App : MauiWinUIApplication
{
	public App()
	{
		// Catch WinUI-level unhandled exceptions — these would otherwise silently kill the process.
		this.UnhandledException += (_, e) =>
		{
			e.Handled = true;
			var msg = $"[CRASH] WinUI.UnhandledException: {e.Exception}";
			Console.Error.WriteLine(msg);
			try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessibleTrader", "crash.log"), msg + Environment.NewLine); } catch { }
		};

		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}


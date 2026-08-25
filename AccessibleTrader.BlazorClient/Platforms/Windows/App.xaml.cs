using Microsoft.UI.Xaml;

namespace AccessibleTrader.BlazorClient.WinUI;

public partial class App : MauiWinUIApplication
{
	public App()
	{
		// Record WinUI-level unhandled exceptions, which would otherwise die without a trace.
		//
		// This deliberately does NOT set e.Handled. Swallowing every unhandled exception keeps
		// the process alive in a state nothing has reasoned about — a half-built window, a
		// disposed audio driver, a broker call that unwound past its own bookkeeping — and the
		// user, who cannot see that the UI has stopped repainting, goes on trading against it.
		// Crashing on the way out is the honest outcome for an exception nobody caught; the log
		// written here is what makes the crash diagnosable afterwards.
		this.UnhandledException += (_, e) =>
		{
			var msg = $"[CRASH] WinUI.UnhandledException: {e.Exception}";
			Console.Error.WriteLine(msg);
			try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessibleTrader", "crash.log"), msg + Environment.NewLine); } catch { }
		};

		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}


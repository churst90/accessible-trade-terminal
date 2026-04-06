using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace AccessibleTrader.BlazorClient;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Catch any unhandled exceptions on background threads and write them to a crash log.
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			var msg = $"[CRASH] AppDomain.UnhandledException: {e.ExceptionObject}";
			Console.Error.WriteLine(msg);
			try { File.AppendAllText(Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "crash.log"), msg + Environment.NewLine); } catch { }
		};
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			var msg = $"[CRASH] UnobservedTaskException: {e.Exception}";
			Console.Error.WriteLine(msg);
			try { File.AppendAllText(Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "crash.log"), msg + Environment.NewLine); } catch { }
			e.SetObserved();
		};

			var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseSkiaSharp()   // Registers SKCanvasView handler — required for chart rendering.
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			})
			.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddHandler<BlazorWebView, AccessibleTrader.BlazorClient.Services.TransparentBlazorWebViewHandler>();
#if MACCATALYST
				handlers.AddHandler<ContentPage, KeyboardPageHandler>();
#endif
#if IOS
				handlers.AddHandler<ContentPage, KeyboardPageHandler>();
#endif
			})
			;

		builder.Services.AddMauiBlazorWebView();
        builder.Services.AddAccessibleTraderServices();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}

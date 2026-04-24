using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
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

#if ANDROID
        // Swap the Core-side IScriptWorkerLauncher (which is a routing
        // stub on Android) for the real platform launcher. The real one
        // binds a ScriptWorkerService running under android:isolatedProcess=true
        // and carries frames over ParcelFileDescriptor pipes. Adding it
        // here as a later Singleton registration makes it the
        // effective resolution via GetRequiredService<T> — standard
        // .NET DI "last registration wins" semantics.
        builder.Services.AddSingleton<AccessibleTrader.Core.Services.Scripting.IScriptWorkerLauncher>(_ =>
            new AccessibleTrader.BlazorClient.Platforms.Android.AndroidIsolatedProcessLauncher(
                global::Android.App.Application.Context));
#endif

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// Expose host-owned services to plugins that can't take a Core reference.
		// Set once after the container is built; plugins activated later pick
		// them up via the PluginHostServices static accessors. Safe to run even
		// if no plugin uses any of them.
		try
		{
			var pluginSecureStorage = app.Services.GetService<AccessibleTrader.Sdk.Services.IPluginSecureStorage>();
			if (pluginSecureStorage != null)
				AccessibleTrader.Sdk.Services.PluginHostServices.SecureStorage = pluginSecureStorage;

			// Phase 4 Track B: sign-time credential checkout (replaces long-lived
			// _apiKey/_apiSecret fields in trading providers) and a host-owned
			// HttpClient factory with per-provider outbound-host allow-list.
			var apiKeyCheckout = app.Services.GetService<AccessibleTrader.Sdk.Services.IApiKeyCheckout>();
			if (apiKeyCheckout != null)
				AccessibleTrader.Sdk.Services.PluginHostServices.ApiKeys = apiKeyCheckout;

			var httpFactory = app.Services.GetService<AccessibleTrader.Sdk.Services.IPluginHttpClientFactory>();
			if (httpFactory != null)
				AccessibleTrader.Sdk.Services.PluginHostServices.HttpClientFactory = httpFactory;

			// Security-event audit log. Core + plugin code records
			// AppContainer fallbacks, memory-quota kills, credential
			// checkout failures, token-cleanup failures, etc. here.
			var secLog = app.Services.GetService<AccessibleTrader.Sdk.Services.ISecurityEventLog>();
			if (secLog != null)
				AccessibleTrader.Sdk.Services.PluginHostServices.SecurityEvents = secLog;

			// Phase 10-F(b): shared indicator cache bridge. DLL-plugin strategies and
			// Roslyn strategies access it via PluginHostServices.IndicatorCache instead
			// of the Core-only IStrategyIndicatorCache, so plugin assemblies don't need
			// a Core reference.
			var indicatorCache = app.Services.GetService<AccessibleTrader.Core.Services.IStrategyIndicatorCache>();
			if (indicatorCache != null)
				AccessibleTrader.Sdk.Services.PluginHostServices.IndicatorCache = indicatorCache;
		}
		catch { /* non-fatal — plugins fall back to their local behaviour */ }

		return app;
	}
}

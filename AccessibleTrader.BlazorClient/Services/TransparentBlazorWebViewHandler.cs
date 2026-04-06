using Microsoft.AspNetCore.Components.WebView.Maui;

#if WINDOWS
using Microsoft.UI.Xaml.Controls;
#elif ANDROID
using Android.Webkit;
#elif IOS || MACCATALYST
using UIKit;
using WebKit;
#endif

namespace AccessibleTrader.BlazorClient.Services;

/// <summary>
/// Custom BlazorWebViewHandler that sets the native WebView's background to Transparent
/// as early as possible.
/// </summary>
public class TransparentBlazorWebViewHandler : BlazorWebViewHandler
{
#if WINDOWS
    protected override WebView2 CreatePlatformView()
    {
        var wv2 = base.CreatePlatformView();
        wv2.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
        
        wv2.CoreWebView2Initialized += (sender, args) =>
        {
            if (sender is WebView2 webView)
            {
                webView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            }
        };

        return wv2;
    }
#elif ANDROID
    protected override global::Android.Webkit.WebView CreatePlatformView()
    {
        var webView = base.CreatePlatformView();
        webView.SetBackgroundColor(global::Android.Graphics.Color.Transparent);
        return webView;
    }
#elif IOS || MACCATALYST
    protected override WKWebView CreatePlatformView()
    {
        var webView = base.CreatePlatformView();
        webView.Opaque = false;
        webView.BackgroundColor = UIColor.Clear;
        return webView;
    }
#endif
}

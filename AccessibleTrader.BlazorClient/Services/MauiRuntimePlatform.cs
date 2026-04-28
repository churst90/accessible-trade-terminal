using AccessibleTrader.Core.Services;
using Microsoft.Maui.Devices;

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// MAUI-host implementation of <see cref="IRuntimePlatform"/> backed by
    /// <c>Microsoft.Maui.Devices.DeviceInfo</c>. Lives in the BlazorClient
    /// project so the platform-agnostic Components RCL stays free of
    /// <c>Microsoft.Maui.*</c> dependencies.
    /// </summary>
    public sealed class MauiRuntimePlatform : IRuntimePlatform
    {
        public bool IsIos          => DeviceInfo.Current.Platform == DevicePlatform.iOS;
        public bool IsAndroid      => DeviceInfo.Current.Platform == DevicePlatform.Android;
        public bool IsWindows      => DeviceInfo.Current.Platform == DevicePlatform.WinUI;
        public bool IsMacCatalyst  => DeviceInfo.Current.Platform == DevicePlatform.MacCatalyst;
    }
}

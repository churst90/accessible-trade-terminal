using System.Runtime.InteropServices;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// WebHost implementation of <see cref="IRuntimePlatform"/> backed by
    /// <see cref="RuntimeInformation"/>. The MAUI head answers based on
    /// <c>DeviceInfo.Current.Platform</c>; that type isn't reachable here
    /// (no MAUI dependency), so we map from the OS reported by the BCL.
    ///
    /// Linux is not represented in the existing seam — callers branch on
    /// IsIos/IsAndroid/IsWindows/IsMacCatalyst to gate platform-specific
    /// features (e.g. iOS refuses Roslyn script compilation). All four
    /// flags return false on Linux, which matches the desired behaviour:
    /// the web host runs in browser context, so platform-specific desktop
    /// codepaths stay off.
    /// </summary>
    public sealed class WebHostRuntimePlatform : IRuntimePlatform
    {
        public bool IsIos         => false;
        public bool IsAndroid     => false;
        public bool IsWindows     => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public bool IsMacCatalyst => false;

        /// <summary>
        /// Overridden to true so <c>ChartArea.razor</c> renders the inline
        /// browser SKCanvasView instead of relying on the native MAUI
        /// canvas overlay that doesn't exist here.
        /// </summary>
        public bool IsBrowserHost => true;
    }
}

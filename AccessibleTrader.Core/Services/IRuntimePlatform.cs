namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Tiny platform-detection seam so platform-agnostic components (in the
    /// `AccessibleTrader.BlazorClient.Components` RCL) can branch on host
    /// without taking a hard dependency on `Microsoft.Maui.Devices`.
    /// </summary>
    /// <remarks>
    /// The MAUI host registers an implementation backed by
    /// <c>Microsoft.Maui.Devices.DeviceInfo</c>. A test or non-MAUI host can
    /// register a stub returning whichever platform shape it wants to
    /// exercise.
    /// </remarks>
    public interface IRuntimePlatform
    {
        /// <summary>True when running on iOS (used by CustomScriptsModal to
        /// disable Roslyn compilation, which Apple's runtime forbids).</summary>
        bool IsIos { get; }

        /// <summary>True when running on Android.</summary>
        bool IsAndroid { get; }

        /// <summary>True when running on Windows.</summary>
        bool IsWindows { get; }

        /// <summary>True when running on macCatalyst.</summary>
        bool IsMacCatalyst { get; }
    }
}

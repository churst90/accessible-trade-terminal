using System.Globalization;
using System.Reflection;
using Xunit.Sdk;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Runs the decorated test (or every test in the decorated class) with the
    /// given thread culture, restoring the original in After.
    ///
    /// Deliberately sets <see cref="Thread.CurrentThread"/>'s culture, NOT
    /// <c>CultureInfo.DefaultThreadCurrentCulture</c>: the default is process-global,
    /// and xUnit runs collections in parallel — a global default would leak a hostile
    /// culture into unrelated tests mid-flight (the same clobber class as the
    /// process-global PluginHostServices fakes). Thread culture flows with
    /// ExecutionContext across awaits, so async tests stay covered.
    ///
    /// This simulates a host that failed to pin invariant culture (the shipped hosts
    /// all pin — CultureInvariantScanTests holds them to it), so anything asserted
    /// under it is proven to hold by per-site pinning alone.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class UseCultureAttribute : BeforeAfterTestAttribute
    {
        private readonly CultureInfo _culture;
        private CultureInfo? _originalCulture;
        private CultureInfo? _originalUiCulture;

        public UseCultureAttribute(string culture) => _culture = new CultureInfo(culture, useUserOverride: false);

        public override void Before(MethodInfo methodUnderTest)
        {
            _originalCulture = Thread.CurrentThread.CurrentCulture;
            _originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentCulture = _culture;
            Thread.CurrentThread.CurrentUICulture = _culture;
            CultureInfo.CurrentCulture.ClearCachedData();
        }

        public override void After(MethodInfo methodUnderTest)
        {
            Thread.CurrentThread.CurrentCulture = _originalCulture!;
            Thread.CurrentThread.CurrentUICulture = _originalUiCulture!;
        }
    }
}

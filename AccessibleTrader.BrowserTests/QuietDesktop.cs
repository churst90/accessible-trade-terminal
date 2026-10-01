using System.Runtime.CompilerServices;

namespace AccessibleTrader.BrowserTests
{
    /// <summary>
    /// Runs as this test assembly loads, before any test: sets
    /// <see cref="AccessibleTrader.WebHost.Services.DesktopOutput.Variable"/> so that no host this
    /// suite starts plays chart tones through the speakers, speaks through spd-say or Orca, raises
    /// notifications or opens a browser. A suite run on Cody's machine did all of that, while he
    /// was working, until 2026-10-01. Child processes inherit it.
    /// AccessibleTrader.Tests' QuietDesktopTests check the switch itself.
    /// </summary>
    internal static class QuietDesktop
    {
        [ModuleInitializer]
        internal static void Engage() =>
            Environment.SetEnvironmentVariable(AccessibleTrader.WebHost.Services.DesktopOutput.Variable, "1");
    }
}

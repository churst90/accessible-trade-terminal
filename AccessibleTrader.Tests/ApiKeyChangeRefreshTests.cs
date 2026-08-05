using System;
using System.IO;
using System.Linq;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Adding an API key must clear the "API key required" sentinel without a
    /// restart.
    ///
    /// <para>
    /// Found by using the terminal: add a key under Alt+K, activate it, and the
    /// symbol dropdown went on demanding a key that had just been supplied. Half
    /// the fix was already there — saving a key calls
    /// <c>ConfigureStoredKeyProvidersAsync</c>, and its comment even says why — but
    /// the market cascade had already filled its symbol list with the sentinel and
    /// nothing recomputed it. The provider was ready; the dropdown was stale.
    /// </para>
    ///
    /// <para>
    /// This is the same family as the quick-trade executor and the background
    /// monitors: correct code that nothing reaches. Here the missing piece was an
    /// event that was never raised, so these check both ends — that it is
    /// published, and that something subscribes.
    /// </para>
    /// </summary>
    public class ApiKeyChangeRefreshTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Read(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        [Fact]
        public void Saving_a_key_publishes_that_keys_changed()
        {
            string modal = Read("AccessibleTrader.BlazorClient.Components", "ApiKeysModal.razor");

            Assert.Contains("new ApiKeysChangedEvent(", modal);
        }

        [Fact]
        public void Activating_a_key_also_publishes_it()
        {
            // Two separate paths reach a usable key: saving with auto-activate, and
            // pressing Activate on an existing profile. Fixing only the first would
            // leave the second broken in exactly the way the user hit.
            string modal = Read("AccessibleTrader.BlazorClient.Components", "ApiKeysModal.razor");

            int publishes = modal.Split("new ApiKeysChangedEvent(").Length - 1;
            Assert.True(publishes >= 2,
                $"Only {publishes} ApiKeysChangedEvent publish site(s). Both saving and activating "
              + "must raise it, or one of the two routes to a working key stays stale.");
        }

        [Fact]
        public void The_market_orchestrator_subscribes_and_refreshes_symbols()
        {
            // An event nobody handles is indistinguishable from no event at all —
            // which is how the quick-trade executor placed no orders for months.
            string orch = Read("AccessibleTrader.Core", "Services", "MarketOrchestrator.cs");

            Assert.Contains("Subscribe<ApiKeysChangedEvent>", orch);

            // And it must actually recompute, not merely observe.
            int at = orch.IndexOf("Subscribe<ApiKeysChangedEvent>", StringComparison.Ordinal);
            string handler = orch.Substring(at, Math.Min(700, orch.Length - at));
            Assert.Contains("RefreshSymbolsAsync", handler);
        }

        [Fact]
        public void The_subscription_is_disposed()
        {
            // The orchestrator is IDisposable and disposes its other subscription;
            // a leaked one keeps a dead component alive after a circuit ends.
            string orch = Read("AccessibleTrader.Core", "Services", "MarketOrchestrator.cs");

            Assert.Contains("_apiKeysChangedSub?.Dispose();", orch);
        }

        [Fact]
        public void The_sentinel_is_still_the_thing_being_cleared()
        {
            // Guards the guard: if the sentinel mechanism were renamed or removed,
            // the tests above would pass while testing nothing meaningful.
            string orch = Read("AccessibleTrader.Core", "Services", "MarketOrchestrator.cs");

            Assert.Contains("ApiKeyRequiredSentinel", orch);
            Assert.Contains("RefreshSymbolsAsync", orch);
        }
    }
}

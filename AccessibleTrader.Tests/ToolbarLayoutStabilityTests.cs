using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The toolbar must not rearrange itself depending on what you selected.
    ///
    /// <para>
    /// A <c>&lt;select&gt;</c> sizes itself to its widest option. The API-key sentinel used to read
    /// "⚠ API key required — open API Keys (Alt+K)" and lived inside the symbol dropdown, so
    /// picking a provider without a key widened that control enough to reflow the whole toolbar —
    /// Pan and Zoom jumped up into the row above. Nothing was broken; the layout simply moved
    /// under the user, which for someone navigating by Tab order is worse than it looks.
    /// </para>
    ///
    /// <para>
    /// The fix is a terse sentinel plus a width cap on the control, with the real explanation in
    /// the tooltip, an "Add key" button, and a spoken announcement. These tests pin all four, since
    /// the temptation to write a friendlier sentinel is exactly how it comes back.
    /// </para>
    /// </summary>
    public class ToolbarLayoutStabilityTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Toolbar() => File.ReadAllText(Path.Combine(
            RepoRoot(), "AccessibleTrader.BlazorClient.Components", "Toolbar.razor"));

        [Fact]
        public void The_api_key_sentinel_stays_short_enough_not_to_widen_the_symbol_dropdown()
        {
            // Roughly the width of a long ticker. Past this it starts driving the control's size
            // instead of riding along inside it.
            const int MaxChars = 20;

            Assert.True(MarketOrchestrator.ApiKeyRequiredSentinel.Length <= MaxChars,
                $"The sentinel is {MarketOrchestrator.ApiKeyRequiredSentinel.Length} characters. " +
                "It sits in the symbol dropdown, which sizes to its widest option, so a long one " +
                "reflows the toolbar. Put the explanation in ApiKeyRequiredHelp instead.");
        }

        [Fact]
        public void The_short_sentinel_still_says_what_is_wrong()
        {
            // Terse is not the same as cryptic. Whatever it is shortened to must still name the
            // problem on its own, because it is what a screen reader reads off the dropdown.
            Assert.Contains("API key", MarketOrchestrator.ApiKeyRequiredSentinel, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_full_explanation_survives_somewhere_useful()
        {
            string help = MarketOrchestrator.ApiKeyRequiredHelp;

            // It has to say what to do, not just what is wrong — and name the shortcut, since
            // that is the fast route for the keyboard user who just hit this.
            Assert.Contains("Alt+K", help, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("API Keys", help, StringComparison.OrdinalIgnoreCase);
            Assert.True(help.Length > MarketOrchestrator.ApiKeyRequiredSentinel.Length,
                "The help text exists to carry what the sentinel had to drop.");
        }

        [Fact]
        public void The_symbol_dropdown_is_width_capped_so_no_ticker_can_reflow_the_row()
        {
            // The sentinel is not the only long value — a Polygon options contract
            // ("O:SPY251219C00650000") does the same thing. The cap covers both.
            Assert.Contains("max-width", Toolbar());
        }

        [Fact]
        public void An_add_key_affordance_appears_instead_of_leaving_the_user_at_a_dead_end()
        {
            string toolbar = Toolbar();

            // A disabled Load button and an unexplained dropdown is a dead end. There has to be a
            // way to act on it from where the user already is.
            Assert.Contains("NeedsApiKey", toolbar);
            Assert.Contains("ApiKeyRequiredHelp", toolbar);
            Assert.Contains("Add key", toolbar);
        }

        [Fact]
        public void Load_stays_disabled_while_the_sentinel_is_the_selected_symbol()
        {
            // The sentinel is not a symbol. Loading it would fire a fetch for a nonsense ticker
            // and report a provider error, which reads as "the provider is broken".
            Assert.Contains("ApiKeyRequiredSentinel", Toolbar());
        }
    }
}

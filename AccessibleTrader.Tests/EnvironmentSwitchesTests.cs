using AccessibleTrader.Core.Services.Security;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The spellings each head accepts for its security switches, pinned once now that both heads
    /// read them through <see cref="EnvironmentSwitches"/> (code review, 2026-11-20).
    /// </summary>
    public class EnvironmentSwitchesTests
    {
        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("1", true)]
        [InlineData("yes", true)]
        [InlineData("0", false)]
        [InlineData("false", false)]
        [InlineData("FALSE", false)]
        public void Security_events_are_written_to_disk_unless_switched_off(string? value, bool persisted) =>
            Assert.Equal(persisted, EnvironmentSwitches.SecurityEventsPersisted(value));

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("0", false)]
        [InlineData("yes", false)]
        [InlineData("1", true)]
        [InlineData("true", true)]
        [InlineData("True", true)]
        public void Unverified_plugins_load_only_on_1_or_true(string? value, bool allowed) =>
            Assert.Equal(allowed, EnvironmentSwitches.UnverifiedPluginsAllowed(value));

        [Fact]
        public void No_head_parses_the_switches_itself_any_more()
        {
            string root = RepoPaths.RepoRoot();
            var heads = new[]
            {
                "AccessibleTrader.BlazorClient/ServiceCollectionExtensions.cs",
                "AccessibleTrader.WebHost/ServiceCollectionExtensions.cs",
                "AccessibleTrader.WebHost/Services/PluginHostBridges.cs",
            };
            foreach (var rel in heads)
            {
                string path = Path.Combine(root, rel);
                Assert.True(File.Exists(path), "moved: " + rel);
                string text = File.ReadAllText(path);
                Assert.Contains("EnvironmentSwitches.", text);
                Assert.DoesNotContain("GetEnvironmentVariable(\"ACCESSIBLETRADER_SECURITY_EVENT", text);
                Assert.DoesNotContain("GetEnvironmentVariable(\"ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS", text);
            }
        }
    }
}

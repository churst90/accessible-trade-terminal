using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A key profile's Environment must reach the provider, because it chooses the HOST.</b>
    ///
    /// <para>
    /// Reported 2026-09-06: "gemini keeps reconnecting ... it says no key" with a Paper profile
    /// saved. The key was fine. <c>GeminiProvider.Configure</c> reads <c>"Environment"</c> to
    /// decide between <c>api.gemini.com</c> and <c>api.sandbox.gemini.com</c>, and neither place
    /// that builds the credential dictionary put the field in — so a sandbox key was signed
    /// against the LIVE venue, which has never heard of it. The symptom looks like a bad key and
    /// is actually a wrong host.
    /// </para>
    ///
    /// <para>
    /// Five plugins read this field and each defaulted differently with it missing: Gemini and
    /// Kraken Futures to live (sandbox keys refused), Alpaca to paper (a live profile quietly
    /// traded on paper), and <b>Tradier to live — a profile marked sandbox would have placed real
    /// orders at a real broker.</b> That last one is why this is a safety test and not a
    /// convenience one.
    /// </para>
    ///
    /// <para>
    /// Same family as the provider-name drift of 2026-08-31: a field the API-keys dialog collects
    /// and nothing downstream ever receives.
    /// </para>
    /// </summary>
    public class CredentialEnvironmentTests
    {
        /// <summary>
        /// A provider that records exactly what <c>Configure</c> was handed. A substitute rather
        /// than a hand-written class: <c>IMarketDataProvider</c> has two dozen members and only
        /// one of them is the subject.
        /// </summary>
        private static (DataService Data, Func<Dictionary<string, string>?> Captured) Build(ApiKeyConfig key)
        {
            var keys = Substitute.For<IApiKeyService>();
            keys.GetAllKeysAsync().Returns(_ => Task.FromResult(new List<ApiKeyConfig> { key }));

            var data = new DataService(
                Substitute.For<IPluginLoaderService>(),
                NullLogger<DataService>.Instance,
                Substitute.For<ICacheService>(),
                keys);

            Dictionary<string, string>? captured = null;
            var provider = Substitute.For<IMarketDataProvider>();
            provider.Name.Returns("Gemini");
            provider.IsConfigured.Returns(false);
            provider.When(x => x.Configure(Arg.Any<Dictionary<string, string>>()))
                    .Do(ci => captured = ci.Arg<Dictionary<string, string>>());

            data.RegisterProvider(provider);
            return (data, () => captured);
        }

        private static ApiKeyConfig Key(string environment) => new(
            Provider: "Gemini", Nickname: "gem", ApiKey: "k", ApiSecret: "s",
            Environment: environment, IsActive: true);

        [Theory]
        [InlineData("Paper")]
        [InlineData("Live")]
        public async Task The_stored_Environment_reaches_the_provider(string environment)
        {
            var (data, captured) = Build(Key(environment));
            await data.InitializeAsync(Substitute.For<IPluginLoaderService>());

            await data.ConfigureStoredKeyProvidersAsync();

            var config = captured();
            Assert.NotNull(config);
            Assert.True(config!.TryGetValue("Environment", out var sent),
                "The provider was configured without an Environment. Gemini, Kraken Futures, "
              + "Tradier, Alpaca and Oanda all read this key to choose which HOST to sign "
              + "against, so its absence sends a sandbox key to the live venue — which reads to "
              + "the user as 'no key'.");
            Assert.Equal(environment, sent);
        }

        [Fact]
        public async Task The_credential_itself_still_arrives()
        {
            // The control. A Configure call carrying an Environment and no key would satisfy the
            // assertion above and be useless.
            var (data, captured) = Build(Key("Paper"));
            await data.InitializeAsync(Substitute.For<IPluginLoaderService>());

            await data.ConfigureStoredKeyProvidersAsync();

            Assert.Equal("k", captured()!["ApiKey"]);
            Assert.Equal("s", captured()!["ApiSecret"]);
        }

        [Fact]
        public async Task A_profile_with_no_Environment_recorded_does_not_crash_the_configure()
        {
            // Profiles predate the field. An empty string is the honest answer — every plugin
            // that reads it compares against a known word and falls to its own default.
            var (data, captured) = Build(Key(null!));
            await data.InitializeAsync(Substitute.For<IPluginLoaderService>());

            await data.ConfigureStoredKeyProvidersAsync();

            Assert.Equal("", captured()!["Environment"]);
        }
    }
}

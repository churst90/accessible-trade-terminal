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
                keys,
                new CredentialInUseRegistry());

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

        // ── The mapping, as the pure function it is ──────────────────────────
        //
        // These used to drive a real DataService through InitializeAsync — six times — which
        // scans for plugins and creates the REAL app-data directory. That is process-wide state,
        // and it raced the bUnit Settings tests into intermittent failure: the full suite lost
        // between one and three of them per run while every one passed in isolation. The
        // end-to-end property (the dictionary really reaches Configure) is worth exactly one
        // test; the vocabulary and the polarity are arithmetic.

        [Theory]
        [InlineData("Paper", "Paper", "true")]
        [InlineData("Live",  "Live",  "false")]
        // Anything that is not explicitly Live is PRACTICE. Legacy profiles predate the field and
        // carry an empty string; under the other polarity every one of them would have been
        // handed to its plugin as a live-money credential.
        [InlineData("",      "Paper", "true")]
        [InlineData(null,    "Paper", "true")]
        [InlineData("sandbox", "Paper", "true")]
        [InlineData("nonsense", "Paper", "true")]
        public void The_environment_is_normalised_and_spelled_every_way_the_fleet_reads_it(
            string? stored, string expectedEnvironment, string expectedTestnet)
        {
            var c = DataService.CredentialFor(Key(stored!));

            // Five plugins compare this against a literal to choose their HOST. An unrecognised
            // value — including the empty string a legacy profile holds — sends most of them live.
            Assert.Equal(expectedEnvironment, c["Environment"]);
            // And Binance reads a different key entirely. Only a unit test had ever supplied it,
            // so a Binance profile marked Paper signed against the LIVE exchange.
            Assert.Equal(expectedTestnet, c["Testnet"]);
        }

        [Fact]
        public void The_credential_itself_is_carried()
        {
            // The control: a dictionary full of environment flags and no key would satisfy every
            // assertion above and be useless.
            var c = DataService.CredentialFor(Key("Paper"));

            Assert.Equal("k", c["ApiKey"]);
            Assert.Equal("s", c["ApiSecret"]);
        }

        // ── And the end-to-end property, ONCE ────────────────────────────────

        [Fact]
        public async Task The_credential_reaches_the_provider_at_all()
        {
            // The original defect was not a wrong value, it was a field that never arrived:
            // neither place that built this dictionary put Environment in it. That is what this
            // one test guards, and it is the only one here that needs a real DataService.
            var (data, captured) = Build(Key("Paper"));
            await data.InitializeAsync(Substitute.For<IPluginLoaderService>());

            await data.ConfigureStoredKeyProvidersAsync();

            var config = captured();
            Assert.NotNull(config);
            Assert.Equal("Paper", config!["Environment"]);
            Assert.Equal("true", config["Testnet"]);
            Assert.Equal("k", config["ApiKey"]);
        }
    }
}

using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>One credential per provider, and everything reads the same one.</b>
    /// See <c>docs/ORDER_ROUTING_SAFETY_SCOPE.md</c> §D1.
    ///
    /// <para>
    /// Three parts of the app used to hold three different opinions about which key an order
    /// used. The HOST came from whichever active key the startup loop reached first and never
    /// changed after; the SIGNATURE came from a lookup blind to both Environment and IsActive;
    /// and the dashboard's switcher changed a flag neither of them read. Host from one profile
    /// and signature from another is how a Paper-labelled key signs a real order.
    /// </para>
    /// </summary>
    public class CredentialInUseTests
    {
        private static ApiKeyConfig Key(string nickname, string environment = "Paper",
                                        string provider = "Gemini", bool withdrawal = false) =>
            new(provider, nickname, "ak-" + nickname, "sk", "", "Spot", environment, IsActive: true,
                AllowsWithdrawal: withdrawal);

        private static (DataService Data, IMarketDataProvider Provider, List<Dictionary<string, string>> Configures, IApiKeyService Keys)
            Build(bool isConfigured, params ApiKeyConfig[] stored)
        {
            var keys = Substitute.For<IApiKeyService>();
            keys.GetAllKeysAsync().Returns(_ => Task.FromResult(stored.ToList()));
            keys.GetKeysForProviderAsync(Arg.Any<string>())
                .Returns(ci => Task.FromResult(stored.Where(k => ProviderNames.Match(k.Provider, ci.Arg<string>())).ToList()));

            var data = new DataService(
                Substitute.For<IPluginLoaderService>(),
                NullLogger<DataService>.Instance,
                Substitute.For<ICacheService>(),
                keys,
                new CredentialInUseRegistry());

            var configures = new List<Dictionary<string, string>>();
            var provider = Substitute.For<IMarketDataProvider>();
            provider.Name.Returns("Gemini");
            provider.IsConfigured.Returns(isConfigured);
            provider.When(x => x.Configure(Arg.Any<Dictionary<string, string>>()))
                    .Do(ci => configures.Add(ci.Arg<Dictionary<string, string>>()));

            data.RegisterProvider(provider);
            return (data, provider, configures, keys);
        }

        // ── The record ────────────────────────────────────────────────────────

        [Fact]
        public async Task Configuring_from_a_stored_key_records_which_key_is_in_use()
        {
            var (data, _, _, _) = Build(isConfigured: false, Key("gem-paper"));
            await data.InitializeAsync(Substitute.For<IPluginLoaderService>());

            await data.ConfigureStoredKeyProvidersAsync();

            Assert.Equal("gem-paper", data.CredentialInUse("Gemini")?.Nickname);
        }

        [Fact]
        public async Task A_provider_nothing_has_configured_has_no_credential_in_use()
        {
            // The honest answer, and the one the chokepoint turns into a refusal on a venue with
            // no practice environment. A guess here would be worse than a null.
            var (data, _, _, _) = Build(isConfigured: false);
            await data.InitializeAsync(Substitute.For<IPluginLoaderService>());

            await data.ConfigureStoredKeyProvidersAsync();

            Assert.Null(data.CredentialInUse("Gemini"));
        }

        [Fact]
        public async Task The_record_is_found_under_a_different_spelling_of_the_provider_name()
        {
            // A store can hold "TwelveData" beside "Twelve Data"; the 2026-08-31 drift was
            // exactly this, one field over.
            var (data, _, _, _) = Build(isConfigured: false, Key("gem-paper", provider: "gemini"));
            await data.InitializeAsync(Substitute.For<IPluginLoaderService>());

            await data.ConfigureStoredKeyProvidersAsync();

            Assert.Equal("gem-paper", data.CredentialInUse("Gemini")?.Nickname);
        }

        // ── Choosing a key ────────────────────────────────────────────────────

        [Fact]
        public async Task Reconfiguring_pushes_the_key_even_when_the_provider_says_it_is_already_configured()
        {
            // THE defect behind "the switcher that does not switch": seven plugins declare
            // IsConfigured => true unconditionally, and the startup loop's skip fired for them
            // every single time. A user CHOOSING a key must not be skipped.
            var (data, _, configures, _) = Build(isConfigured: true, Key("gem-paper"), Key("gem-live", "Live"));
            await data.InitializeAsync(Substitute.For<IPluginLoaderService>());
            await data.ConfigureStoredKeyProvidersAsync();
            Assert.Empty(configures);                       // the skip, still in place for startup

            var chosen = await data.ReconfigureProviderAsync("Gemini", "gem-live");

            Assert.Equal("gem-live", chosen?.Nickname);
            var pushed = Assert.Single(configures);
            Assert.Equal("Live", pushed["Environment"]);
            Assert.Equal("ak-gem-live", pushed["ApiKey"]);
            Assert.Equal("gem-live", data.CredentialInUse("Gemini")?.Nickname);
        }

        [Fact]
        public async Task Reconfiguring_to_an_unknown_nickname_changes_nothing_and_says_so()
        {
            // Returning null rather than silently leaving the old credential in place: the user
            // has just been told the key changed, so the caller has to be able to correct that.
            var (data, _, configures, _) = Build(isConfigured: true, Key("gem-paper"));
            await data.InitializeAsync(Substitute.For<IPluginLoaderService>());

            var chosen = await data.ReconfigureProviderAsync("Gemini", "not-a-profile");

            Assert.Null(chosen);
            Assert.Empty(configures);
        }

        [Fact]
        public async Task Reconfiguring_refuses_a_withdrawal_profile()
        {
            // A withdrawal-enabled key must never become a trading session's credential —
            // the separation is worth nothing if one path hands it over.
            var (data, _, configures, _) = Build(isConfigured: true, Key("gem-withdraw", "Live", withdrawal: true));
            await data.InitializeAsync(Substitute.For<IPluginLoaderService>());

            var chosen = await data.ReconfigureProviderAsync("Gemini", "gem-withdraw");

            Assert.Null(chosen);
            Assert.Empty(configures);
        }

        // ── What signs ────────────────────────────────────────────────────────

        [Fact]
        public async Task The_checkout_hands_out_the_credential_in_use_not_the_first_matching_profile()
        {
            // The whole point. With a paper and a live profile stored, GetKeyForProviderAsync
            // returns whichever was saved first — so the host could come from one profile and the
            // signature from the other.
            var registry = new CredentialInUseRegistry();
            registry.Record("Gemini", Key("gem-live", "Live"));
            var keys = Substitute.For<IApiKeyService>();
            keys.GetKeyForProviderAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(_ => Task.FromResult<ApiKeyConfig?>(Key("gem-paper")));

            var resolved = await ApiKeyCheckoutResolution.ResolveAsync(registry, keys, "Gemini", "Spot");

            Assert.Equal("gem-live", resolved?.Nickname);
            await keys.DidNotReceive().GetKeyForProviderAsync(Arg.Any<string>(), Arg.Any<string>());
        }

        [Fact]
        public async Task The_checkout_falls_back_to_the_lookup_when_nothing_has_been_configured()
        {
            // The lazy first-fetch case: no host has been chosen either, so there is nothing for
            // the signature to disagree with.
            var keys = Substitute.For<IApiKeyService>();
            keys.GetKeyForProviderAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(_ => Task.FromResult<ApiKeyConfig?>(Key("gem-paper")));

            var resolved = await ApiKeyCheckoutResolution.ResolveAsync(
                new CredentialInUseRegistry(), keys, "Gemini", "Spot");

            Assert.Equal("gem-paper", resolved?.Nickname);
        }

        [Fact]
        public async Task The_checkout_never_signs_with_a_withdrawal_profile_from_the_record()
        {
            var registry = new CredentialInUseRegistry();
            registry.Record("Gemini", Key("gem-withdraw", "Live", withdrawal: true));
            var keys = Substitute.For<IApiKeyService>();
            keys.GetKeyForProviderAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(_ => Task.FromResult<ApiKeyConfig?>(Key("gem-paper")));

            var resolved = await ApiKeyCheckoutResolution.ResolveAsync(registry, keys, "Gemini", "Spot");

            Assert.Equal("gem-paper", resolved?.Nickname);
        }
    }
}

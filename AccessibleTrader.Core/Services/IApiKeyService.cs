using System.Collections.Generic;
using System.Threading.Tasks;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// A named credential profile for a single provider. Multiple profiles can exist
    /// for the same provider — e.g. one Paper and one Live key for Alpaca.
    /// </summary>
    public record ApiKeyConfig(
        string Provider,
        string Nickname,
        string ApiKey,
        string ApiSecret,
        string Passphrase = "",
        string MarketType = "Spot",
        /// <summary>"Paper" or "Live". Shown prominently in the API Keys modal.</summary>
        string Environment = "Paper",
        /// <summary>
        /// True when this profile is the one currently used for trading sessions.
        /// Only one profile per provider+environment combination should be active.
        /// </summary>
        bool IsActive = false,

        /// <summary>
        /// True only for a profile deliberately created to move funds OFF the venue.
        ///
        /// <para>
        /// **Default false, and it must stay the exception.** Withdrawal permission
        /// is the most dangerous scope an API key can carry: a trading key that can
        /// also withdraw means one compromise empties the account. Keeping it on a
        /// separate profile costs the user one extra setup step and buys the
        /// difference between "my key leaked" and "my funds are gone".
        /// </para>
        ///
        /// <para>
        /// Nothing on the trading path ever selects a profile with this set, and
        /// the withdrawal path selects ONLY profiles with it set. The two never
        /// reach for the same credential.
        /// </para>
        /// </summary>
        bool AllowsWithdrawal = false
    );

    public interface IApiKeyService
    {
        Task<List<ApiKeyConfig>> GetAllKeysAsync();
        Task<List<ApiKeyConfig>> GetKeysForProviderAsync(string provider);
        Task<ApiKeyConfig?> GetKeyForProviderAsync(string provider, string marketType = "Spot");
        Task<ApiKeyConfig?> GetActiveKeyForProviderAsync(string provider, string environment = "Paper");

        /// <summary>
        /// The profile explicitly marked withdrawal-enabled for this provider, or
        /// null when there is none.
        ///
        /// <para>
        /// The ONLY way the withdrawal path obtains a credential, and it will not
        /// fall back to the trading key. "No withdrawal profile configured" is the
        /// correct and safe answer — a fallback here would silently reunite the two
        /// powers that this separation exists to keep apart.
        /// </para>
        /// </summary>
        Task<ApiKeyConfig?> GetWithdrawalKeyAsync(string provider);
        Task SaveKeyAsync(ApiKeyConfig config);
        Task RemoveKeyAsync(string nickname);
        Task SetActiveKeyAsync(string nickname);
    }
}

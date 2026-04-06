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
        bool IsActive = false
    );

    public interface IApiKeyService
    {
        Task<List<ApiKeyConfig>> GetAllKeysAsync();
        Task<List<ApiKeyConfig>> GetKeysForProviderAsync(string provider);
        Task<ApiKeyConfig?> GetKeyForProviderAsync(string provider, string marketType = "Spot");
        Task<ApiKeyConfig?> GetActiveKeyForProviderAsync(string provider, string environment = "Paper");
        Task SaveKeyAsync(ApiKeyConfig config);
        Task RemoveKeyAsync(string nickname);
        Task SetActiveKeyAsync(string nickname);
    }
}

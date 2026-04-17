using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// Single MAUI <see cref="SecureStorage"/> adapter that serves both the
    /// Core-facing <see cref="ISecureStorageService"/> and the plugin-facing
    /// <see cref="IPluginSecureStorage"/>. One implementation, two interfaces —
    /// plugins don't need a reference to AccessibleTrader.Core, and Core
    /// doesn't need a reference to the SDK's plugin-host bridge.
    /// </summary>
    public class MauiSecureStorageService : ISecureStorageService, IPluginSecureStorage
    {
        public async Task SetAsync(string key, string value)
        {
            await SecureStorage.Default.SetAsync(key, value);
        }

        public async Task<string?> GetAsync(string key)
        {
            return await SecureStorage.Default.GetAsync(key);
        }

        public void Remove(string key)
        {
            SecureStorage.Default.Remove(key);
        }
    }
}

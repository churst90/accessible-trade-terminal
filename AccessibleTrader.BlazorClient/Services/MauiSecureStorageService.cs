using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.BlazorClient.Services
{
    public class MauiSecureStorageService : ISecureStorageService
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

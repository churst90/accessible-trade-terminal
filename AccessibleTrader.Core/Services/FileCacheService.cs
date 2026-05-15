using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface ICacheService
    {
        Task<T?> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    }

    public class FileCacheService : ICacheService
    {
        private readonly string _cacheDir;
        private readonly ILogger<FileCacheService> _logger;

        public FileCacheService(ILogger<FileCacheService> logger, IPlatformPathService pathService)
        {
            _logger = logger;
            
            // Use the provided path service for cross-platform support.
            _cacheDir = Path.Combine(pathService.CacheDirectory, "DataCache");
            
            try
            {
                if (!Directory.Exists(_cacheDir)) Directory.CreateDirectory(_cacheDir);
            }
            catch (Exception ex)
            {
                // Fallback to temp if local app data fails
                _cacheDir = Path.Combine(Path.GetTempPath(), "AccessibleTraderCache");
                if (!Directory.Exists(_cacheDir)) Directory.CreateDirectory(_cacheDir);
                _logger.LogWarning(ex, "Could not create primary cache dir, falling back to temp.");
            }
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var path = GetPath(key);
            if (!File.Exists(path)) return default;

            try
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var entry = JsonSerializer.Deserialize<CacheEntry<T>>(json);
                
                if (entry != null && entry.Expiration > DateTime.UtcNow)
                {
                    return entry.Value;
                }
                
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read cache key {Key}.", key);
            }
            return default;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            try
            {
                var entry = new CacheEntry<T>
                {
                    Value = value,
                    Expiration = DateTime.UtcNow.Add(expiration ?? TimeSpan.FromDays(1))
                };
                
                var json = JsonSerializer.Serialize(entry);
                await AtomicFile.WriteAllTextAsync(GetPath(key), json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write cache key {Key}.", key);
            }
        }

        private string GetPath(string key)
        {
            // Use a hash-based filename to avoid collisions when different keys
            // sanitize to the same string (e.g. "key:1" and "key/1" → "key_1").
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(key)))[..16];
            return Path.Combine(_cacheDir, $"{hash}.json");
        }

        private class CacheEntry<T>
        {
            public T? Value { get; set; }
            public DateTime Expiration { get; set; }
        }
    }
}

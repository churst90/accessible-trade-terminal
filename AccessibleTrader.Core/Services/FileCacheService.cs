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
                _logger.LogWarning($"Could not create primary cache dir, falling back to temp: {ex.Message}");
            }
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var path = GetPath(key);
            if (!File.Exists(path)) return default;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var entry = JsonSerializer.Deserialize<CacheEntry<T>>(json);
                
                if (entry != null && entry.Expiration > DateTime.UtcNow)
                {
                    return entry.Value;
                }
                
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to read cache key {key}: {ex.Message}");
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
                await File.WriteAllTextAsync(GetPath(key), json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to write cache key {key}: {ex.Message}");
            }
        }

        private string GetPath(string key)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeKey = new string(key.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            return Path.Combine(_cacheDir, $"{safeKey}.json");
        }

        private class CacheEntry<T>
        {
            public T? Value { get; set; }
            public DateTime Expiration { get; set; }
        }
    }
}

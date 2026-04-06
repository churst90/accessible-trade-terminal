using Microsoft.Maui.Storage;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.BlazorClient.Services
{
    public class MauiPathService : IPlatformPathService
    {
        public string AppDataDirectory => FileSystem.AppDataDirectory;
        public string CacheDirectory => FileSystem.CacheDirectory;
    }
}

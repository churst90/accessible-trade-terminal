namespace AccessibleTrader.Core.Services
{
    public interface IPlatformPathService
    {
        string AppDataDirectory { get; }
        string CacheDirectory { get; }
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public record CustomScript(string Id, string Name, string Code, bool IsEnabled);

    public interface ICustomScriptService
    {
        IReadOnlyList<CustomScript> GetScripts();
        void AddScript(CustomScript script);
        void RemoveScript(string id);
        void UpdateScript(CustomScript script);
        Task<string> RunScriptAsync(string id, IReadOnlyList<Ohlcv> data);
        void SaveScripts();
    }
}

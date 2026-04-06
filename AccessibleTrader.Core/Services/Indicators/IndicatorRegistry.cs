using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    public interface IIndicatorRegistry
    {
        List<IndicatorMetadata> GetAvailableIndicators();
        int GetStabilityWindow(string code, Dictionary<string, object> parameters);
    }

    public class IndicatorRegistry : IIndicatorRegistry
    {
        private readonly IIndicatorService _indicatorService;

        public IndicatorRegistry(IIndicatorService indicatorService)
        {
            _indicatorService = indicatorService;
        }

        public List<IndicatorMetadata> GetAvailableIndicators() => _indicatorService.GetAvailableIndicators();

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 
            _indicatorService.GetStabilityWindow(code, parameters);
    }
}

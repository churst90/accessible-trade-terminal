using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces;

public interface IComponentRoleMapper
{
    ComponentRole GetComponentRole(string indicatorCode, string componentName);
    ColorSource GetColorSource(string indicatorCode, string componentName);
    ComponentDisplayType GetDisplayType(string indicatorCode, string componentName);
}

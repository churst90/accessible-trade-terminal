using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces;

public interface ISonificationProfileProvider
{
    SonificationProfile GetProfile(ComponentDisplayType displayType, ComponentRole role, string componentName);
}

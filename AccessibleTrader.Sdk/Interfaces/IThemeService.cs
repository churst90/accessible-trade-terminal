using System;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Theming;

namespace AccessibleTrader.Sdk.Interfaces;

public interface IThemeService
{
    ChartTheme Current { get; }
    void SetTheme(ThemeType theme);
    event EventHandler<ChartTheme>? ThemeChanged;
}

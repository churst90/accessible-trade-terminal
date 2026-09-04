using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Theming;

namespace AccessibleTrader.Core.Services.Theming
{
    /// <summary>
    /// A built-in theme exactly as authored, with none of the user's appearance preferences on top.
    ///
    /// <para>
    /// The theme editor needs this and the running application does not. Everywhere else, what
    /// matters is the theme as the user will actually see it — their up/down colours, their
    /// background override, their unified gradient. But an EDITOR showing that would present the
    /// user's own preferences as if they were the theme's, and then save them into the theme,
    /// baking a preference into a thing meant to be shared.
    /// </para>
    ///
    /// <para>
    /// Results are cached because a theme is a pure function of its type, and the editor asks for
    /// the base on every keystroke to recompute its preview.
    /// </para>
    /// </summary>
    public static class BaseThemeResolver
    {
        private static readonly Dictionary<ThemeType, ChartTheme> Cache = new();
        private static readonly object Gate = new();

        /// <summary>The named built-in, undecorated.</summary>
        public static ChartTheme Resolve(ThemeType type)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(type, out var cached)) return cached;

                var service = new ThemeService(new NullSettings());
                service.SetTheme(type);
                Cache[type] = service.Current;
                return service.Current;
            }
        }

        /// <summary>
        /// A settings manager that remembers nothing, so <see cref="ThemeService"/> finds no
        /// preferences to layer and no file to write.
        /// </summary>
        private sealed class NullSettings : ISettingsManager
        {
            public Newtonsoft.Json.Linq.JToken? GetSetting(string keyPath, Newtonsoft.Json.Linq.JToken? defaultValue = null)
                => defaultValue;
            public void SetSetting(string keyPath, Newtonsoft.Json.Linq.JToken value) { }
            public Newtonsoft.Json.Linq.JObject GetEffectiveSettingsForSeries(string seriesId) => new();
            public void SaveSettings() { }
            public void ResetToDefaults() { }   // nothing is remembered, so nothing to forget
        }
    }
}

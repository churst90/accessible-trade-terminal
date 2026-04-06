using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services
{
    public interface IConfigService
    {
        T GetConfig<T>(string section) where T : new();
        void SaveConfig<T>(string section, T config);
        JObject FullConfig { get; }
    }

    public class ConfigService : IConfigService
    {
        private readonly string _configPath;
        private JObject _config;

        public JObject FullConfig => _config;

        public ConfigService(string filename = "appsettings.json")
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
            _config = Load();
        }

        private JObject Load()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    return JObject.Parse(json);
                }
                catch { }
            }
            return new JObject();
        }

        public T GetConfig<T>(string section) where T : new()
        {
            var token = _config[section];
            if (token == null) return new T();
            return token.ToObject<T>() ?? new T();
        }

        public void SaveConfig<T>(string section, T config)
        {
            _config[section] = JToken.FromObject(config!);
            File.WriteAllText(_configPath, _config.ToString(Formatting.Indented));
        }
    }
}

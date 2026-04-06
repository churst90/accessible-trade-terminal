using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Configuration
{
    public static class SeriesSchemas
    {
        // High Contrast Color Constants
        public const string ColorPinkEma = "#FF69B4";
        public const string ColorOrangeSma = "#FFA500";
        public const string ColorBlueRsi = "#0096FF";
        public const string ColorWhite = "#FFFFFF";
        public const string ColorGreenBody = "#00FF00";
        public const string ColorRedBody = "#FF0000";

        // Static Schemas for Core Use
        public static JObject CandlestickSchema => GetDefaultCandleSchema();
        public static JObject VolumeSchema => JObject.Parse(@"{ ""name"": ""Volume"", ""type"": ""volume"" }");
        public static JObject MacdSchema => JObject.Parse(@"{ ""name"": ""MACD"", ""type"": ""oscillator"" }");
        public static JObject LineSchema => JObject.Parse(@"{ ""name"": ""Line"", ""type"": ""line"" }");

        public static JObject GetDefaultCandleSchema() => JObject.Parse($@"
        {{
            ""name"": ""Price"",
            ""type"": ""candle"",
            ""general"": {{ ""show_wicks"": true }},
            ""speech"": {{ ""template"": ""Open {{Open}}, High {{High}}, Low {{Low}}, Close {{Close}}"" }},
            ""components"": [
                {{
                    ""name"": ""Body"",
                    ""appearance"": {{ ""bull_color"": ""{ColorGreenBody}"", ""bear_color"": ""{ColorRedBody}"" }},
                    ""sonification"": {{ ""enabled"": true, ""instrument"": ""sine"" }}
                }},
                {{
                    ""name"": ""Wick"",
                    ""appearance"": {{ ""color"": ""{ColorWhite}"" }},
                    ""sonification"": {{ ""enabled"": true, ""instrument"": ""sine"" }}
                }}
            ]
        }}");

        public static JObject GetDefaultEmaSchema(int period) => JObject.Parse($@"
        {{
            ""name"": ""EMA {period}"",
            ""type"": ""line"",
            ""general"": {{ ""period"": {period} }},
            ""speech"": {{ ""template"": ""EMA {period}: {{value}}"" }},
            ""appearance"": {{ ""color"": ""{ColorPinkEma}"", ""width"": 2 }},
            ""sonification"": {{ ""enabled"": true, ""instrument"": ""saw"" }}
        }}");

        public static JObject GetDefaultSmaSchema(int period) => JObject.Parse($@"
        {{
            ""name"": ""SMA {period}"",
            ""type"": ""line"",
            ""general"": {{ ""period"": {period} }},
            ""speech"": {{ ""template"": ""SMA {period}: {{value}}"" }},
            ""appearance"": {{ ""color"": ""{ColorOrangeSma}"", ""width"": 2 }},
            ""sonification"": {{ ""enabled"": true, ""instrument"": ""triangle"" }}
        }}");

        public static JObject GetDefaultRsiSchema() => JObject.Parse($@"
        {{
            ""name"": ""RSI"",
            ""type"": ""oscillator"",
            ""general"": {{ ""period"": 14 }},
            ""speech"": {{ ""template"": ""RSI: {{value}}"" }},
            ""appearance"": {{ ""color"": ""{ColorBlueRsi}"", ""width"": 1.5 }},
            ""sonification"": {{ ""enabled"": true, ""instrument"": ""sine"" }}
        }}");
    }
}

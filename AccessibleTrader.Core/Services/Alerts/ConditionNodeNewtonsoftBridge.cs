using AccessibleTrader.Sdk.Strategies;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// Newtonsoft ↔ System.Text.Json bridge for <see cref="ConditionNode"/>.
    ///
    /// The tree's polymorphism ($kind discriminator for leaf/group) is declared with
    /// System.Text.Json attributes and is how strategy specs persist. Alerts, however,
    /// persist through Newtonsoft (alerts.json), which ignores those attributes and
    /// cannot instantiate the abstract base. Rather than duplicating the polymorphism
    /// rules in a second serializer (drift waiting to happen), this converter delegates
    /// the node subtree to System.Text.Json in both directions, embedding its output
    /// verbatim inside the Newtonsoft document.
    /// </summary>
    public sealed class ConditionNodeNewtonsoftBridge : JsonConverter<ConditionNode?>
    {
        public override void WriteJson(JsonWriter writer, ConditionNode? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }
            string json = System.Text.Json.JsonSerializer.Serialize(value);
            writer.WriteRawValue(json);
        }

        public override ConditionNode? ReadJson(JsonReader reader, Type objectType, ConditionNode? existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var token = JToken.ReadFrom(reader);
            return System.Text.Json.JsonSerializer.Deserialize<ConditionNode>(token.ToString(Formatting.None));
        }
    }
}

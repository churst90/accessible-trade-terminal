using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// Builds a <see cref="WebhookAlertChannelConfig"/> from settings, reading the named
    /// list at <c>alerts.webhooks</c> (a JSON array of <c>{ name, url, authHeader? }</c>).
    ///
    /// Back-compat: when <c>alerts.webhooks</c> is absent/empty but the legacy single
    /// <c>alerts.webhook.url</c> is present, it is migrated into a single
    /// <c>{ Name = "Default", Url = … }</c> entry so an existing install keeps working
    /// (the user re-assigns per-alert <c>WebhookTarget</c> to route per-asset).
    /// </summary>
    public static class WebhookAlertConfigLoader
    {
        /// <summary>Name given to a migrated legacy single-webhook URL.</summary>
        public const string LegacyDefaultName = "Default";

        public static WebhookAlertChannelConfig? Load(ISettingsManager settings)
        {
            var token     = settings.GetSetting(SettingsKeys.Webhooks);
            var legacyUrl = settings.GetSetting(SettingsKeys.LegacyWebhookUrl)?.ToString();
            var legacyAuth = settings.GetSetting(SettingsKeys.LegacyWebhookAuth)?.ToString();

            var webhooks = ParseWebhooks(token, legacyUrl, legacyAuth);
            if (webhooks.Count == 0) return null;
            return new WebhookAlertChannelConfig { Webhooks = webhooks };
        }

        /// <summary>
        /// Parse the <c>alerts.webhooks</c> array, falling back to migrating a legacy
        /// single URL. Exposed for direct testing of the migration path.
        /// </summary>
        public static IReadOnlyList<NamedWebhook> ParseWebhooks(
            JToken? webhooksToken, string? legacyUrl, string? legacyAuth)
        {
            var list = new List<NamedWebhook>();

            if (webhooksToken is JArray arr)
            {
                foreach (var item in arr)
                {
                    var name = item.Value<string>("name") ?? item.Value<string>("Name");
                    var url  = item.Value<string>("url")  ?? item.Value<string>("Url");
                    var auth = item.Value<string>("authHeader") ?? item.Value<string>("AuthHeader");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
                    list.Add(new NamedWebhook { Name = name!, Url = url!, AuthHeader = auth });
                }
            }

            // Migrate the legacy single webhook if the named list produced nothing.
            if (list.Count == 0 && !string.IsNullOrWhiteSpace(legacyUrl))
            {
                list.Add(new NamedWebhook
                {
                    Name = LegacyDefaultName,
                    Url = legacyUrl!,
                    AuthHeader = string.IsNullOrWhiteSpace(legacyAuth) ? null : legacyAuth,
                });
            }

            return list;
        }

        /// <summary>Serialize a webhook list back to the JSON array persisted at <c>alerts.webhooks</c>.</summary>
        public static JArray ToJson(IEnumerable<NamedWebhook> webhooks)
        {
            var arr = new JArray();
            foreach (var w in webhooks)
            {
                if (string.IsNullOrWhiteSpace(w.Name) || string.IsNullOrWhiteSpace(w.Url)) continue;
                var o = new JObject
                {
                    ["name"] = w.Name,
                    ["url"]  = w.Url,
                };
                if (!string.IsNullOrWhiteSpace(w.AuthHeader)) o["authHeader"] = w.AuthHeader;
                arr.Add(o);
            }
            return arr;
        }
    }
}

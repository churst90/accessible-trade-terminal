using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Notifications
{
    /// <summary>
    /// <b>Which channel a terminal event takes, in one place.</b>
    ///
    /// <para>
    /// The rule (Cody, 2026-09-11) is about the event's SUBJECT, not about the process:
    /// </para>
    /// <list type="bullet">
    ///   <item>Whatever is happening on <b>the chart in front of the trader</b> is spoken in
    ///   the browser's live region and earconed. It is never toasted — the user is already
    ///   there, and a toast for something you are being told anyway is noise.</item>
    ///   <item><b>Everything else</b> — a bar closing on another open tab, an alert or a fill on
    ///   a symbol with no tab open, and every terminal event while the browser is closed —
    ///   arrives as a system notification, because that is the only way it can reach the user.
    ///   "Minimised" still counts as looking at the focused chart: the live region is live.</item>
    /// </list>
    ///
    /// <para>
    /// Before this existed the decision was spread across three default-off switches in a
    /// different dialog from the feature they gated, and the focused chart's own bar close
    /// raised a toast on every minute — which is the report that started this pass.
    /// </para>
    /// </summary>
    public static class NotificationPolicy
    {
        /// <summary>
        /// The default for <see cref="SettingsKeys.NotifyUnseenEvents"/>: <b>ON</b>.
        /// Cody, 2026-09-11. It lives here rather than in <c>SettingsKeys</c> because that class
        /// is read reflectively as a set of string literals.
        /// </summary>
        public const bool NotifyUnseenByDefault = true;

        /// <summary>
        /// Whether the user wants notifications for the things they cannot see.
        /// Defaults to <c>true</c>; see <see cref="SettingsKeys.NotifyUnseenEvents"/>.
        ///
        /// <para>The three retired switches are consulted ONLY when the new key has never been
        /// written, and only so that someone who had deliberately turned all three off is not
        /// switched back on by the new default. A partial legacy state ("alerts on, bar closes
        /// off") reads as ON, because the user had asked to be notified about something.</para>
        /// </summary>
        public static bool NotifyUnseen(ISettingsManager? settings)
        {
            if (settings == null) return NotifyUnseenByDefault;

            bool? chosen = ReadBool(settings, SettingsKeys.NotifyUnseenEvents);
            if (chosen.HasValue) return chosen.Value;

            bool? alerts = ReadBool(settings, SettingsKeys.LegacyDesktopNotifyAlerts);
            bool? bars   = ReadBool(settings, SettingsKeys.LegacyDesktopNotifyNewBars);
            bool? fills  = ReadBool(settings, SettingsKeys.LegacyDesktopNotifyOrderFills);
            if (alerts is null && bars is null && fills is null)
                return NotifyUnseenByDefault;

            return (alerts ?? false) || (bars ?? false) || (fills ?? false);
        }

        /// <summary>
        /// Is this event about the chart the trader is looking at? If so the browser is already
        /// saying it and nothing may toast.
        ///
        /// <para>Compared on the symbol alone, deliberately. A fill and a chart agree on the
        /// symbol and need not agree on the timeframe or the venue — an order filled on the
        /// exchange whose chart is on screen is news the user is watching for, whichever
        /// timeframe that chart happens to be drawn at.</para>
        /// </summary>
        public static bool IsOnScreen(WorkspaceState? state, string? symbol)
        {
            if (state == null || string.IsNullOrWhiteSpace(symbol)) return false;

            return Same(symbol, state.SymbolDisplayName) || Same(symbol, state.Identity.Symbol);
        }

        private static bool Same(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
            && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool? ReadBool(ISettingsManager settings, string key)
        {
            try { return settings.GetSetting(key)?.ToObject<bool>(); }
            catch { return null; }
        }
    }
}

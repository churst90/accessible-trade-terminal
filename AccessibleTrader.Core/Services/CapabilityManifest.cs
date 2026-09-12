using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// <b>Every capability this build has, enumerated from the policy that decides them.</b>
    ///
    /// <para>
    /// Two problems recorded in <c>patches/HOSTED-DEPLOY-NOTES.md</c> have the same shape and the
    /// same answer.
    /// </para>
    ///
    /// <para>
    /// <b>The public sentences track the box, not the tag.</b> The website's feature page has
    /// drifted five separate times — split view, the default theme, "two venues", missing
    /// narration, a Split button in a screenshot — and the 09-12 gate made "Close the browser and
    /// your alerts keep running" false on the hosted head the moment it shipped. The next false
    /// sentence is already queued: "three switches, each off until you turn it on" stops being
    /// true at the next tag. The doc-drift checker has never seen that site, and it could not
    /// help if it had: the truth lives in <see cref="DemoPolicy"/>'s booleans, not in prose.
    /// </para>
    ///
    /// <para>
    /// <b>A gate lands with no runtime signal.</b> When <c>HostedAlertMonitor</c> stopped being
    /// constructed, the only proof was grepping a journal for a line that was no longer there —
    /// an absence, which is the weakest evidence there is and exactly the shape of check the
    /// "assert the artifact, not the incantation" rule exists to refuse.
    /// </para>
    ///
    /// <para>
    /// So: <see cref="DemoPolicy"/> already IS the manifest, and this reads it. One startup line
    /// per head naming the mode and every flag, and the same content as JSON for whoever needs to
    /// check a sentence against a tag. It is reflected rather than listed, so a capability added
    /// to the policy tomorrow is in the manifest tomorrow — a hand-written list would be a second
    /// place every flag has to be added, which is the defect this repo keeps finding.
    /// </para>
    /// </summary>
    public static class CapabilityManifest
    {
        /// <summary>
        /// Every parameterless public boolean on the policy, by name, in declaration order.
        /// Reflected, not listed: the point is that nothing can be added to the policy and left
        /// out of the manifest.
        /// </summary>
        public static IReadOnlyDictionary<string, bool> For(DemoPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);

            var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var prop in typeof(DemoPolicy)
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0)
                         .OrderBy(p => p.MetadataToken))
            {
                flags[prop.Name] = (bool)prop.GetValue(policy)!;
            }
            return flags;
        }

        /// <summary>
        /// The startup line. One line, every flag, so "is background alerting on in this
        /// process" is answered by something PRESENT in the journal rather than by the absence
        /// of something else.
        /// </summary>
        public static string StartupLine(DemoPolicy policy)
        {
            var flags = For(policy);
            var sb = new StringBuilder();
            sb.Append("Capabilities: mode=").Append(policy.Mode);
            foreach (var (name, value) in flags)
                sb.Append(' ').Append(name).Append('=').Append(value ? "on" : "off");
            return sb.ToString();
        }

        /// <summary>
        /// The manifest as JSON, for a check that runs outside the process — comparing what a
        /// release claims in public against what the tag actually allows.
        /// </summary>
        public static string ToJson(DemoPolicy policy, string version)
            => JsonSerializer.Serialize(new
            {
                version,
                mode = policy.Mode.ToString(),
                capabilities = For(policy),
            }, new JsonSerializerOptions { WriteIndented = true });
    }
}

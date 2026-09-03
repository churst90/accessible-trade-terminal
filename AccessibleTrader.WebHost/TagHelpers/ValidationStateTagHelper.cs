using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.ComponentModel.DataAnnotations;

namespace AccessibleTrader.WebHost.TagHelpers
{
    /// <summary>
    /// Puts a field's validation state where a screen reader can read it, on every
    /// <c>asp-for</c> control on the account pages.
    ///
    /// <para>
    /// The 2026-09-01 accessibility audit's finding 3.13 measured this and found nothing
    /// at all: zero <c>aria-invalid</c>, zero <c>aria-required</c>, zero <c>required=</c>
    /// across both projects. Every auth model carries <c>[Required]</c>, but there is no
    /// unobtrusive-validation script on any of the nine pages, so the requirement reached
    /// the server and stopped there — and when a field WAS rejected, its accessible state
    /// stayed "valid". A user moving back through a failed sign-in heard exactly what they
    /// heard before the error. Demonstrated on 2026-09-02: a POST with an empty email
    /// re-renders with the message text present in the DOM and no <c>aria-invalid</c>
    /// anywhere in the document.
    /// </para>
    ///
    /// <para>
    /// A tag helper rather than nine pages of hand-written attributes, because the failure
    /// mode being fixed is "somebody forgot": a tenth page, or a tenth field, gets this by
    /// existing. <see cref="AuthPageErrorStateTests"/> holds it to that by sweeping the
    /// rendered HTML of every page it can reach rather than the source of any one of them.
    /// </para>
    ///
    /// <para>
    /// It does three things, and the third is the one that carries the most weight.
    /// </para>
    /// </summary>
    [HtmlTargetElement("input", Attributes = ForAttributeName)]
    [HtmlTargetElement("select", Attributes = ForAttributeName)]
    [HtmlTargetElement("textarea", Attributes = ForAttributeName)]
    public class ValidationStateTagHelper : TagHelper
    {
        private const string ForAttributeName = "asp-for";

        /// <summary>
        /// Runs AFTER the built-in InputTagHelper (Order 0), which is what writes the
        /// <c>type</c> attribute from the model metadata. Reading <c>type</c> before that
        /// has run sees whatever the author typed and nothing else, so the hidden-field
        /// skip below would miss every field whose type is inferred.
        /// </summary>
        public override int Order => 100;

        [HtmlAttributeName(ForAttributeName)]
        public ModelExpression For { get; set; } = default!;

        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = default!;

        /// <summary>
        /// Per-request flag: has something already claimed the page's initial focus?
        /// Shared with <see cref="AuthErrorBoxTagHelper"/>, which renders above the form
        /// and therefore gets first refusal.
        /// </summary>
        internal const string FocusClaimedKey = "att.auth.autofocus-claimed";

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            var modelState = ViewContext.ViewData.ModelState;

            bool hidden = string.Equals(
                output.Attributes["type"]?.Value?.ToString(), "hidden", StringComparison.OrdinalIgnoreCase);

            if (hidden)
            {
                // A hidden field with a stale autofocus would take focus to nowhere.
                output.Attributes.RemoveAll("autofocus");
                return;
            }

            // There WAS a second skip here, for controls out of the tab order — meaning
            // Register's honeypot, an asp-for input inside an aria-hidden wrapper. It was
            // deleted on 2026-09-02 because sabotaging it changed nothing: the honeypot
            // is a nullable string, so the required rule below already declines it, and
            // it carries no validation attributes, so it can never be the rejected field
            // that takes focus. Two guards removing the same thing hide each other (the
            // cap-and-suppression shape from the narrator work), and the one that could
            // not be observed was this one. Without it AuthPageErrorStateTests'
            // honeypot case becomes a guard that can actually fail: make Website a
            // non-nullable string and it goes red.

            // ── 1. Required ──────────────────────────────────────────────────────
            if (IsRequired(For.Metadata))
                output.Attributes.SetAttribute("aria-required", "true");

            // ── 2. Invalid ───────────────────────────────────────────────────────
            bool fieldRejected =
                modelState.TryGetValue(For.Name, out var entry) && entry.Errors.Count > 0;
            if (fieldRejected)
                output.Attributes.SetAttribute("aria-invalid", "true");

            // ── 3. Where the page opens ──────────────────────────────────────────
            // role="alert" on content that is present at PARSE time does not fire in NVDA
            // or VoiceOver, so the error message on a re-rendered page is announced by
            // nothing. The reliable channel on a full page load is where focus lands, and
            // an unconditional autofocus on the first field — which is what these pages
            // had — puts the user in Email no matter which field failed. So: the first
            // REJECTED field takes focus, and any autofocus the author wrote elsewhere is
            // removed so the browser cannot pick it up instead (it honours the first in
            // document order, which on a failed password is the wrong one).
            var items = ViewContext.HttpContext.Items;
            bool claimed = items[FocusClaimedKey] is true;

            if (claimed)
            {
                output.Attributes.RemoveAll("autofocus");
                return;
            }

            if (!modelState.IsValid)
            {
                if (fieldRejected)
                {
                    output.Attributes.SetAttribute("autofocus", "autofocus");
                    items[FocusClaimedKey] = true;
                }
                else
                {
                    output.Attributes.RemoveAll("autofocus");
                }
                return;
            }

            // Clean page: leave the author's autofocus exactly where they put it, and
            // record that it is taken so nothing downstream adds a second one.
            if (output.Attributes.ContainsName("autofocus"))
                items[FocusClaimedKey] = true;
        }

        /// <summary>
        /// Whether the field must be filled in, judged the way the server judges it.
        ///
        /// <para>
        /// Restricted to strings on purpose. <c>ModelMetadata.IsRequired</c> is true for
        /// every non-nullable value type, so asking it directly marks "Keep me signed in"
        /// — a plain <c>bool</c> — as a required checkbox, which is both false and
        /// impossible to satisfy. Every field on these forms that a user types into is a
        /// string, and a non-nullable string under this project's nullable context is one
        /// the binder will not accept empty. That also catches
        /// <c>Register.ConfirmPassword</c>, which carries no <c>[Required]</c> of its own
        /// but is rejected by <c>[Compare]</c> when left blank — so looking only for a
        /// RequiredAttribute would have called the confirm field optional.
        /// </para>
        /// </summary>
        internal static bool IsRequired(Microsoft.AspNetCore.Mvc.ModelBinding.ModelMetadata metadata)
        {
            if (metadata.ModelType != typeof(string)) return false;
            if (metadata.ValidatorMetadata.OfType<RequiredAttribute>().Any()) return true;
            return metadata.IsRequired;
        }
    }
}

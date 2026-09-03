using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace AccessibleTrader.WebHost.TagHelpers
{
    /// <summary>
    /// Makes the page-level error box the place a failed page opens.
    ///
    /// <para>
    /// These pages have two kinds of failure and they are not the same shape. A rejected
    /// FIELD is handled by <see cref="ValidationStateTagHelper"/>, which sends focus to
    /// the field that failed. A page-level failure — "Email or password is incorrect.",
    /// a duplicate registration, a two-factor code that did not match — belongs to no
    /// field, and until 2026-09-02 was announced by nothing: <c>role="alert"</c> on
    /// content that is already in the DOM when the page is parsed does not fire in NVDA
    /// or VoiceOver, and the unconditional <c>autofocus</c> on the first input dropped
    /// the user into Email with the message silently above them.
    /// </para>
    ///
    /// <para>
    /// So the box takes focus itself. <c>tabindex="-1"</c> makes it focusable without
    /// putting it in the tab order, and <c>autofocus</c> lands there on load, which every
    /// screen reader announces because it is reading the focused element rather than
    /// waiting for a live region to fire.
    /// </para>
    ///
    /// <para>
    /// PRECEDENCE, and it is explicit rather than left to document order. A rejected FIELD
    /// wins: it is the more specific answer, and it is the one the user can act on.
    /// <c>EnableAuthenticator</c> is the page that forced the question —
    /// <c>bool valid = ModelState.IsValid &amp;&amp; await VerifyTwoFactorTokenAsync(...)</c>
    /// sets a page-level Error on a ModelState failure too, so a BLANK code produced both
    /// kinds of failure at once and, under first-in-document-order, opened the page on a
    /// sentence about codes rotating every 30 seconds — which is not what went wrong.
    /// So this claims the focus only when ModelState is clean, i.e. when no field owns the
    /// failure.
    /// </para>
    ///
    /// <para>
    /// The same element also carries the page's success notes — "Your password has been
    /// updated" — which were the unfixed half of exactly this defect: a parse-time
    /// <c>role="status"</c> that never fires, with the unconditional autofocus on the first
    /// field jumping the reader straight past it.
    /// </para>
    ///
    /// <para>
    /// A note and an error cannot both render today: Login sets <c>PasswordReset</c> only
    /// in OnGet, and Security sets <c>Status</c> and <c>Error</c> in exclusive branches. An
    /// <c>Announce="false"</c> parameter was written to let a note stand aside for an error
    /// and then deleted, because nothing could reach it — a guard that cannot be observed is
    /// worse than none, since it reads as protection. Instead the precedence is structural:
    /// the error box is placed ABOVE the note in the two pages that carry both, and the
    /// first claim wins. If a page ever renders the pair, the error takes the focus by
    /// construction rather than by a flag someone has to remember.
    /// </para>
    /// </summary>
    [HtmlTargetElement("div", Attributes = MarkerAttributeName)]
    public class AuthErrorBoxTagHelper : TagHelper
    {
        private const string MarkerAttributeName = "auth-announce";

        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = default!;

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            output.Attributes.RemoveAll(MarkerAttributeName);

            // Focusable either way, so a user who arrows back up can land on it deliberately.
            output.Attributes.SetAttribute("tabindex", "-1");

            var items = ViewContext.HttpContext.Items;
            if (items[ValidationStateTagHelper.FocusClaimedKey] is true) return;
            if (!ViewContext.ViewData.ModelState.IsValid) return;   // a field owns this failure

            output.Attributes.SetAttribute("autofocus", "autofocus");
            items[ValidationStateTagHelper.FocusClaimedKey] = true;
        }
    }
}

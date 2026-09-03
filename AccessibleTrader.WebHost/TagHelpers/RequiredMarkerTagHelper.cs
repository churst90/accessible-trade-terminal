using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace AccessibleTrader.WebHost.TagHelpers
{
    /// <summary>
    /// Adds the visible half of "this field is required".
    ///
    /// <para>
    /// <see cref="ValidationStateTagHelper"/> supplies <c>aria-required</c>, which is the
    /// half a screen reader reads. The audit's finding 3.13 is careful to say the
    /// information was conveyed by nothing at all — "visually or programmatically" —
    /// because there was no asterisk convention and no "fields marked … are required"
    /// note either. A sighted user, including the low-vision half of this product's
    /// audience, learned which fields were mandatory by submitting and failing.
    /// </para>
    ///
    /// <para>
    /// The marker is <c>aria-hidden</c>: a screen reader already hears "required" from the
    /// state, and a label that also read "Email star" would say it twice. The legend that
    /// explains what the star means lives in the form itself, next to the submit button.
    /// </para>
    /// </summary>
    [HtmlTargetElement("label", Attributes = "asp-for")]
    public class RequiredMarkerTagHelper : TagHelper
    {
        /// <summary>After the built-in LabelTagHelper, whose content this appends to.</summary>
        public override int Order => 100;

        [HtmlAttributeName("asp-for")]
        public ModelExpression For { get; set; } = default!;

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            if (!ValidationStateTagHelper.IsRequired(For.Metadata)) return;

            // The label's body is whatever the page wrote between the tags; when it wrote
            // nothing the LabelTagHelper fills in the display name. Either way it is
            // already in the output by the time this runs, so append rather than set.
            var content = output.Content.IsModified
                ? output.Content
                : await output.GetChildContentAsync();
            output.Content.SetHtmlContent(
                content.GetContent() + "<span class=\"req\" aria-hidden=\"true\">*</span>");
        }
    }
}

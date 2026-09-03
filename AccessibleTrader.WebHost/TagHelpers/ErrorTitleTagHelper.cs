using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace AccessibleTrader.WebHost.TagHelpers
{
    /// <summary>
    /// Prefixes the document title with "Error: " when the page came back rejected.
    ///
    /// <para>
    /// Focus placement (see <see cref="AuthErrorBoxTagHelper"/> and
    /// <see cref="ValidationStateTagHelper"/>) is the detailed channel, but it is not the
    /// certain one: <c>autofocus</c> fires at parse time, before a screen reader has
    /// settled the virtual buffer for the new document, and the accessible DESCRIPTION is
    /// the part most often lost to that race. The document title is not — every screen
    /// reader announces it unconditionally on a full page load, and a full page load is
    /// what these no-JavaScript forms do on every failure.
    /// </para>
    ///
    /// <para>
    /// So "Error: Sign in — Accessible Trade Terminal" is the one thing the user is
    /// guaranteed to hear, before anything else, and it is what tells them the submission
    /// did not go through at all. The prefix is spelled out rather than a symbol so it
    /// survives punctuation verbosity settings.
    /// </para>
    ///
    /// <para>
    /// Field rejections are read off ModelState here, so this targets EVERY title rather
    /// than only the ones that opt in: a tenth page gets the prefix by existing. The
    /// optional <c>auth-error</c> attribute adds the page-level failures that live outside
    /// ModelState — a wrong password is not a field-validation error, and must not be,
    /// because saying WHICH half was wrong is an enumeration oracle.
    /// </para>
    /// </summary>
    [HtmlTargetElement("title")]
    public class ErrorTitleTagHelper : TagHelper
    {
        private const string ErrorAttributeName = "auth-error";

        [HtmlAttributeName(ErrorAttributeName)]
        public bool PageLevelError { get; set; }

        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = default!;

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            output.Attributes.RemoveAll(ErrorAttributeName);
            if (!PageLevelError && ViewContext.ViewData.ModelState.IsValid) return;

            var content = await output.GetChildContentAsync();
            output.Content.SetHtmlContent("Error: " + content.GetContent());
        }
    }
}

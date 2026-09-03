// The precedence rule, tested where it can actually be observed.
//
// AuthErrorBoxTagHelper claims the page's opening focus only when ModelState is clean —
// a rejected FIELD is the more specific answer and the one the user can act on. Driving
// that through a real page does not test it: after the EnableAuthenticator fix of
// 2026-09-02, every account page returns Page() on a ModelState failure BEFORE setting a
// page-level Error, so no page renders an error box while ModelState is invalid, and
// sabotaging the check leaves the whole HTTP suite green (S24). Two guards removing the
// same thing hide each other, and the one that could not be seen was this one.
//
// It is kept rather than deleted because it is the general rule and the shape that broke
// EnableAuthenticator — `ModelState.IsValid && await Verify(...)`, which sets an Error for
// a blank field too — is one somebody will write again. So it is exercised here directly,
// against a synthetic ModelState, where the failing case exists.

using AccessibleTrader.WebHost.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace AccessibleTrader.Tests.WebHost;

public sealed class AuthFocusPrecedenceTests
{
    private static (AuthErrorBoxTagHelper Helper, HttpContext Http) NewHelper(bool modelStateValid)
    {
        var http = new DefaultHttpContext();
        var viewData = new ViewDataDictionary(
            new EmptyModelMetadataProvider(), new ModelStateDictionary());
        if (!modelStateValid) viewData.ModelState.AddModelError("Input.Code", "Enter the code.");

        var viewContext = new ViewContext
        {
            HttpContext = http,
            ViewData = viewData,
            RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            ActionDescriptor = new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(),
        };
        return (new AuthErrorBoxTagHelper { ViewContext = viewContext }, http);
    }

    private static TagHelperOutput NewOutput() =>
        new("div", new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

    private static TagHelperContext NewContext() =>
        new(new TagHelperAttributeList(), new Dictionary<object, object>(), "test");

    [Fact]
    public void With_no_field_rejected_the_message_takes_the_focus()
    {
        var (helper, http) = NewHelper(modelStateValid: true);
        var output = NewOutput();

        helper.Process(NewContext(), output);

        Assert.Equal("-1", output.Attributes["tabindex"].Value.ToString());
        Assert.True(output.Attributes.ContainsName("autofocus"));
        Assert.True(http.Items[ValidationStateTagHelper.FocusClaimedKey] is true);
    }

    [Fact]
    public void With_a_field_rejected_the_message_stands_aside()
    {
        // The failing case that no page can currently produce. The box stays FOCUSABLE —
        // a user arrowing back up must be able to land on it — it just does not take the
        // page's opening focus away from the field that actually failed.
        var (helper, http) = NewHelper(modelStateValid: false);
        var output = NewOutput();

        helper.Process(NewContext(), output);

        Assert.Equal("-1", output.Attributes["tabindex"].Value.ToString());
        Assert.False(output.Attributes.ContainsName("autofocus"));
        Assert.Null(http.Items[ValidationStateTagHelper.FocusClaimedKey]);
    }

    [Fact]
    public void A_second_announcement_does_not_claim_the_focus_as_well()
    {
        // Two elements with autofocus is not "the first one wins" in any spec a reader can
        // rely on; it is one attribute too many. The pages place the error block above the
        // note so that if the pair ever renders, the failure is the one that claims — and
        // this makes the second claim a no-op rather than a duplicate attribute.
        var (helper, http) = NewHelper(modelStateValid: true);

        var first = NewOutput();
        helper.Process(NewContext(), first);
        var second = NewOutput();
        helper.Process(NewContext(), second);

        Assert.True(first.Attributes.ContainsName("autofocus"));
        Assert.False(second.Attributes.ContainsName("autofocus"));
        Assert.Equal("-1", second.Attributes["tabindex"].Value.ToString());
    }
}

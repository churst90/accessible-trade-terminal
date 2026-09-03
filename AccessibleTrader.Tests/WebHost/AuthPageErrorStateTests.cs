// Does a rejected sign-in tell the user anything?
//
// The 2026-09-01 audit's finding 3.13 — "error state is not conveyed anywhere in the
// product" — measured four sweeps across both projects and got zero every time:
// aria-invalid, aria-required, required=, aria-disabled. On the nine account pages the
// consequence is specific and was demonstrated on 2026-09-02 before any of this was
// written: POST an empty email, and the response carries the message text in the DOM
// and NO aria-invalid anywhere in the document. Every auth model carries [Required],
// but asp-for emits data-val-required and there is no unobtrusive-validation script on
// any of these pages, so the requirement reached the server and stopped there. A user
// moving back through a failed form heard exactly what they heard before the error.
//
// These pages had never been touched by a test of any kind: the audit's coverage table
// records `grep "cshtml"` across both test projects returning nothing.
//
// What is asserted here is the RENDERED HTML of the real pages served by the real host,
// not the source of any one page and not the tag helpers in isolation — because the
// defect being guarded against is "the tenth page forgot", and only the rendered output
// can see that.
//
// NOT asserted, deliberately: the native `required` attribute, which is the third of the
// audit's four sweeps and stays at zero. Native validation pops a browser bubble that is
// announced inconsistently across screen readers and short-circuits the server round
// trip that produces the messages these pages are careful about (the sign-in failure is
// deliberately generic, because saying which half was wrong is an enumeration oracle).
// aria-required is announced by NVDA, JAWS, VoiceOver and Orca, so nothing is lost.
// Recorded here so a later reader does not "fix" the omission back.

using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace AccessibleTrader.Tests.WebHost;

[Collection("ProviderCredentialBridge")]
public sealed class AuthPageErrorStateTests : IClassFixture<HostedWebHostFixture>
{
    private readonly HostedWebHostFixture _host;
    public AuthPageErrorStateTests(HostedWebHostFixture host) => _host = host;

    private const string Password = "Correct-h0rse-battery";

    private static async Task<IDocument> ParseAsync(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await new HtmlParser().ParseDocumentAsync(await resp.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Every control a user actually types into: visible inputs, selects and textareas.
    /// Hidden fields and the registration honeypot (out of the tab order) are not part
    /// of the form as the user meets it and are excluded here as they are in the page.
    /// </summary>
    private static List<IElement> UserFields(IDocument doc) =>
        doc.QuerySelectorAll("input, select, textarea")
           .Where(e => !string.Equals(e.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase))
           .Where(e => e.GetAttribute("tabindex") != "-1")
           .Where(e => !e.HasAttribute("readonly"))
           .ToList();

    private static List<IElement> TextFields(IDocument doc) =>
        UserFields(doc)
            .Where(e => e.TagName != "INPUT"
                        || !string.Equals(e.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static IElement Field(IDocument doc, string name) =>
        doc.QuerySelectorAll("input, select, textarea").First(e => e.GetAttribute("name") == name);

    // ── The required state, on every anonymously reachable page ──────────────

    public static TheoryData<string> AnonymousPages => new()
    {
        "/terminal/account/login",
        "/terminal/account/register",
        "/terminal/account/forgotpassword",
        "/terminal/account/resetpassword?email=a@b.test&token=x",
    };

    [Theory]
    [MemberData(nameof(AnonymousPages))]
    public async Task Every_field_a_user_types_into_says_it_is_required(string url)
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var doc = await ParseAsync(await client.GetAsync(url));

        var fields = TextFields(doc);

        // The vacuity floor. A page that stopped rendering its form would otherwise
        // report a clean sweep of nothing.
        Assert.True(fields.Count >= 1, $"{url} rendered no user-editable field at all.");

        foreach (var f in fields)
            Assert.True(f.GetAttribute("aria-required") == "true",
                $"{url}: <{f.TagName.ToLowerInvariant()} name=\"{f.GetAttribute("name")}\"> is required by the "
                + "model and says nothing about it. A screen-reader user learns the field was "
                + "mandatory by submitting and failing.");

        // And the visible half, for the sighted and low-vision half of the audience.
        Assert.Contains("Fields marked with an asterisk are required.", doc.Body!.TextContent);
        Assert.NotEmpty(doc.QuerySelectorAll("label .req"));
    }

    [Fact]
    public async Task A_checkbox_is_not_marked_required()
    {
        // ModelMetadata.IsRequired is true for any non-nullable value type, so the naive
        // rule marks "Keep me signed in" — a plain bool — as a mandatory checkbox. That is
        // both false and unsatisfiable: the user cannot make an unchecked box "filled in".
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var doc = await ParseAsync(await client.GetAsync("/terminal/account/login"));

        var remember = Field(doc, "Input.RememberMe");
        Assert.Null(remember.GetAttribute("aria-required"));
    }

    [Fact]
    public async Task The_registration_honeypot_is_left_alone()
    {
        // It is an asp-for input inside an aria-hidden wrapper, deliberately out of the
        // tab order. Marking it required would be a lie; focusing it would be a trap.
        //
        // There used to be an explicit skip for it in the tag helper, and sabotaging that
        // skip changed nothing at all — the honeypot escapes because `Website` is a
        // NULLABLE string (so the required rule declines it) and carries no validation
        // attributes (so it can never be the rejected field that takes focus). The skip
        // was deleted rather than kept as a guard that could not fail; with it gone this
        // case reddens the moment Website becomes a non-nullable string, which is the
        // refactor that would actually reintroduce the defect. Proved red that way.
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var doc = await ParseAsync(await client.GetAsync("/terminal/account/register"));

        var pot = Field(doc, "Website");
        Assert.Equal("-1", pot.GetAttribute("tabindex"));
        Assert.Null(pot.GetAttribute("aria-required"));
        Assert.Null(pot.GetAttribute("aria-invalid"));
        Assert.False(pot.HasAttribute("autofocus"));
    }

    [Fact]
    public async Task A_clean_page_marks_nothing_invalid_and_opens_on_the_first_field()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var doc = await ParseAsync(await client.GetAsync("/terminal/account/login"));

        Assert.Empty(doc.QuerySelectorAll("[aria-invalid]"));
        Assert.DoesNotContain("Error:", doc.Title);
        var focused = doc.QuerySelectorAll("[autofocus]");
        Assert.Single(focused);
        Assert.Equal("Input.Email", focused[0].GetAttribute("name"));
    }

    // ── A rejected FIELD ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_rejected_field_is_marked_invalid_and_is_where_the_page_opens()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var token = await WebHostIntegration.GetAntiforgeryTokenAsync(client, "/terminal/account/login");

        // A password with no email. The email is the field that fails.
        var resp = await client.PostAsync("/terminal/account/login", WebHostIntegration.Form(
            ("__RequestVerificationToken", token),
            ("Input.Email", ""),
            ("Input.Password", Password)));
        var doc = await ParseAsync(resp);

        // Non-vacuity: the server really did reject it and really did render a message.
        var message = doc.QuerySelector("#email-err")!.TextContent.Trim();
        Assert.NotEqual("", message);

        var email = Field(doc, "Input.Email");
        Assert.Equal("true", email.GetAttribute("aria-invalid"));

        // The field that did NOT fail keeps its valid state — otherwise the user is sent
        // round a form marking everything wrong.
        Assert.Null(Field(doc, "Input.Password").GetAttribute("aria-invalid"));

        // And the page opens ON the failure. Exactly one autofocus in the document:
        // the browser honours the first in tree order, so a leftover second one on a
        // page whose first field is not the failing one silently wins.
        var focused = doc.QuerySelectorAll("[autofocus]");
        Assert.Single(focused);
        Assert.Equal("Input.Email", focused[0].GetAttribute("name"));

        // The message is wired to the field, so landing there reads it.
        Assert.Contains("email-err", email.GetAttribute("aria-describedby") ?? "");

        Assert.StartsWith("Error:", doc.Title);
    }

    [Fact]
    public async Task Focus_follows_the_failure_rather_than_the_first_field()
    {
        // The whole point of the previous test's autofocus assertion, made explicit on
        // the case that was broken: an unconditional autofocus on Email dropped the user
        // into Email no matter which field the server rejected.
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var token = await WebHostIntegration.GetAntiforgeryTokenAsync(client, "/terminal/account/register");

        var resp = await client.PostAsync("/terminal/account/register", WebHostIntegration.Form(
            ("__RequestVerificationToken", token),
            ("Input.Email", "someone@example.test"),
            ("Input.Password", "short"),          // fails the 10-character minimum
            ("Input.ConfirmPassword", "short"),
            ("Website", "")));
        var doc = await ParseAsync(resp);

        Assert.NotEqual("", doc.QuerySelector("#pw-err")!.TextContent.Trim());

        var focused = doc.QuerySelectorAll("[autofocus]");
        Assert.Single(focused);
        Assert.Equal("Input.Password", focused[0].GetAttribute("name"));
        Assert.Null(Field(doc, "Input.Email").GetAttribute("aria-invalid"));
    }

    [Fact]
    public async Task The_failure_is_read_before_the_standing_hint()
    {
        // aria-describedby is read in the order it lists ids. Register and ResetPassword
        // pointed the password field at "pw-hint pw-err", so a user who had just failed
        // heard the twelve-word password policy before being told what went wrong.
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var doc = await ParseAsync(await client.GetAsync("/terminal/account/register"));

        var described = Field(doc, "Input.Password").GetAttribute("aria-describedby");
        Assert.Equal("pw-err pw-hint", described);
    }

    // ── A PAGE-LEVEL failure ─────────────────────────────────────────────────

    [Fact]
    public async Task A_wrong_password_focuses_the_message_because_no_field_owns_it()
    {
        const string email = "authstate-wrongpw@example.test";
        await WebHostIntegration.SeedUserAsync(_host.Factory, email, Password);

        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await WebHostIntegration.LoginAsync(client, email, "not-the-password");
        var doc = await ParseAsync(resp);

        var box = doc.QuerySelector(".auth-error");
        Assert.NotNull(box);
        Assert.Contains("incorrect", box!.TextContent, StringComparison.OrdinalIgnoreCase);

        // role="alert" on content that is already in the DOM at parse time does not fire
        // in NVDA or VoiceOver, so the box has to be where focus lands instead.
        Assert.Equal("-1", box.GetAttribute("tabindex"));
        var focused = doc.QuerySelectorAll("[autofocus]");
        Assert.Single(focused);
        Assert.Same(box, focused[0]);

        // Neither field is at fault — saying which half was wrong is an enumeration
        // oracle — so neither is marked invalid.
        Assert.Empty(doc.QuerySelectorAll("[aria-invalid='true']"));

        Assert.StartsWith("Error:", doc.Title);
    }

    [Fact]
    public async Task A_rejected_field_beats_the_page_level_message_for_the_focus()
    {
        // EnableAuthenticator is the page that forced this precedence to be explicit:
        //     bool valid = ModelState.IsValid && await VerifyTwoFactorTokenAsync(...)
        // so a BLANK code fails ModelState *and* used to set the page-level Error as well.
        // First-in-document-order would give the focus to the box, which on a blank
        // submission says "That code didn't match. Codes change every 30 seconds" — a
        // sentence about the wrong problem, sending the user to look at their phone
        // instead of at the empty box. The rejected FIELD is the more specific answer and
        // the one the user can act on, so it wins; and the page no longer claims a
        // mismatch it never checked.
        const string email = "authstate-both@example.test";
        await WebHostIntegration.SeedUserAsync(_host.Factory, email, Password);

        using var client = WebHostIntegration.NewClient(_host.Factory);
        Assert.Equal(HttpStatusCode.Found,
            (await WebHostIntegration.LoginAsync(client, email, Password)).StatusCode);

        var page = await client.GetAsync("/terminal/account/enable2fa");
        var html = await page.Content.ReadAsStringAsync();
        var resp = await client.PostAsync("/terminal/account/enable2fa", WebHostIntegration.Form(
            ("__RequestVerificationToken", WebHostIntegration.ExtractAntiforgeryToken(html)),
            ("Input.Code", "")));            // empty: [Required] fails
        var doc = await ParseAsync(resp);

        // Non-vacuity: the field really was rejected and really did render a message.
        Assert.NotEqual("", doc.QuerySelector("#code-err")!.TextContent.Trim());

        var focused = doc.QuerySelectorAll("[autofocus]");
        Assert.Single(focused);
        Assert.Equal("Input.Code", focused[0].GetAttribute("name"));
        Assert.Equal("true", Field(doc, "Input.Code").GetAttribute("aria-invalid"));

        // And the message about codes rotating is not shown at all, because nothing was
        // compared against anything.
        Assert.DoesNotContain("didn't match", doc.Body!.TextContent);
        Assert.StartsWith("Error:", doc.Title);
    }

    // ── The success notes, which were the unfixed half of the same defect ────

    [Fact]
    public async Task A_confirmation_banner_takes_the_focus_so_it_is_not_jumped_past()
    {
        // "Your password has been updated. Sign in with your new password." is parse-time
        // content in a role="status" region, so the live region never fires; and the
        // unconditional autofocus on Email jumped the reader straight past it. Exactly the
        // shape of the error-box defect, on the confirmation a blind user most needs after
        // a reset.
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var doc = await ParseAsync(await client.GetAsync("/terminal/account/login?reset=1"));

        var note = doc.QuerySelector(".auth-note");
        Assert.NotNull(note);
        Assert.Contains("password has been updated", note!.TextContent);

        var focused = doc.QuerySelectorAll("[autofocus]");
        Assert.Single(focused);
        Assert.Same(note, focused[0]);
        Assert.Equal("-1", note.GetAttribute("tabindex"));
    }

    // There WAS a test here for a note and an error rendering together, with the error
    // taking the focus. It was deleted rather than kept green: the state is unreachable —
    // Login sets PasswordReset only in OnGet, and Security sets Status and Error in
    // exclusive branches — so it asserted nothing and read as protection. The precedence
    // is structural instead: the error block is placed above the note on both pages and
    // the first claim wins. Recorded in docs/TODO.md.


    // ── The parse-time alert that never fired ────────────────────────────────

    [Fact]
    public async Task The_always_present_field_error_span_is_not_an_alert_region()
    {
        // It is in the DOM on every render, empty or not. As role="alert" it fired zero
        // times (alert content present at parse time does not fire) while leaving a
        // permanent implicit assertive live region that would interrupt the moment any
        // client-side validation is added. The span stays — a dangling aria-describedby
        // IDREF is handled differently by every screen reader, and an empty description
        // is announced as nothing — but it is plain text.
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var doc = await ParseAsync(await client.GetAsync("/terminal/account/login"));

        foreach (var span in doc.QuerySelectorAll(".field-error"))
        {
            Assert.Null(span.GetAttribute("role"));
            Assert.Null(span.GetAttribute("aria-live"));
        }
        // The IDREFs still resolve.
        foreach (var f in TextFields(doc))
        foreach (var id in (f.GetAttribute("aria-describedby") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            Assert.True(doc.GetElementById(id) != null,
                $"aria-describedby on {f.GetAttribute("name")} points at #{id}, which is not in the document.");
    }

    // ── The instrument itself ────────────────────────────────────────────────

    [Fact]
    public async Task The_sweep_can_see_a_field_that_lacks_the_state()
    {
        // Every theory above is also what a sweep that recognises nothing would report.
        // Security.cshtml's two password fields are raw name="Password" inputs with no
        // asp-for, so no tag helper can reach them and the state is carried by hand —
        // which makes them the honest control for "the sweep would notice if it went".
        const string email = "authstate-security@example.test";
        await WebHostIntegration.SeedUserAsync(_host.Factory, email, Password);

        using var client = WebHostIntegration.NewClient(_host.Factory);
        Assert.Equal(HttpStatusCode.Found,
            (await WebHostIntegration.LoginAsync(client, email, Password)).StatusCode);

        var doc = await ParseAsync(await client.GetAsync("/terminal/account/security"));

        // Two-factor is off for a fresh user, so the password-confirm forms are not
        // rendered; the page still has to be reachable and clean.
        Assert.Empty(doc.QuerySelectorAll("[aria-invalid='true']"));

        var handMarked = doc.QuerySelectorAll("input[name='Password']");
        foreach (var f in handMarked)
            Assert.Equal("true", f.GetAttribute("aria-required"));
    }
}

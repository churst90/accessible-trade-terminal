namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Proves the instrument before anything is measured with it. If these fail, no finding from the
/// rest of the suite means anything.
/// </summary>
[Collection("Terminal browser")]
public sealed class HarnessSmokeTests
{
    private readonly TerminalBrowserFixture _fixture;
    public HarnessSmokeTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Runs with or without a browser, and says which. A browser suite that skipped every test is
    /// indistinguishable from one that passed unless something prints the reason.
    /// </summary>
    [Fact]
    public void The_browser_suite_reports_whether_it_can_actually_run()
    {
        if (BrowserAvailability.SkipReason is { } reason)
            Assert.Fail(
                "The browser sweep did NOT run on this machine, so nothing below it was measured.\n" +
                reason + "\n\n" +
                "This test fails on purpose rather than skipping: a silently-skipped browser suite " +
                "is the exact shape of a green run that means nothing.");
    }

    /// <summary>
    /// The harness must take ONE socket, and it must not be the demo's.
    ///
    /// <para>
    /// <c>CreateHost</c> asks for port 0 so parallel runs cannot collide, but the builder also
    /// reads <c>AccessibleTrader.WebHost/appsettings.json</c>, whose
    /// <c>Kestrel:Endpoints:Http:Url</c> is <c>http://localhost:5145</c>. A <c>Listen</c> call
    /// does not REPLACE a configured endpoint, it ADDS to it, so the host took both. On CI
    /// nothing owns 5145 and the extra bind succeeds in silence; on the box that serves the demo
    /// on 5145 every case in this suite dies with "address already in use" — which is why that
    /// box runs with a <c>Kestrel__Endpoints__Http__Url</c> override, the tell that put this
    /// item on the list.
    /// </para>
    ///
    /// <para>
    /// This is a plain <see cref="FactAttribute"/>, not a <see cref="BrowserFactAttribute"/>: the
    /// fixture builds the host before it looks for Chromium, so the bind is measurable on a
    /// machine with no browser at all. Asserting <c>RootUrl</c> is non-empty — the obvious
    /// version of this test — passed against the defect.
    /// </para>
    /// </summary>
    [Fact]
    public void The_harness_binds_exactly_one_port_and_it_is_not_the_demos()
    {
        var bound = _fixture.BoundAddresses;

        Assert.True(bound.Count == 1,
            "Kestrel bound " + bound.Count + " addresses: " + string.Join(", ", bound) +
            ". The harness must own exactly one ephemeral port; a second address means a " +
            "configured endpoint survived alongside the port-0 listener.");
        Assert.DoesNotContain("5145", bound[0]);
        Assert.StartsWith("http://127.0.0.1:", bound[0]);
    }

    [BrowserFact]
    public async Task The_terminal_serves_a_page_and_arms_its_keyboard_pipeline()
    {
        await using var t = await _fixture.NewPageAsync();

        // GotoAppAsync already waited on both of these; asserting them makes the failure legible
        // when the wait is what timed out.
        Assert.Equal(1, await t.Page.Locator("#main-heading").CountAsync());
        Assert.True(await t.Page.EvaluateAsync<bool>("() => window.accessibleTrader._inputReady === true"));
        Assert.Equal(0, await t.OpenModalCountAsync());
    }

    [Fact]
    public void The_harness_serves_the_terminal_under_a_path_prefix()
    {
        // Plain [Fact]: the prefix is a property of the host, measurable with no browser.
        Assert.EndsWith(TerminalServerFactory.PathBase + "/", _fixture.RootUrl);
        Assert.StartsWith("http://127.0.0.1:", _fixture.RootUrl);
    }

    [BrowserFact]
    public async Task The_page_resolves_everything_through_the_prefixed_base_href()
    {
        // GotoAppAsync already proved the circuit booted — the framework script loaded, the
        // WebSocket negotiated, the app rendered — all of it under the prefix. This pins WHY
        // that worked: the document's base href is the prefix, so a regression that served a
        // root-relative base under a prefixed host would fail here by name rather than as a
        // 60-second wait for a heading that never comes.
        await using var t = await _fixture.NewPageAsync();

        string baseUri = await t.Page.EvaluateAsync<string>("() => document.baseURI");
        Assert.EndsWith(TerminalServerFactory.PathBase + "/", baseUri);
        Assert.StartsWith(_fixture.RootUrl, t.Page.Url);
    }

    [BrowserFact]
    public async Task No_dialog_is_open_before_anything_is_pressed()
    {
        await using var t = await _fixture.NewPageAsync();
        Assert.Empty(await t.VisibleDialogIdsAsync());
    }
}

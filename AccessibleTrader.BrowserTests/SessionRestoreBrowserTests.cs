using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// <b>Close the terminal's tab, bring it back from the tray, and the indicators are still on the
/// chart.</b>
///
/// <para>
/// Cody, 2026-10-15: "I restored the chart from the task bar/mate bottom panel so when I restore,
/// all my indicators should still be as i had them." Market Structure and Volume Profile (Visible
/// Range) were missing after a restore; Cipher B was there. Brave's own session log showed what
/// "minimise" and "restore" were on that machine: the terminal's tab was CLOSED (02:06:28 and
/// 02:11:09), and each restore was a NEW tab opened from outside the browser — the tray's
/// "Restore workspaces to browser", which is <c>xdg-open</c>. So the round trip under test is
/// exactly that: a tab is closed the way a browser closes it (<c>pagehide</c>, which sends
/// Blazor's disconnect beacon and closes the circuit at once), and a brand-new tab, with its own
/// empty storage, opens the terminal and resumes the session.
/// </para>
///
/// <para>
/// Both tests read the chart's series from the Indicator bar's picker — what the user can reach —
/// not the file on disk. Where the file matters it is asserted as a precondition, so a failure
/// says which half broke.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class SessionRestoreBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public SessionRestoreBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private const string MarketStructure = "Market Structure";
    private const string VolumeProfile = "Volume Profile (Visible Range)";

    [BrowserFact]
    public async Task IndicatorsAddedInAClosedTab_AreOnTheChartInTheNextTab()
    {
        var before = SlotFiles();
        try
        {
            var first = await _fixture.NewPageAsync();
            string ms, vp;
            try
            {
                await first.LoadSeededChartAsync();
                ms = await first.AddIndicatorAsync(MarketStructure);
                vp = await first.AddIndicatorAsync(VolumeProfile);
            }
            finally
            {
                await CloseTheTabAsync(first);
            }

            var saved = await WaitForSlotHoldingAsync(before, ms, vp);
            Assert.True(saved != null,
                $"precondition: no session slot holding both {ms} and {vp} was written after the "
              + "tab closed, so there is nothing for the next tab to resume. Slots now: "
              + string.Join(", ", SlotFiles().Select(Path.GetFileName)));

            var second = await _fixture.NewPageAsync();
            try
            {
                var names = await WaitForSeriesAsync(second, ms, vp);
                var missing = new[] { ms, vp }.Where(n => !names.Contains(n)).ToList();
                Assert.True(missing.Count == 0,
                    $"the tab opened after closing the last one resumed without {string.Join(" and ", missing)}. "
                  + $"The chart holds: {string.Join(" / ", names)}. Saved slot: {Path.GetFileName(saved)}");
            }
            finally
            {
                await CloseTheTabAsync(second);
            }
        }
        finally
        {
            ForgetSlotsWrittenSince(before);
        }
    }

    /// <summary>
    /// <b>The session Cody actually had, resumed.</b> The fixture is his 2026-10-15 02:11:09 slot
    /// byte for byte in its series — Candles, Volume, Price, Cipher B, Market Structure and Volume
    /// Profile (Visible Range) on the first of three tabs, every parameter and component as saved
    /// — with only the tabs' market, provider and symbol re-pointed at the harness's offline
    /// datasets, so no socket opens. Written as the newest slot, then a fresh tab opens: the
    /// question is whether anything between the file and the chart (metadata migration, the
    /// restore loop, the active tab's first data load) drops an indicator.
    /// </summary>
    [BrowserFact]
    public async Task CodysSavedSession_ResumesWithEveryIndicatorOnTheChart()
    {
        var before = SlotFiles();
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "session-2026-10-15-0211.json");
        string seeded = Path.Combine(WorkspacesDir, $"__last-session__{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(WorkspacesDir);
        File.Copy(fixture, seeded);
        File.SetLastWriteTimeUtc(seeded, DateTime.UtcNow.AddSeconds(5));   // the newest, by a margin

        var expected = new[] { "Candles", "Volume", "Price", "Cipher B", MarketStructure, VolumeProfile };
        try
        {
            var t = await _fixture.NewPageAsync();
            try
            {
                var names = await WaitForSeriesAsync(t, expected);
                var missing = expected.Where(n => !names.Contains(n)).ToList();
                Assert.True(missing.Count == 0,
                    $"resuming the 02:11 session dropped {string.Join(", ", missing)}. "
                  + $"The chart holds: {string.Join(" / ", names)}");
            }
            finally
            {
                await CloseTheTabAsync(t);
            }
        }
        finally
        {
            ForgetSlotsWrittenSince(before);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string WorkspacesDir =>
        Path.Combine(_fixture.DataRoot, "data", "AccessibleTrader", "Workspaces");

    private IReadOnlyList<string> SlotFiles() =>
        Directory.Exists(WorkspacesDir)
            ? Directory.GetFiles(WorkspacesDir, "__last-session__*.json")
            : Array.Empty<string>();

    private int CircuitsClosed()
    {
        var log = _fixture.ServerLog;
        lock (log) return log.Count(l => l.Contains("Browser circuit closed", StringComparison.Ordinal));
    }

    /// <summary>
    /// Closes the TAB the way the browser does — <c>pagehide</c> fires and Blazor's beacon tells
    /// the server — and waits for the server to close that circuit, which is when it takes the
    /// final snapshot. Without the wait the next tab can resume before the snapshot lands, and a
    /// circuit merely disconnected would write its slot again three minutes later, after this
    /// test had cleaned up.
    /// </summary>
    private async Task CloseTheTabAsync(TerminalPage t)
    {
        int closedBefore = CircuitsClosed();
        try { await t.Page.CloseAsync(new PageCloseOptions { RunBeforeUnload = true }); }
        catch (PlaywrightException) { /* already closed */ }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (CircuitsClosed() <= closedBefore && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        await t.DisposeAsync();
    }

    /// <summary>A slot written since <paramref name="before"/> whose text names every indicator.</summary>
    private async Task<string?> WaitForSlotHoldingAsync(IReadOnlyList<string> before, params string[] names)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var f in SlotFiles())
            {
                if (before.Contains(f)) continue;
                string text;
                try { text = await File.ReadAllTextAsync(f); }
                catch (IOException) { continue; }   // mid-replace; the next pass reads it
                if (names.All(n => text.Contains($"\"Name\": \"{n}\"", StringComparison.Ordinal)))
                    return f;
            }
            await Task.Delay(200);
        }
        return null;
    }

    /// <summary>The chart's series names once every expected one is there, or as they stand at the timeout.</summary>
    private static async Task<IReadOnlyList<string>> WaitForSeriesAsync(TerminalPage t, params string[] expected)
    {
        try
        {
            await t.Page.WaitForFunctionAsync(
                @"names => { const have = [...document.querySelectorAll('#indicator-select option')]
                                 .map(o => o.textContent.trim());
                             return names.every(n => have.includes(n)); }",
                expected, new PageWaitForFunctionOptions { Timeout = 30_000 });
        }
        catch (TimeoutException) { /* the assertion reports what IS there */ }
        return await t.ActiveSeriesNamesAsync();
    }

    /// <summary>
    /// The suite shares one host and every new page resumes the newest slot, so a slot this test
    /// left behind would put its indicators on the next test's chart. Its tabs are closed (and
    /// their circuits with them) before this runs, so nothing writes these files again.
    /// </summary>
    private void ForgetSlotsWrittenSince(IReadOnlyList<string> before)
    {
        foreach (var f in SlotFiles().Except(before))
        {
            try { File.Delete(f); } catch (IOException) { }
        }
    }
}

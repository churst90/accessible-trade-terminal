using System.Text.Json;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// <b>One announcing channel, measured in a real browser.</b>
///
/// <para>Cody reported that speech went intermittently silent after chart commands — the toggle
/// happened, the sentence often did not — and, arrowing over the bottom of the page in browse
/// mode, that he found "3 lines, one after the other: the first one blank, the second says
/// candles body unmuted, the third says the same thing." The three lines were the diagnosis. The
/// document carried THREE <c>role="status"</c> regions: the two assertive speech buffers (one
/// always blank, which is the design) and <c>StatusBar</c>'s <c>aria-live="polite"</c> mirror
/// holding the identical sentence.</para>
///
/// <para>Two live regions with the same text are not redundancy. Every screen reader drops a
/// live-region message that duplicates what it just queued, so one copy was always discarded —
/// and when the polite copy reached the accessibility bus FIRST (measured on the AT-SPI bus on 6
/// of 16 presses) the assertive copy purged the queued polite one and was then itself dropped as
/// a duplicate of it, and the sentence was spoken neither time. That is not Orca-specific: NVDA,
/// JAWS and VoiceOver all suppress an immediate repeat, and which copy arrives first is decided
/// by the order the browser happens to serialise two DOM updates into one accessibility batch —
/// which is why it was intermittent.</para>
///
/// <para>This runs against real Chromium and the real WebHost because that is the only place the
/// question can be asked. A source scan can tell you what a component declares; only the rendered
/// document can tell you how many live regions the page ends up with once every component has
/// contributed.</para>
/// </summary>
[Collection("Terminal browser")]
public sealed class LiveRegionInventoryBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public LiveRegionInventoryBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Every live region on the cold-start page, as [id or class, role, aria-live, text-length].
    /// Scoped to regions that are PRESENT, because a dialog's own status region only exists while
    /// its dialog is open and is a legitimate second announcer for that dialog's own text.
    /// </summary>
    private const string InventoryJs = @"() => Array.from(
        document.querySelectorAll('[aria-live], [role=status], [role=alert], [role=log]'))
        .map(e => ({
            id: e.id || '',
            cls: (e.className && e.className.baseVal !== undefined ? e.className.baseVal : e.className) || '',
            role: e.getAttribute('role') || '',
            live: e.getAttribute('aria-live') || '',
            text: (e.textContent || '').trim()
        }))";

    private sealed record Region(string Id, string Cls, string Role, string Live, string Text);

    private static async Task<List<Region>> InventoryAsync(TerminalPage t)
    {
        var json = await t.Page.EvaluateAsync<JsonElement>(InventoryJs);
        return json.EnumerateArray().Select(e => new Region(
            e.GetProperty("id").GetString() ?? "",
            e.GetProperty("cls").GetString() ?? "",
            e.GetProperty("role").GetString() ?? "",
            e.GetProperty("live").GetString() ?? "",
            e.GetProperty("text").GetString() ?? "")).ToList();
    }

    /// <summary>
    /// The status strip is present, carries the sentence, and is not a live region. Both halves
    /// matter: dropping aria-live from a strip that had also stopped being rendered would pass a
    /// check for the absence alone while quietly deleting the visual mirror.
    /// </summary>
    [BrowserFact]
    public async Task The_status_strip_shows_the_sentence_and_does_not_announce_it()
    {
        await using var t = await _fixture.NewPageAsync();

        // Something that certainly speaks, and whose sentence the strip mirrors.
        await t.ClearSpokenAsync();
        await t.PressAsync("Alt+o");
        await t.WaitForDialogAsync();
        await t.WaitForSpeechAsync();
        await t.PressAsync("Escape");
        await t.WaitForNoDialogAsync();
        await t.Page.WaitForTimeoutAsync(400);

        string strip = (await t.Page.Locator("section.status-bar .status-content")
                                    .InnerTextAsync()).Trim();
        Assert.False(string.IsNullOrEmpty(strip),
            "The status strip is empty after the terminal spoke. It is the visual mirror of the "
            + "last thing said and a sighted user reads it instead of the invisible buffers.");

        var regions = await InventoryAsync(t);
        var stripRegion = regions.FirstOrDefault(r => r.Cls.Contains("status-content"));
        Assert.True(stripRegion is null,
            "The status strip is declaring itself a live region again "
            + $"(role=\"{stripRegion?.Role}\" aria-live=\"{stripRegion?.Live}\"). It holds the same "
            + "sentence as the speech buffers, so that makes two announcers for one sentence and "
            + "the screen reader drops one of them — sometimes both. It is a MIRROR; landmark and "
            + "browse-mode navigation reach it without aria-live.");
    }

    /// <summary>
    /// The app's own announcing channel is the two speech buffers and nothing else on a cold
    /// start. Dialog-scoped status regions are exempt by construction — no dialog is open here.
    /// </summary>
    [BrowserFact]
    public async Task Only_the_two_speech_buffers_announce_app_speech()
    {
        await using var t = await _fixture.NewPageAsync();
        var regions = await InventoryAsync(t);

        var speech = regions.Where(r => r.Id is "aria-speech-1" or "aria-speech-2").ToList();
        Assert.Equal(2, speech.Count);
        Assert.All(speech, r => Assert.Equal("assertive", r.Live));

        // Blazor's own error alert is part of the framework's shell, not the app's speech.
        var others = regions
            .Where(r => r.Id is not ("aria-speech-1" or "aria-speech-2" or "blazor-error-ui"))
            .Where(r => !r.Cls.Contains("boot-screen"))
            .ToList();

        Assert.True(others.Count == 0,
            "Something other than the speech double-buffer is a live region on the cold-start "
            + "page. Anything that mirrors what the terminal just said will race the buffers and "
            + "cost the user the sentence:\n  "
            + string.Join("\n  ", others.Select(r => $"id='{r.Id}' class='{r.Cls}' role='{r.Role}' aria-live='{r.Live}'")));
    }

    /// <summary>
    /// The buffers empty after their linger, so browse mode finds ONE line at the bottom of the
    /// page — the visible strip — rather than a buffer still holding the last sentence.
    ///
    /// <para>Clearing cannot swallow an announcement: a live region is announced on text being
    /// ADDED, every screen reader captures the text into its own queue when the event is
    /// delivered, and Orca's live-region presenter goes further and presents only
    /// <c>object:text-changed:insert</c>, so a pure deletion is never spoken. The assertion that
    /// the sentence WAS spoken first is in the same test on purpose — a clear that ran too early
    /// would show up here as silence, not as a tidy page.</para>
    /// </summary>
    [BrowserFact]
    public async Task The_speech_buffers_empty_after_announcing()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.ClearSpokenAsync();

        await t.PressAsync("Alt+o");
        await t.WaitForDialogAsync();
        var spoken = await t.WaitForSpeechAsync();
        Assert.NotEmpty(spoken);

        // Immediately after: one buffer holds the sentence.
        string held = await t.Page.EvaluateAsync<string>(
            "() => ['aria-speech-1','aria-speech-2'].map(i => (document.getElementById(i)||{}).textContent || '').join('').trim()");
        Assert.False(string.IsNullOrEmpty(held),
            "Neither speech buffer held the sentence right after it was announced — the live "
            + "region never carried it, so a screen reader on the hosted site heard nothing.");

        // …and after the linger, neither does.
        await t.Page.WaitForTimeoutAsync(4_500);
        string after = await t.Page.EvaluateAsync<string>(
            "() => ['aria-speech-1','aria-speech-2'].map(i => (document.getElementById(i)||{}).textContent || '').join('').trim()");
        Assert.True(after.Length == 0,
            $"A speech buffer is still holding \"{after}\" long after it announced. In browse mode "
            + "that is an extra line at the bottom of the page saying the same thing as the status "
            + "strip, which is what Cody read as three lines.");
    }
}

using System.Text.Json;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Whether the browser voice — the only channel left in "no screen reader" mode — actually
/// speaks. Writes <c>scratchpad/a3_speech_channel.json</c>.
/// </summary>
[Collection("Terminal browser")]
public sealed class BrowserVoiceChannelTests
{
    private readonly TerminalBrowserFixture _fixture;
    public BrowserVoiceChannelTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    [BrowserFact]
    public async Task Where_does_the_browser_voice_go()
    {
        await using var t = await _fixture.NewPageAsync();

        var diag = new Dictionary<string, object?>
        {
            ["speakIsInstrumented"] = await t.Page.EvaluateAsync<bool>(
                "() => typeof window.accessibleTrader.speak === 'function' && window.accessibleTrader.speak.toString().indexOf('__spoken') >= 0"),
            ["hasSpeechSynthesis"] = await t.Page.EvaluateAsync<bool>("() => 'speechSynthesis' in window"),
            ["storedOutputMode"] = await t.Page.EvaluateAsync<string?>(
                "() => window.accessibleTrader.getSpeechOutputMode ? window.accessibleTrader.getSpeechOutputMode() : '(no getter)'"),
            ["promptVisible"] = await t.Page.EvaluateAsync<bool>(
                "() => !!document.querySelector('.speech-prompt')"),
            ["promptHeading"] = await t.Page.EvaluateAsync<string?>(
                "() => { const h = document.getElementById('speech-prompt-title'); return h ? h.textContent.trim() : null; }"),
            ["errorUiDisplayed"] = await t.Page.EvaluateAsync<bool>(
                "() => { const e = document.getElementById('blazor-error-ui'); return !!e && getComputedStyle(e).display !== 'none'; }"),
        };

        // Drive something that certainly announces, then look at both channels.
        await t.ClearSpokenAsync();
        await t.PressAsync("Alt+o");
        await t.WaitForDialogAsync();
        await t.Page.WaitForTimeoutAsync(700);

        diag["spokenAfterAltO"] = await t.SpokenAsync();
        // BOTH regions: MainLayout alternates between two live regions so that repeating the
        // same phrase still re-announces. Reading only the first one reports silence.
        diag["liveRegionAfterAltO"] = await t.Page.EvaluateAsync<string?>(
            "() => ['aria-speech-1','aria-speech-2'].map(i => (document.getElementById(i)||{}).textContent || '').join('|').trim()");

        // Now pick "browser voice" — the mode in which the ARIA live region is deliberately
        // switched OFF (ShouldEnableLiveRegion) and the browser voice is the ONLY channel left.
        await t.PressAsync("Escape");
        await t.WaitForNoDialogAsync();
        await t.ClearSpokenAsync();

        var ttsRadio = t.Page.Locator(".speech-prompt input[value='tts']");
        diag["ttsRadioFound"] = await ttsRadio.CountAsync();
        if (await ttsRadio.CountAsync() > 0)
        {
            await ttsRadio.First.CheckAsync();
            await t.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Save choice" }).ClickAsync();
            await t.Page.WaitForTimeoutAsync(800);

            diag["spokenAfterChoosingBrowserVoice"] = await t.SpokenAsync();

            await t.ClearSpokenAsync();
            await t.PressAsync("Alt+o");
            await t.WaitForDialogAsync();
            await t.Page.WaitForTimeoutAsync(800);
            diag["spokenAfterAltO_inBrowserVoiceMode"] = await t.SpokenAsync();
            diag["liveRegionAfterAltO_inBrowserVoiceMode"] = await t.Page.EvaluateAsync<string?>(
                "() => ['aria-speech-1','aria-speech-2'].map(i => (document.getElementById(i)||{}).textContent || '').join('|').trim()");
        }

        // Control: does the recorder record when JS calls the function directly? If this is
        // empty the instrument is broken; if it records, the .NET side is reaching a different
        // function than the page's window.accessibleTrader.speak.
        await t.ClearSpokenAsync();
        await t.Page.EvaluateAsync("() => window.accessibleTrader.speak('control probe', true)");
        diag["controlProbe"] = await t.SpokenAsync();
        diag["speakSource"] = await t.Page.EvaluateAsync<string>(
            "() => String(window.accessibleTrader.speak).slice(0, 160)");

        diag["serverLogTail"] = _fixture.ServerLog
            .Where(l => l.Contains("peech", StringComparison.Ordinal)
                     || l.Contains("Error", StringComparison.Ordinal)
                     || l.Contains("Exception", StringComparison.Ordinal)
                     || l.Contains("Circuit", StringComparison.Ordinal) || l.Contains("DIAG", StringComparison.Ordinal))
            .TakeLast(40).ToList();

        File.WriteAllText(Path.Combine(RepoRoot(), "scratchpad", "a3_speech_channel.json"),
            JsonSerializer.Serialize(diag, new JsonSerializerOptions { WriteIndented = true }));

        // The first-visit chooser must appear when the browser is the last speech hop, because
        // otherwise a screen-reader user hears everything twice and has no way to stop it.
        Assert.True((bool)diag["promptVisible"]!, "The first-visit speech-output chooser did not render.");

        // The one that matters. "No screen reader — read everything aloud with the browser's
        // voice" switches the ARIA live region OFF, so the browser voice is the only channel the
        // user has left. If it is silent, the option meant for users without a screen reader makes
        // the terminal mute.
        var afterChoice = (IReadOnlyList<Utterance>)diag["spokenAfterChoosingBrowserVoice"]!;
        Assert.True(afterChoice.Count > 0,
            "Choosing browser-voice output announced nothing through window.speechSynthesis.");

        var afterAction = (IReadOnlyList<Utterance>)diag["spokenAfterAltO_inBrowserVoiceMode"]!;
        Assert.True(afterAction.Count > 0,
            "In browser-voice mode the ARIA live region is deliberately disabled, and the browser "
            + "voice said nothing either — the terminal is mute for a user with no screen reader.");
    }
}

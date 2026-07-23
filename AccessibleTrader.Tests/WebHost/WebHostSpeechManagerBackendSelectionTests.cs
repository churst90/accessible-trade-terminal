using AccessibleTrader.WebHost.Services;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Pins the priority order of <c>WebHostSpeechManager.SelectBackend</c>:
/// Orca D-Bus &gt; spd-say &gt; browser SpeechSynthesis. The user wants
/// Orca's voice config honoured whenever Orca is available — a subtle
/// reorder here would silently degrade speech quality (Google's web TTS
/// instead of the configured voxin/espeak voice). These tests catch any
/// regression in the selection ladder without spawning real
/// <c>gdbus</c> / <c>spd-say</c> processes.
/// </summary>
public class WebHostSpeechManagerBackendSelectionTests
{
    [Fact]
    public void PrefersOrcaWhenGdbusAndOrcaAvailable()
    {
        var backend = WebHostSpeechManager.SelectBackend(
            gdbusPath:    "/usr/bin/gdbus",
            spdSayPath:   "/usr/bin/spd-say",
            orcaAvailable: true);

        Assert.Equal(SpeechBackend.OrcaDBus, backend);
    }

    [Fact]
    public void FallsBackToSpdSayWhenOrcaProbeFails()
    {
        // gdbus is on the box but the Orca probe timed out / returned non-zero.
        var backend = WebHostSpeechManager.SelectBackend(
            gdbusPath:    "/usr/bin/gdbus",
            spdSayPath:   "/usr/bin/spd-say",
            orcaAvailable: false);

        Assert.Equal(SpeechBackend.SpdSay, backend);
    }

    [Fact]
    public void FallsBackToSpdSayWhenGdbusMissingButSpdSayPresent()
    {
        var backend = WebHostSpeechManager.SelectBackend(
            gdbusPath:    null,
            spdSayPath:   "/usr/bin/spd-say",
            orcaAvailable: false);

        Assert.Equal(SpeechBackend.SpdSay, backend);
    }

    [Fact]
    public void FallsBackToBrowserTtsWhenNeitherToolFound()
    {
        // Windows / macOS WebHost deploys, or the public-website demo
        // server which has no local TTS daemon: every speech call should
        // route to the browser via BrowserSpeakRequest.
        var backend = WebHostSpeechManager.SelectBackend(
            gdbusPath:    null,
            spdSayPath:   null,
            orcaAvailable: false);

        Assert.Equal(SpeechBackend.BrowserTts, backend);
    }

    [Fact]
    public void OrcaAvailableTrueButGdbusMissingFallsBackToBrowserOrSpdSay()
    {
        // Defensive: if Orca says it's available but gdbus is somehow gone,
        // we can't actually invoke PresentMessage so the ladder must skip
        // the Orca branch. With spd-say available → spd-say.
        var withSpd = WebHostSpeechManager.SelectBackend(
            gdbusPath:    null,
            spdSayPath:   "/usr/bin/spd-say",
            orcaAvailable: true);
        Assert.Equal(SpeechBackend.SpdSay, withSpd);

        // Without spd-say either → browser.
        var without = WebHostSpeechManager.SelectBackend(
            gdbusPath:    null,
            spdSayPath:   null,
            orcaAvailable: true);
        Assert.Equal(SpeechBackend.BrowserTts, without);
    }

    // ── Live-region policy (the 2026-07-23 double-speech fix) ────────────────
    // Exactly ONE sink may vocalize a Speak call. With a server-side backend
    // (Orca D-Bus / spd-say) the server is the voice and the ARIA live region
    // must stay EMPTY — Chrome announces live regions reliably, so Orca read
    // the region while the server also spoke through it: everything doubled.

    [Fact]
    public void ServerSideBackends_DisableTheLiveRegion_RegardlessOfMode()
    {
        foreach (var backend in new[] { SpeechBackend.OrcaDBus, SpeechBackend.SpdSay })
        foreach (AccessibleTrader.Core.Services.SpeechOutputMode mode in
                 System.Enum.GetValues<AccessibleTrader.Core.Services.SpeechOutputMode>())
        {
            Assert.False(WebHostSpeechManager.ShouldEnableLiveRegion(backend, mode));
        }
    }

    [Fact]
    public void BrowserBackend_KeepsTheLiveRegion_ExceptInBrowserVoiceMode()
    {
        Assert.True(WebHostSpeechManager.ShouldEnableLiveRegion(
            SpeechBackend.BrowserTts, AccessibleTrader.Core.Services.SpeechOutputMode.ScreenReader));
        Assert.True(WebHostSpeechManager.ShouldEnableLiveRegion(
            SpeechBackend.BrowserTts, AccessibleTrader.Core.Services.SpeechOutputMode.Both));
        Assert.False(WebHostSpeechManager.ShouldEnableLiveRegion(
            SpeechBackend.BrowserTts, AccessibleTrader.Core.Services.SpeechOutputMode.BrowserVoice));
    }
}

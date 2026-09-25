using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The browser-audio pump — the ONLY sonification path on the public demo and on every
/// WebHost without a local PCM player — publishes a tone and drops silence.
///
/// <para>
/// <b>What nothing checked (A2o, survivor O45).</b> The pump skips buffers whose every sample
/// is below 1e-4, so the browser's schedule can run dry between sounds and the next tone
/// starts at once instead of behind a backlog of streamed silence. Raising that threshold to
/// 1e4 — every buffer "silent" — left the whole suite green: the demo and every
/// player-less host would have heard NO chart audio at all. The pump had never been run by
/// a test, because the only constructor probed the real filesystem for pw-cat / pacat /
/// aplay and, on a Linux box that has one, started it. The internal seam that takes the
/// probe (the same shape <c>PickPlayer</c> already had) is what lets a test hold the driver
/// in browser mode on any machine.
/// </para>
///
/// <para>
/// Two halves, each the other's vacuity check: with no voice sounding, NOTHING is published
/// (the reason the silence skip exists — a threshold at or below zero would stream the
/// backlog again); with a tone sounding, audio arrives.
/// </para>
/// </summary>
public sealed class BrowserAudioPumpTests
{
    private static WebHostAudioDriver BrowserModeDriver(WebHostBrowserAudioSink sink)
        => new(NullLogger<WebHostAudioDriver>.Instance, sink, lifetime: null, fileExists: _ => false);

    private static float Peak(byte[] chunk)
    {
        var samples = new float[chunk.Length / sizeof(float)];
        Buffer.BlockCopy(chunk, 0, samples, 0, samples.Length * sizeof(float));
        return samples.Length == 0 ? 0f : samples.Max(MathF.Abs);
    }

    [Fact]
    public void A_sounding_tone_reaches_the_browser_and_silence_does_not()
    {
        using var sink = new WebHostBrowserAudioSink();
        var chunks = new List<byte[]>();
        using var sub = sink.Chunks.Subscribe(c => { lock (chunks) chunks.Add(c); });
        using var driver = BrowserModeDriver(sink);

        // Silence half: the pump is running (a subscriber exists) and nothing is sounding.
        Thread.Sleep(400);
        lock (chunks)
            Assert.True(chunks.Count == 0,
                $"{chunks.Count} buffer(s) of silence were streamed to the browser — every later tone "
                + "now plays behind that backlog");

        // Tone half.
        driver.SetVoice(0, 440, 0.5f, 0f, "sine", continuous: false, durationSeconds: 0.5);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        float loudest = 0f;
        while (DateTime.UtcNow < deadline)
        {
            lock (chunks) loudest = chunks.Count == 0 ? 0f : chunks.Max(Peak);
            if (loudest > 0.01f) break;
            Thread.Sleep(20);
        }

        Assert.True(loudest > 0.01f,
            "a half-second 440 Hz tone at half volume never reached the browser sink — the demo and "
            + "every host without pw-cat/pacat/aplay would hear no sonification at all");
    }
}

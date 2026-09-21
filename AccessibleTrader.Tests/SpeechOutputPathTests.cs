using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>How speech leaves the desktop head, and what happens when it cannot.</b>
///
/// <para>
/// Reported from a Windows VM on 2026-09-21 — the first time the MAUI head had ever been put in
/// front of a screen reader: focusing the chart said nothing, F2 said nothing, and the same build
/// served over the web through NVDA was fine. Two facts explain it. The first is that
/// <c>nvdaControllerClient64.dll</c> was staged by NOTHING — not tracked by git, not named by any
/// csproj, absent from every publish — so the one copy that existed had been dropped by hand into
/// a <c>bin/Debug</c> folder on 2026-03-03 and only the machine that owned that folder could
/// speak. The second is that this head cannot fall back the way the WebHost can: the chart is a
/// native SkiaSharp canvas sitting ON TOP of the BlazorWebView, so a reader following focus onto
/// the chart is not reading the web view's DOM, and an ARIA live region inside it announces to
/// nobody.
/// </para>
///
/// <para>
/// <b>None of the logic below could be tested before, because it was three <c>DllImport</c>s and
/// a bool decided in a constructor.</b> The same lesson as the notifier seam in the background
/// monitor: make the platform a PARAMETER and the decision becomes ordinary code.
/// </para>
/// </summary>
public sealed class SpeechOutputPathTests
{
    private sealed class FakeNvda : INvdaControllerClient
    {
        public bool LibraryPresent;
        public bool ReaderRunning;
        public bool ThrowOnSpeak;
        public int RunningProbes;
        public List<string> Spoken { get; } = new();
        public int Cancels;

        public bool IsClientLibraryAvailable => LibraryPresent;

        public bool IsReaderRunning()
        {
            RunningProbes++;
            return ReaderRunning;
        }

        public void Speak(string text)
        {
            if (ThrowOnSpeak) throw new InvalidOperationException("NVDA went away");
            Spoken.Add(text);
        }

        public void CancelSpeech() => Cancels++;
    }

    private sealed class RecordingJournal : IJournalService
    {
        public List<JournalEntry> Entries { get; } = new();
        public int Capacity => 1000;
        public event Action<JournalEntry>? EntryAdded;
        public void Add(JournalEntry entry) { Entries.Add(entry); EntryAdded?.Invoke(entry); }
        public void AddSpeech(string text) => Add(new JournalEntry(DateTime.Now, JournalEntryKind.Speech, "TTS", null, text));
        public IReadOnlyList<JournalEntry> Snapshot() => Entries;
        public void Clear() => Entries.Clear();
    }

    private sealed class JournalOnlyProvider : IServiceProvider
    {
        private readonly IJournalService _journal;
        public JournalOnlyProvider(IJournalService journal) => _journal = journal;
        public object? GetService(Type serviceType) => serviceType == typeof(IJournalService) ? _journal : null;
    }

    private static (BlazorSpeechManager Sut, FakeNvda Nvda, RecordingJournal Journal) Build(
        bool libraryPresent, bool readerRunning, TimeSpan? probeInterval = null)
    {
        var nvda = new FakeNvda { LibraryPresent = libraryPresent, ReaderRunning = readerRunning };
        var journal = new RecordingJournal();
        var sut = new BlazorSpeechManager(
            NullLogger<BlazorSpeechManager>.Instance, new JournalOnlyProvider(journal), nvda, probeInterval);
        return (sut, nvda, journal);
    }

    // ── Which path carries an utterance ─────────────────────────────────────────

    [Fact]
    public void WithNvdaPresentAndRunning_SpeechGoesDirect_NotToTheLiveRegion()
    {
        var (sut, nvda, _) = Build(libraryPresent: true, readerRunning: true);
        var live = new List<string>();
        sut.OnSpeak = live.Add;

        sut.Speak("RSI 62", interrupt: true);

        Assert.Equal(new[] { "RSI 62" }, nvda.Spoken);
        Assert.Empty(live);
        Assert.Equal(1, nvda.Cancels);
        Assert.Equal(SpeechOutputStatus.NvdaDirect, sut.OutputStatus);
    }

    [Fact]
    public void WithNoLibrary_SpeechFallsBackToTheLiveRegion()
    {
        var (sut, nvda, _) = Build(libraryPresent: false, readerRunning: false);
        var live = new List<string>();
        sut.OnSpeak = live.Add;

        sut.Speak("RSI 62");

        Assert.Empty(nvda.Spoken);
        Assert.Equal(new[] { "RSI 62" }, live);
        Assert.Equal(SpeechOutputStatus.LiveRegion, sut.OutputStatus);
    }

    /// <summary>
    /// <b>The Windows report, as a test.</b> No DLL, and no live region attached because focus is
    /// on the native chart canvas rather than in the web view. Every sentence the terminal wanted
    /// to say goes nowhere.
    /// </summary>
    [Fact]
    public void WithNoLibraryAndNoLiveRegion_TheTerminalIsMute_AndSaysSoOnce()
    {
        var (sut, _, journal) = Build(libraryPresent: false, readerRunning: false);

        Assert.Equal(SpeechOutputStatus.Mute, sut.OutputStatus);
        Assert.False(sut.IsActive);

        sut.Speak("Focused Candles, body");
        sut.Speak("Speech on");
        sut.Speak("RSI 62");

        var errors = journal.Entries.Where(e => e.Kind == JournalEntryKind.Error).ToList();
        Assert.Single(errors);
        Assert.Contains("no way to speak", errors[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nvdaControllerClient", errors[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And every one of those sentences is still written down. A mute terminal that also forgot
    /// what it could not say would leave the user nothing to recover; the journal is the record.
    /// </summary>
    [Fact]
    public void AMuteTerminalStillJournalsEverySentence()
    {
        var (sut, _, journal) = Build(libraryPresent: false, readerRunning: false);

        sut.Speak("one");
        sut.Speak("two");
        sut.Speak("three");

        var speech = journal.Entries.Where(e => e.Kind == JournalEntryKind.Speech).Select(e => e.Text).ToList();
        Assert.Equal(new[] { "one", "two", "three" }, speech);
    }

    // ── The latch, which is the bug that outlived the DLL ───────────────────────

    /// <summary>
    /// <b>NVDA started after the terminal must be found.</b> The old code asked once, in the
    /// constructor, and stored the answer in a field for the life of a SINGLETON — so a user who
    /// launched the terminal first and their screen reader second was on the fallback path for
    /// the rest of the session, with nothing anywhere saying why.
    /// </summary>
    [Fact]
    public void AReaderThatStartsAfterTheTerminal_IsPickedUp()
    {
        // Zero interval: every utterance re-asks. The point under test is that the answer is
        // asked AGAIN AT ALL, not how often — and driving it through the real parameter rather
        // than by poking the stamp is what makes the ask-once-and-latch sabotage go red.
        var (sut, nvda, _) = Build(libraryPresent: true, readerRunning: false, probeInterval: TimeSpan.Zero);
        var live = new List<string>();
        sut.OnSpeak = live.Add;

        sut.Speak("before");
        Assert.Equal(new[] { "before" }, live);
        Assert.Empty(nvda.Spoken);

        nvda.ReaderRunning = true;

        sut.Speak("after");
        Assert.Equal(new[] { "after" }, nvda.Spoken);
    }

    /// <summary>
    /// The probe is throttled, because speech here is driven by arrow-key repeat and a native
    /// call per utterance would sit on the hot path of the most common interaction there is.
    /// </summary>
    [Fact]
    public void TheReaderProbeIsThrottled_NotOncePerUtterance()
    {
        var (sut, nvda, _) = Build(libraryPresent: true, readerRunning: true);

        for (int i = 0; i < 50; i++) sut.Speak($"bar {i}");

        Assert.Equal(50, nvda.Spoken.Count);
        Assert.True(nvda.RunningProbes < 10,
            $"the reader was probed {nvda.RunningProbes} times for 50 utterances — the throttle is not holding");
    }

    /// <summary>
    /// A library that is absent is absent for good — the build did not stage it — so it must not
    /// be probed over and over. This is the one thing the old latch got right and it is kept.
    /// </summary>
    [Fact]
    public void AMissingLibraryIsNeverProbedForAReader()
    {
        var (sut, nvda, _) = Build(libraryPresent: false, readerRunning: false);

        for (int i = 0; i < 20; i++) sut.Speak($"bar {i}");

        Assert.Equal(0, nvda.RunningProbes);
    }

    /// <summary>
    /// NVDA crashing mid-session must cost the user ONE utterance at most, not the rest of the
    /// session: the utterance falls through to the live region, and the cached "running" answer
    /// is dropped so the next probe asks again rather than latching.
    /// </summary>
    [Fact]
    public void NvdaThrowingMidSession_FallsThroughForThatUtterance_AndReprobes()
    {
        var (sut, nvda, _) = Build(libraryPresent: true, readerRunning: true);
        var live = new List<string>();
        sut.OnSpeak = live.Add;

        nvda.ThrowOnSpeak = true;
        sut.Speak("during the crash");
        Assert.Equal(new[] { "during the crash" }, live);

        // NVDA is now GONE, not merely momentarily unhappy. The throw must have invalidated the
        // cached "running" answer, so the next utterance re-asks, is told no, and takes the live
        // region. Keeping the stale answer instead would send it into a reader that is not there.
        //
        // This is what the first draft of this test could not see: it let the reader come back
        // immediately, so "re-probed and found it" and "never re-probed and assumed it" produced
        // the same observable result and a sabotage of the invalidation passed.
        nvda.ThrowOnSpeak = false;
        nvda.ReaderRunning = false;
        int probesBefore = nvda.RunningProbes;

        sut.Speak("after the crash");

        Assert.True(nvda.RunningProbes > probesBefore,
            "the reader was not re-probed after NVDA threw — the stale answer was kept");
        Assert.Equal(new[] { "during the crash", "after the crash" }, live);
        Assert.Empty(nvda.Spoken);
    }

    // ── The gates that were already there and must stay ─────────────────────────

    [Fact]
    public void SpeechDisabledSaysNothingAndJournalsNothing()
    {
        var (sut, nvda, journal) = Build(libraryPresent: true, readerRunning: true);
        sut.IsSpeechEnabled = false;

        sut.Speak("RSI 62");

        Assert.Empty(nvda.Spoken);
        Assert.Empty(journal.Entries);
    }

    /// <summary>
    /// The WebHost's "browser voice" mode turns the live region off so a reader watching the DOM
    /// does not double-speak. That must not be mistaken for a mute terminal and reported as a
    /// fault: it is a deliberate configuration, and the browser TTS path is carrying the words.
    /// </summary>
    [Fact]
    public void LiveRegionDisabledIsAConfiguration_NotAFault()
    {
        var (sut, _, journal) = Build(libraryPresent: false, readerRunning: false);
        sut.OnSpeak = _ => { };
        sut.LiveRegionEnabled = false;

        sut.Speak("RSI 62");

        Assert.DoesNotContain(journal.Entries, e => e.Kind == JournalEntryKind.Error);
    }

    [Fact]
    public void AQueuedUtteranceIsFlushedWhenTheLiveRegionAttaches()
    {
        var (sut, _, _) = Build(libraryPresent: false, readerRunning: false);

        sut.Speak("the first thing");

        var live = new List<string>();
        sut.OnSpeak = live.Add;

        Assert.Equal(new[] { "the first thing" }, live);
    }

    /// <summary>
    /// Losing every path a second time is worth a second report. The one-shot flag is there to
    /// stop a mute terminal filling the journal with the same line per arrow key, not to make the
    /// condition unreportable for the rest of the session.
    /// </summary>
    [Fact]
    public void MuteIsReportedAgainAfterARecovery()
    {
        var (sut, _, journal) = Build(libraryPresent: false, readerRunning: false);

        sut.Speak("mute once");
        sut.OnSpeak = _ => { };          // recovered
        sut.OnSpeak = null;              // and lost again
        sut.Speak("mute twice");

        Assert.Equal(2, journal.Entries.Count(e => e.Kind == JournalEntryKind.Error));
    }

}

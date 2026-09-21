using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A startup that dies halfway must be reachable, not merely logged.</b>
///
/// <para>
/// <c>AppStartupService.InitializeCoreAsync</c> resolves the application in dependency order, and
/// the order is what makes a partial failure so misleading. The accessibility coordinator — the
/// thing that makes the terminal speak at all — is step 4. The provider list is step 1. Fail in
/// between and you get an application that lists providers and never says a word, which reads
/// like two unrelated bugs and is one fault.
/// </para>
///
/// <para>
/// It is launched through <see cref="SafeFireAndForget"/>, whose entire contract is "catch it and
/// log it" — and the desktop head registered its logging providers inside <c>#if DEBUG</c>, so a
/// RELEASE build had none and the report went nowhere. Reported from a Windows VM on 2026-09-21.
/// The audit had filed "MAUI Release has no logging providers" on 2026-08-24 as a finding in its
/// own right; what it did not do was follow it through to what it costs, which is that the one
/// head nobody can attach a debugger to is also the one head that cannot report anything.
/// </para>
/// </summary>
public sealed class StartupFailureIsReachableTests
{
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

    private static (AppStartupService Sut, RecordingJournal Journal, SpyEventBus Bus) Build()
    {
        var journal = new RecordingJournal();
        var bus = new SpyEventBus();

        var services = new ServiceCollection();
        services.AddSingleton<IJournalService>(journal);
        services.AddSingleton<IEventBus>(bus);
        // The very first await in the startup body, rigged to throw — the earliest reachable
        // failure point, and therefore the one that leaves the most of the application unbuilt.
        var data = Substitute.For<IDataService>();
        data.InitializeAsync(Arg.Any<IPluginLoaderService>())
            .Returns(_ => Task.FromException(new InvalidOperationException("plugin host went bang")));
        services.AddSingleton(data);
        services.AddSingleton(Substitute.For<IPluginLoaderService>());

        var sut = new AppStartupService(services.BuildServiceProvider(),
                                        NullLogger<AppStartupService>.Instance);
        return (sut, journal, bus);
    }

    [Fact]
    public async Task AFailedStartupIsRecordedAsAValueAnyoneCanAskFor()
    {
        var (sut, _, _) = Build();

        await Assert.ThrowsAnyAsync<Exception>(() => sut.InitializeAsync());

        Assert.NotNull(sut.StartupFault);
    }

    /// <summary>
    /// The journal, because it is ordinary DOM a screen reader can read at leisure — and because
    /// on the head where this went wrong the speech channel may itself be among the things that
    /// never came up.
    /// </summary>
    [Fact]
    public async Task AFailedStartupWritesAnErrorIntoTheJournal_NamingTheCauseAndTheLog()
    {
        var (sut, journal, _) = Build();

        await Assert.ThrowsAnyAsync<Exception>(() => sut.InitializeAsync());

        var errors = journal.Entries.Where(e => e.Kind == JournalEntryKind.Error).ToList();
        var entry = Assert.Single(errors);
        Assert.Contains("did not finish starting up", entry.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plugin host went bang", entry.Text, StringComparison.Ordinal);
        Assert.Contains("terminal.log", entry.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the Error feedback channel, which is <c>SpeechChannel.Critical</c> and cannot be
    /// muted — on the chance that speech came up before the failure point.
    /// </summary>
    [Fact]
    public async Task AFailedStartupPublishesOnTheChannelThatCannotBeMuted()
    {
        var (sut, _, bus) = Build();

        await Assert.ThrowsAnyAsync<Exception>(() => sut.InitializeAsync());

        var feedback = bus.Log.OfType<FeedbackRequestEvent>().ToList();
        Assert.Contains(feedback, f => f.Type == FeedbackType.Error
                                    && (f.Message ?? "").Contains("did not finish starting up",
                                                                  StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <b>It still rethrows.</b> This adds channels, it does not replace the one that exists:
    /// <see cref="SafeFireAndForget"/> must still log it, and any caller awaiting the task must
    /// still see it. Swallowing here would trade one silence for another.
    /// </summary>
    [Fact]
    public async Task TheExceptionStillPropagates()
    {
        var (sut, _, _) = Build();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => sut.InitializeAsync());

        Assert.Contains("plugin host went bang", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The once-only contract holds through the new wrapper: the desktop head fires startup from
    /// <c>MainPage</c> AND the shared layout awaits it on first render, and both must share one
    /// task. A wrapper that re-ran the body on the second caller would run the whole resolution
    /// sequence twice.
    /// </summary>
    [Fact]
    public async Task TheBodyStillRunsOnceAcrossConcurrentCallers()
    {
        var (sut, journal, _) = Build();

        var first = sut.InitializeAsync();
        var second = sut.InitializeAsync();

        Assert.Same(first, second);
        await Assert.ThrowsAnyAsync<Exception>(() => first);
        await Assert.ThrowsAnyAsync<Exception>(() => second);

        Assert.Single(journal.Entries.Where(e => e.Kind == JournalEntryKind.Error));
    }
}

/// <summary>
/// The file logger the desktop head lacked entirely in Release. Small, dependency-free and
/// unable to throw into its caller, because it has to work in the one configuration nobody can
/// attach a debugger to.
/// </summary>
public sealed class RollingFileLoggerProviderTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public RollingFileLoggerProviderTests()
    {
        _dir = TestTemp.NewDir("atlog");
        _path = Path.Combine(_dir, "terminal.log");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void ItWritesTheMessageTheCategoryAndTheLevel()
    {
        using var provider = new RollingFileLoggerProvider(_path);
        provider.CreateLogger("Startup").LogError("plugin host went bang");

        var text = File.ReadAllText(_path);
        Assert.Contains("plugin host went bang", text, StringComparison.Ordinal);
        Assert.Contains("Startup", text, StringComparison.Ordinal);
        Assert.Contains("[ERR]", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The exception, in full. "Background task 'AppStartup' failed" on its own names no cause,
    /// and the cause is the entire content of a startup report.
    /// </summary>
    [Fact]
    public void ItWritesTheWholeExceptionIncludingTheInnerChain()
    {
        using var provider = new RollingFileLoggerProvider(_path);
        var inner = new DllNotFoundException("nvdaControllerClient64.dll");
        provider.CreateLogger("Startup").LogError(new InvalidOperationException("outer", inner), "failed");

        var text = File.ReadAllText(_path);
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("DllNotFoundException", text, StringComparison.Ordinal);
        Assert.Contains("nvdaControllerClient64.dll", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BelowTheMinimumLevelNothingIsWritten()
    {
        using var provider = new RollingFileLoggerProvider(_path, LogLevel.Warning);
        provider.CreateLogger("X").LogInformation("chatter");

        Assert.False(File.Exists(_path));
    }

    /// <summary>
    /// A logger that can break the thing it is watching is worse than none. An unwritable path
    /// must disable the provider, not throw on every log statement in the application.
    /// </summary>
    [Fact]
    public void AnUnwritablePathDisablesTheProviderRatherThanThrowing()
    {
        // A path whose parent is an existing FILE — directory creation cannot succeed.
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "not a directory");

        var provider = new RollingFileLoggerProvider(Path.Combine(blocker, "terminal.log"));
        var log = provider.CreateLogger("X");

        var ex = Record.Exception(() => log.LogError("this must not throw"));
        Assert.Null(ex);
        provider.Dispose();
    }
}

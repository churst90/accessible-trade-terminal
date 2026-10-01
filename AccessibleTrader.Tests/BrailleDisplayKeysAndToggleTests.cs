using System.Reactive.Subjects;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Accessibility.Dotpad;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The Dot Pad, end to end from the hardware's own key codes and from the F4 key: the left
/// panning key pans left, F4 turns braille ON when it is off, and a re-enabled display shows the
/// strip again.
///
/// <para>
/// A2q (2026-10-01). The coordinator's tests raised <see cref="TactileKey"/> values directly, so
/// the driver's translation from <see cref="DotPadKeyCode"/> was never exercised; and F4's toggle
/// was only ever driven from the Settings dialog's event. The campaign reversed the panning keys
/// and made F4 repeat the current state; both survived.
/// </para>
/// </summary>
public sealed class BrailleDisplayKeysAndToggleTests
{
    // ── The hardware panning keys ────────────────────────────────────────────

    private static (Action<DotPadKeyCode> Press, ICommandDispatcher Dispatcher, TactileCanvasCoordinator Coord) RealDriverAndCoordinator()
    {
        Action<IntPtr, DotPadKeyCode, string?>? keyCallback = null;
        var native = Substitute.For<IDotPadNative>();
        native.IsAvailable.Returns(true);
        native.When(n => n.RegisterKeyCallback(Arg.Any<Action<IntPtr, DotPadKeyCode, string?>>()))
              .Do(c => keyCallback = c.Arg<Action<IntPtr, DotPadKeyCode, string?>>());
        var driver = new DotpadTactileDriver(native, NullLogger<DotpadTactileDriver>.Instance);

        var dispatcher = Substitute.For<ICommandDispatcher>();
        var store = Substitute.For<IWorkspaceStore>();
        var stream = new BehaviorSubject<WorkspaceState>(WorkspaceState.Initial);
        store.StateStream.Returns(stream);
        store.State.Returns(_ => stream.Value);
        var coord = new TactileCanvasCoordinator(driver, store, Substitute.For<ISpeechFeedbackRouter>(),
            dispatcher, Substitute.For<ISettingsManager>(), new SpyEventBus(),
            NullLogger<TactileCanvasCoordinator>.Instance);

        Assert.NotNull(keyCallback);
        return (code => keyCallback!(IntPtr.Zero, code, null), dispatcher, coord);
    }

    [Fact]
    public void The_Dot_Pads_left_panning_key_pans_the_chart_left()
    {
        var (press, dispatcher, coord) = RealDriverAndCoordinator();
        press(DotPadKeyCode.PanningLeft);

        dispatcher.Received(1).Dispatch(SystemCommand.PanLeft);
        dispatcher.DidNotReceive().Dispatch(SystemCommand.PanRight);
        coord.Dispose();
    }

    [Fact]
    public void The_Dot_Pads_right_panning_key_pans_the_chart_right()
    {
        var (press, dispatcher, coord) = RealDriverAndCoordinator();
        press(DotPadKeyCode.PanningRight);

        dispatcher.Received(1).Dispatch(SystemCommand.PanRight);
        dispatcher.DidNotReceive().Dispatch(SystemCommand.PanLeft);
        coord.Dispose();
    }

    // ── F4: braille on and off ───────────────────────────────────────────────

    /// <summary>A display that connects when asked and records what the strip was sent.</summary>
    private sealed class FakeDisplay : ITactileDriver
    {
        public bool IsConnected { get; private set; }
        public string DeviceName => "Dot Pad";
        public int DisplayWidth => 60;
        public int DisplayHeight => 40;
        public int BrailleCellCount => 20;
        public List<string> StripWrites { get; } = new();
#pragma warning disable CS0067
        public event EventHandler<TactileKeyEvent>? KeyPressed;
        public event EventHandler<TactileConnectionEvent>? ConnectionChanged;
#pragma warning restore CS0067
        public Task ConnectAsync() { IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
        public Task RenderViewportAsync(bool[,] virtualCanvas, int startX, int startY) => Task.CompletedTask;
        public Task RenderBrailleTextAsync(string text) { lock (StripWrites) StripWrites.Add(text); return Task.CompletedTask; }
    }

    private sealed class Rig : IDisposable
    {
        public FakeDisplay Display { get; } = new();
        public SpyEventBus Bus { get; } = new();
        public List<string> Spoken { get; } = new();
        public ISettingsManager Settings { get; } = Substitute.For<ISettingsManager>();
        public TactileCanvasCoordinator Coord { get; }

        public Rig(bool brailleOn)
        {
            JToken? stored = brailleOn ? JToken.FromObject(true) : null;
            Settings.GetSetting(TactileCanvasCoordinator.BrailleEnabledKey, Arg.Any<JToken?>()).Returns(_ => stored);
            Settings.When(s => s.SetSetting(TactileCanvasCoordinator.BrailleEnabledKey, Arg.Any<JToken>()))
                    .Do(c => stored = c.ArgAt<JToken>(1));

            var store = Substitute.For<IWorkspaceStore>();
            var stream = new BehaviorSubject<WorkspaceState>(WorkspaceState.Initial);
            store.StateStream.Returns(stream);
            store.State.Returns(_ => stream.Value);
            var speech = Substitute.For<ISpeechFeedbackRouter>();
            speech.When(s => s.Speak(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<SpeechChannel>()))
                  .Do(c => { lock (Spoken) Spoken.Add(c.ArgAt<string>(0)); });
            Coord = new TactileCanvasCoordinator(Display, store, speech, Substitute.For<ICommandDispatcher>(),
                Settings, Bus, NullLogger<TactileCanvasCoordinator>.Instance);
        }

        public bool? Stored => Settings.GetSetting(TactileCanvasCoordinator.BrailleEnabledKey)?.ToObject<bool>();

        public void Dispose() => Coord.Dispose();
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(30);
        Assert.True(condition(), what);
    }

    [Fact]
    public void F4_with_braille_off_turns_it_on_and_says_so()
    {
        using var rig = new Rig(brailleOn: false);

        rig.Bus.Publish(new BrailleToggleRequestedEvent());

        Assert.Contains("Braille on.", rig.Spoken);
        Assert.True(rig.Stored);
    }

    [Fact]
    public async Task F4_twice_turns_braille_on_then_off()
    {
        using var rig = new Rig(brailleOn: false);
        rig.Bus.Publish(new BrailleToggleRequestedEvent());
        await Until(() => rig.Display.IsConnected, "the display never connected after braille was turned on");

        rig.Bus.Publish(new BrailleToggleRequestedEvent());

        Assert.Equal(new[] { "Braille on.", "Braille off." }, rig.Spoken.ToArray());
        Assert.False(rig.Stored);
    }

    // ── The strip after the display comes back ───────────────────────────────

    /// <summary>
    /// A2q survey §4 item 5, the strip half. A display that disconnects and comes back is BLANK —
    /// the device does not remember what it showed — so the strip has to be sent again. The
    /// coordinator skips a strip text identical to the last one it sent, and that memory survives
    /// the disconnect, so the catch-up render after reconnecting is skipped as a "repeat" and the
    /// strip stays empty until the value under the cursor changes.
    /// </summary>
    [Fact]
    public async Task After_braille_is_switched_off_and_on_again_the_strip_is_sent_again()
    {
        using var rig = new Rig(brailleOn: true);
        await Until(() => rig.Display.IsConnected && rig.Display.StripWrites.Count > 0,
            "the display never connected and showed its strip");
        string shown = rig.Display.StripWrites[^1];

        rig.Bus.Publish(new BrailleModeToggledEvent(false));
        await Until(() => !rig.Display.IsConnected, "the display did not disconnect");
        int before = rig.Display.StripWrites.Count;

        rig.Bus.Publish(new BrailleModeToggledEvent(true));
        await Until(() => rig.Display.IsConnected, "the display did not reconnect");
        await Task.Delay(200);

        Assert.True(rig.Display.StripWrites.Count > before,
            $"the display reconnected blank and the strip (\"{shown}\") was never sent to it again");
    }

    /// <summary>
    /// The driver half of the same defect: it RESETS the braille strip when a device connects
    /// (clearing stale pins), then skipped the first text it was handed if that text matched what
    /// it sent before the unplug — so a replugged display kept a blank strip.
    /// </summary>
    [Fact]
    public async Task A_replugged_display_is_sent_the_strip_text_again()
    {
        Action<IntPtr, DotPadDataCode, string?>? message = null;
        var native = Substitute.For<IDotPadNative>();
        native.IsAvailable.Returns(true);
        native.When(n => n.RegisterMessageCallback(Arg.Any<Action<IntPtr, DotPadDataCode, string?>>()))
              .Do(c => message = c.Arg<Action<IntPtr, DotPadDataCode, string?>>());
        native.When(n => n.StartUsbScan(Arg.Any<Action<string>>())).Do(c => c.Arg<Action<string>>()("COM9"));
        native.When(n => n.ConnectSerial(Arg.Any<string>())).Do(_ => message!(IntPtr.Zero, DotPadDataCode.Connected, null));
        native.GetConnectedDeviceCount().Returns(1);
        native.TryGetConnectedDeviceHandle(0, out Arg.Any<IntPtr>())
              .Returns(c => { c[1] = new IntPtr(7); return true; });
        native.TryGetDisplayInfo(Arg.Any<IntPtr>(), out Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>())
              .Returns(c => { c[1] = 30; c[2] = 10; c[3] = 20; return true; });
        native.DisplayBrailleText(Arg.Any<IntPtr>(), Arg.Any<string>(), Arg.Any<DotPadLanguage>(), Arg.Any<DotPadBrailleGrade>())
              .Returns(true);

        var driver = new DotpadTactileDriver(native, NullLogger<DotpadTactileDriver>.Instance);
        await driver.ConnectAsync();
        Assert.True(driver.IsConnected);
        await driver.RenderBrailleTextAsync("64100");

        message!(new IntPtr(7), DotPadDataCode.Disconnected, null);     // unplugged
        Assert.False(driver.IsConnected);
        await driver.ConnectAsync();                                      // plugged back in
        Assert.True(driver.IsConnected);
        await driver.RenderBrailleTextAsync("64100");

        native.Received(2).DisplayBrailleText(Arg.Any<IntPtr>(), "64100",
            Arg.Any<DotPadLanguage>(), Arg.Any<DotPadBrailleGrade>());
    }
}

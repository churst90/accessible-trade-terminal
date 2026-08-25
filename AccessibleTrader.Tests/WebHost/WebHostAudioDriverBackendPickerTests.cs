using System.Runtime.InteropServices;
using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Pins the priority order of <c>WebHostAudioDriver.PickPlayer</c>: pw-cat
/// (PipeWire) wins over pacat (PulseAudio) wins over aplay (ALSA). A
/// future audio-stack change could quietly invert that order; these tests
/// catch it. Production passes <c>File.Exists</c> for the predicate;
/// tests pass a closure so we never touch the real filesystem.
/// </summary>
public class WebHostAudioDriverBackendPickerTests
{
    private static bool OnLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    [Fact]
    public void PicksPwCatFirstWhenAllThreeAreAvailable()
    {
        if (!OnLinux) return; // Picker is Linux-only by design

        var (path, args) = WebHostAudioDriver.PickPlayer(p =>
            p is "/usr/bin/pw-cat" or "/usr/bin/pacat" or "/usr/bin/aplay");

        Assert.Equal("/usr/bin/pw-cat", path);
        // Sanity-check the args carry the audio format pw-cat expects.
        Assert.Contains("--playback", args);
        Assert.Contains("--rate",     args);
        Assert.Contains("44100",      args);
        Assert.Contains("--channels", args);
        Assert.Contains("2",          args);
        Assert.Contains("--format",   args);
        Assert.Contains("f32",        args);
        Assert.Contains("--raw",      args);
    }

    [Fact]
    public void FallsBackToPacatWhenPwCatMissing()
    {
        if (!OnLinux) return;

        var (path, args) = WebHostAudioDriver.PickPlayer(p =>
            p is "/usr/bin/pacat" or "/usr/bin/aplay");

        Assert.Equal("/usr/bin/pacat", path);
        // pacat uses `--rate=44100` (= syntax), not space-separated args.
        Assert.Contains("--playback",        args);
        Assert.Contains("--rate=44100",      args);
        Assert.Contains("--channels=2",      args);
        Assert.Contains("--format=float32le", args);
        Assert.Contains("--raw",             args);
    }

    [Fact]
    public void FallsBackToAplayWhenNeitherPwCatNorPacatPresent()
    {
        if (!OnLinux) return;

        var (path, args) = WebHostAudioDriver.PickPlayer(p => p == "/usr/bin/aplay");

        Assert.Equal("/usr/bin/aplay", path);
        // aplay uses `-t raw -f FLOAT_LE -c 2 -r 44100`.
        Assert.Contains("-t",       args);
        Assert.Contains("raw",      args);
        Assert.Contains("-f",       args);
        Assert.Contains("FLOAT_LE", args);
        Assert.Contains("-c",       args);
        Assert.Contains("2",        args);
        Assert.Contains("-r",       args);
        Assert.Contains("44100",    args);
    }

    [Fact]
    public void ReturnsNullPathWhenNothingFound()
    {
        // Works on every OS — picker is gated on Linux but returns
        // (null, []) for non-Linux too, so this case is platform-portable.
        var (path, args) = WebHostAudioDriver.PickPlayer(_ => false);

        Assert.Null(path);
        Assert.Empty(args);
    }

    [Fact]
    public void NonLinuxAlwaysReturnsNullEvenIfPredicateLies()
    {
        if (OnLinux) return; // This case only kicks in on macOS / Windows test boxes

        // Even when the fileExists predicate would happily say pw-cat exists,
        // the picker is gated on Linux because the player CLIs are POSIX-only
        // and the spawn would fail at runtime.
        var (path, args) = WebHostAudioDriver.PickPlayer(_ => true);

        Assert.Null(path);
        Assert.Empty(args);
    }

    [Fact]
    public void SearchesAllStandardBinDirectories()
    {
        if (!OnLinux) return;

        // /usr/local/bin is also checked, not just /usr/bin.
        var (path, _) = WebHostAudioDriver.PickPlayer(p => p == "/usr/local/bin/pw-cat");
        Assert.Equal("/usr/local/bin/pw-cat", path);
    }
}

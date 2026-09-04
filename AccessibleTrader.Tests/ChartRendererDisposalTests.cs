using System.Collections.Immutable;
using System.Reflection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// The hosted heads segfault about twice a day, always with the same siginfo:
/// <c>signo 11 code 0001 addr 0x8</c>, at or within seconds of the
/// <c>Browser circuit closed</c> log line
/// (<c>WebHostBrowserCircuitHandler.cs:205</c>). See
/// <c>patches/HOSTED-DEPLOY-NOTES.md</c> §7.
///
/// <para><b>The mechanism.</b> <see cref="ChartRenderer"/> is registered
/// <c>AddScoped</c> in the hosted head
/// (<c>AccessibleTrader.WebHost/ServiceCollectionExtensions.cs:362</c>), so there
/// is one per Blazor circuit and the circuit scope disposes it at teardown.
/// <c>ChartRenderer.Dispose()</c> (<c>ChartRenderer.cs:1102</c>) frees two native
/// SkiaSharp handles — <c>_textPaint</c> and <c>_textFont</c> — and SkiaSharp 3.x
/// zeroes <c>SKObject.Handle</c> on dispose without adding any managed guard.
/// Meanwhile <c>ChartArea.razor:623</c> renders each frame inside a bare
/// <c>Task.Run(...)</c> that <c>ChartArea.Dispose()</c> (<c>:660</c>) neither
/// tracks, cancels nor awaits. A frame still in flight (or a continuation that
/// resumes) after the scope has been disposed re-enters
/// <c>ChartRenderer.Render</c>, whose first native touch is
/// <c>ChartRenderer.cs:82</c> — <c>_textFont.Size = ...</c> — i.e.
/// <c>sk_font_set_size(NULL, ...)</c>, a write to <c>SkFont::fSize</c> at offset
/// 8. That is the reported <c>addr 0x8</c>, and it is the same address every time
/// because line 82 is always the first disposed-handle access in the frame
/// (<c>_textPaint.Color</c> at <c>:110</c> measures 0x30 instead).</para>
///
/// <para><b>FIXED 2026-09-03, and these are now GUARDS.</b> They were written as
/// pinned-defect tests — the real reproduction is a process-killing SIGSEGV and
/// cannot live in a test suite — and they have been inverted, which is the signal
/// the fix landed.</para>
///
/// <para><b>The fix is that <c>ChartRenderer.Dispose()</c> is EMPTY</b>, and both
/// tests below exist to stop it being "tidied up" again. The two obvious
/// alternatives are both wrong:</para>
/// <list type="bullet">
///   <item><b>Dispose the handles anyway</b> — that is the crash, verbatim.</item>
///   <item><b>Dispose them behind a <c>_disposed</c> flag</b> — that closes only the
///     sequential window. A frame already past the flag when <c>Dispose()</c> runs
///     still writes to a freed <c>SkFont</c>, so a reliable twice-a-day crash
///     becomes a rare one. Strictly harder to diagnose, and it would leave the
///     first test below green.</item>
/// </list>
///
/// <para>Empty is correct rather than merely quiet: <c>SKPaint</c> and <c>SKFont</c>
/// carry SkiaSharp's own finalizer, which cannot run while the object is reachable,
/// and an in-flight render holds a strong reference to the renderer that holds these
/// fields. So the handles are reclaimed exactly when no frame can still use them —
/// the property a hand-written <c>Dispose</c> here cannot express. The cost is one
/// small paint and one small font per circuit reclaimed at GC rather than at
/// teardown.</para>
/// </summary>
public class ChartRendererDisposalTests
{
    /// <summary>
    /// Disposing the renderer must NOT free the two <c>SK*</c> handles that
    /// <c>Render</c> writes into on every frame.
    ///
    /// <para>
    /// The fields are <c>readonly</c>, so nothing can null them out, and
    /// <c>Render</c> cannot be made to notice: a frame already running is past any
    /// check by the time teardown happens. So the only safe answer is not to free
    /// them here at all and let SkiaSharp's finalizer do it once the renderer —
    /// and therefore any frame holding it — is unreachable.
    /// </para>
    ///
    /// <para>
    /// Restoring <c>_textPaint.Dispose(); _textFont.Dispose();</c> turns this red,
    /// which is the whole point: it is the twice-a-day production SIGSEGV.
    /// </para>
    /// </summary>
    [Fact]
    public void DisposingTheRendererDoesNotFreeTheHandlesAFrameMayStillBeUsing()
    {
        var renderer = NewRenderer();

        var font = (SKFont)FieldValue(renderer, "_textFont")!;
        var paint = (SKPaint)FieldValue(renderer, "_textPaint")!;
        Assert.NotEqual(IntPtr.Zero, font.Handle);
        Assert.NotEqual(IntPtr.Zero, paint.Handle);

        renderer.Dispose();     // == what the circuit scope does at teardown

        // Same instances, still hanging off the renderer, and STILL LIVE. A write to
        // font.Size here is sk_font_set_size on a real SkFont, not on NULL — which is
        // exactly what ChartRenderer.cs:82 does on the first line of every frame.
        Assert.Same(font, FieldValue(renderer, "_textFont"));
        Assert.Same(paint, FieldValue(renderer, "_textPaint"));
        Assert.NotEqual(IntPtr.Zero, font.Handle);
        Assert.NotEqual(IntPtr.Zero, paint.Handle);

        // The handle really is usable, not merely non-zero. Without this the test
        // would pass on any object whose Handle field happened to be populated.
        font.Size = 11f;
        Assert.Equal(11f, font.Size, 3);
    }

    /// <summary>
    /// A frame that arrives AFTER teardown must draw successfully.
    ///
    /// <para>
    /// That is the production case: <c>ChartArea.razor:623</c> renders inside a bare
    /// <c>Task.Run</c> that its own <c>Dispose</c> neither tracks, cancels nor
    /// awaits, so a parked continuation resumes on a thread-pool thread after the
    /// circuit scope has already disposed the renderer. <c>Render</c> is not given a
    /// disposal gate — deliberately, because a gate cannot help a frame that is
    /// already past it — so the requirement is simply that the frame is HARMLESS.
    /// </para>
    ///
    /// <para>
    /// No handle-swapping any more. The previous version of this test had to splice
    /// live Skia objects into the two zeroed fields before rendering, because
    /// otherwise the frame's first statement dereferenced NULL and killed the test
    /// host. Not needing the splice is itself the evidence the fix works: the same
    /// call now runs against handles teardown left alone.
    /// </para>
    /// </summary>
    [Fact]
    public void AFrameArrivingAfterTeardownDrawsInsteadOfFaulting()
    {
        // What a frame that drew NOTHING encodes to — the cleared canvas alone.
        // Any real frame must differ from this, otherwise "it rendered" is vacuous.
        int blankBytes = EncodeBlankFrame();

        var renderer = NewRenderer();

        // A normal frame first, so the fixture is known to reach the drawing code.
        int liveBytes = RenderOneFrame(renderer);
        Assert.True(liveBytes > blankBytes,
            $"fixture drew nothing: {liveBytes} bytes vs {blankBytes} for a cleared canvas");

        renderer.Dispose();

        // The frame the circuit used to die on.
        int afterDisposeBytes = RenderOneFrame(renderer);
        Assert.True(afterDisposeBytes > blankBytes,
            $"post-teardown frame drew nothing: {afterDisposeBytes} bytes vs {blankBytes} blank");

        // And ChartRenderer.cs:82 — `_textFont.Size = AxisFontSize * density` — really
        // did execute, against a live font. This is the assertion that makes the test
        // about the faulting statement rather than about Render returning: a sentinel
        // no theme uses, written before the frame and overwritten by line 82.
        const float Sentinel = 4242f;
        var font = (SKFont)FieldValue(renderer, "_textFont")!;
        font.Size = Sentinel;
        RenderOneFrame(renderer);

        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        float expected = new ThemeService(settings).Current.AxisFontSize * Density;
        Assert.NotEqual(Sentinel, expected);
        Assert.Equal(expected, font.Size, 3);
    }

    /// <summary>Encode the same surface with the clear but no <c>Render</c> call.</summary>
    private static int EncodeBlankFrame()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Black);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 90);
        return png.ToArray().Length;
    }

    // ── Fixture ─────────────────────────────────────────────────────────────
    //
    // Constructed the way ChartAreaBrowserCanvasBranchTests does it: a real
    // ThemeService over a substitute settings source, substitutes for the rest.
    // ChartRenderer takes the concrete ThemeService, not IThemeService.

    private const float Density = 1.0f;
    private const int Width = 1280, Height = 720;

    private static ChartRenderer NewRenderer()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var pane = Substitute.For<IPaneLayoutService>();
        pane.Dividers.Returns(new List<(string BelowPaneName, float DividerFraction)>());

        return new ChartRenderer(
            new ThemeService(settings),
            Substitute.For<IStylingService>(),
            pane,
            NullLogger<ChartRenderer>.Instance,
            Substitute.For<Sdk.Logging.IAppLogger>());
    }

    /// <summary>
    /// One frame, drawn the way <c>ChartArea.razor:623-637</c> draws it inside its
    /// <c>Task.Run</c>: an off-screen raster surface, <c>Render</c>, snapshot,
    /// PNG-encode. Returns the encoded byte count so a caller can tell a real
    /// frame from a no-op.
    /// </summary>
    private static int RenderOneFrame(ChartRenderer renderer)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        renderer.Render(
            canvas, Width, Height, Bars(), Series(),
            cursorIndex: 150, viewportStart: 0, viewportLength: 200,
            viewportRange: (90, 115),
            paneRanges: new Dictionary<string, (double Min, double Max)> { ["Main"] = (90, 115) },
            isHeikinAshi: false, isLogScale: false, density: Density,
            paneHeightRatios: ImmutableDictionary<string, float>.Empty,
            rightMarginBars: 10, formations: null);

        canvas.Flush();
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 90);
        return png.ToArray().Length;
    }

    private static List<Ohlcv> Bars()
    {
        var bars = new List<Ohlcv>(300);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 300; i++)
        {
            double open = 100 + (i % 5);
            double close = i % 2 == 0 ? open + 2 : open - 2;
            bars.Add(new Ohlcv(start.AddMinutes(i), open,
                Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, 1000 + i));
        }
        return bars;
    }

    private static List<ChartSeries> Series()
    {
        var config = new SeriesConfig { Id = "price", Name = "price", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "body",
            DisplayType = ComponentDisplayType.Candle,
            ColorHex = "#00FF00",
            ColorHexSecondary = "#FF0000",
        });
        return new List<ChartSeries> { new ChartSeries(config, new SeriesDataBuffer { SeriesId = "price" }) };
    }

    private static object? FieldValue(ChartRenderer renderer, string name) =>
        Field(name).GetValue(renderer);

    private static void SetField(ChartRenderer renderer, string name, object value) =>
        Field(name).SetValue(renderer, value);

    private static FieldInfo Field(string name) =>
        typeof(ChartRenderer).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"ChartRenderer.{name} is gone — this test pins the disposal defect on that field.");
}

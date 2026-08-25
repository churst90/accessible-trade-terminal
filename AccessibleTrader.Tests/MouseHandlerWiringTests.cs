using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Tests.Mocks;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// L4-B: pins the JS-interop and event-bus halves of the browser mouse pipeline.
///
/// The full chain in production is:
///   js/keyboard.js registerMouseHandler  →  GlobalInputService.OnMouseEvent
///   →  IInputService.ProcessMouse  →  BlazorInputService.MouseEvent
///   →  DrawingInteractionManager.HandleMouseEvent  (covered by
///      DrawingInteractionManagerMouseDispatchTests).
///
/// These two tests pin the upstream end: that <see cref="GlobalInputService"/>
/// asks the browser to register the handler on the right DOM id at startup,
/// and that <see cref="BlazorInputService"/> forwards the (x, y, type, w, h)
/// payload identically to what JS sent in.
/// </summary>
public sealed class MouseHandlerWiringTests
{
    [Fact]
    public async Task InitializeAsync_registers_mouse_handler_on_chart_interact_zone()
    {
        // Pins the DOM-id contract: keyboard.js's registerMouseHandler binds events on
        // whatever element id the .NET side asks for. If the id ever drifts apart from
        // ChartArea.razor's @id (currently "chart-interact-zone"), clicks would silently
        // stop reaching drawing tools.
        using var ctx = new TestContext();
        // Loose mode auto-completes void invocations without per-call setup, so
        // the awaited InvokeVoidAsync calls inside InitializeAsync return. Strict
        // (the default) requires SetVoidResult() per invocation and would hang.
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var input = new BlazorInputService();
        var sut = new GlobalInputService(input, new SpyEventBus(), Substitute.For<IWorkspaceStore>());

        await sut.InitializeAsync(ctx.JSInterop.JSRuntime);

        var registerCall = ctx.JSInterop.Invocations
            .FirstOrDefault(i => i.Identifier == "accessibleTrader.registerMouseHandler");
        Assert.Equal("accessibleTrader.registerMouseHandler", registerCall.Identifier);
        // Args: [DotNetObjectReference, elementId]. Second arg pins the DOM id contract.
        Assert.Equal("chart-interact-zone", registerCall.Arguments[1]);
    }

    [Fact]
    public void ProcessMouse_forwards_payload_identically_to_MouseEvent_subscribers()
    {
        // Pins the event-bus contract that GlobalInputService.OnMouseEvent relies on:
        // (x, y, type, width, height) arrive at any DrawingInteractionManager / future
        // subscriber unchanged. JS sends pixel coordinates relative to the chart rect
        // plus the rect's own width/height; the consumer needs all five to map
        // (x/width, y/height) into a (date, price) without losing precision.
        var input = new BlazorInputService();
        (double X, double Y, string Type, double W, double H)? captured = null;
        input.MouseEvent += (x, y, type, w, h) => captured = (x, y, type, w, h);

        input.ProcessMouse(123.4, 56.7, "MouseDown", 1280, 720);

        Assert.NotNull(captured);
        Assert.Equal(123.4, captured!.Value.X);
        Assert.Equal(56.7, captured.Value.Y);
        Assert.Equal("MouseDown", captured.Value.Type);
        Assert.Equal(1280, captured.Value.W);
        Assert.Equal(720, captured.Value.H);
    }
}

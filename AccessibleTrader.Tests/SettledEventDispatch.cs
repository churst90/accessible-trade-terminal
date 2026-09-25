using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>bUnit's synchronous event triggers, made to wait for the renderer.</b> Every
    /// <c>element.Click()</c>, <c>.Change(...)</c>, <c>.Input(...)</c>, <c>.KeyDown(...)</c>,
    /// <c>.Blur()</c>, <c>.Focus()</c>, <c>.FocusIn()</c>, <c>.Toggle()</c> and
    /// <c>.TriggerEvent(...)</c> in this project binds HERE rather than to bUnit, with no edit at
    /// the call site: C# looks for extension methods namespace by namespace from the innermost
    /// outward, and every test file is declared inside <c>AccessibleTrader.Tests</c>, which is
    /// searched before the file's <c>using Bunit;</c>.
    ///
    /// <para>
    /// <b>Why.</b> bUnit's synchronous triggers do not wait. They hand the event to the renderer's
    /// dispatcher and discard the Task. When the dispatcher is idle that runs the handler inline,
    /// so the next line of the test sees its effects, and every test in this project was written
    /// against that case. When the dispatcher is BUSY (a dialog's <c>ShowAsync</c> still running
    /// after its first render, <c>OnAfterRenderAsync</c> finishing, the Trading Dashboard's
    /// two-second refresh timer), the event is queued and the trigger returns before the handler
    /// has run. The next line then asserts on a handler that has not happened. That only happens
    /// under load, and it was the mechanism behind the flakes that kept turning up as false
    /// "catches" in the mutation campaigns (<c>ChartAreaBarSliderTests</c>,
    /// <c>OrderTicketErrorStateTests</c>). Demonstrated 2026-09-25 by holding the dispatcher from
    /// another thread for 500 ms: <c>.Input("15")</c> returned with no <c>NavigateAction</c>
    /// dispatched, and the refused Submit spoke nothing.
    /// </para>
    ///
    /// <para>
    /// <b>How.</b> The bUnit trigger is run AS a dispatcher work item and the test waits for that
    /// item: it queues behind whatever the renderer is doing, then runs the handler inline, up
    /// to its first real <c>await</c>, exactly as the idle case always did. This is the
    /// <c>cut.InvokeAsync(() =&gt; x.Click()).GetAwaiter().GetResult()</c> form that 26 sites in
    /// this project already used by hand. It deliberately does NOT wait for the handler's Task
    /// to complete. A handler parked on a JS call nobody answers would then hang the test,
    /// where before it returned and let the test assert on what happened before the await.
    /// </para>
    ///
    /// <para>
    /// Called from ON the dispatcher (inside a <c>cut.InvokeAsync</c>), it runs inline, as bUnit
    /// would, because waiting there for a queued item would deadlock.
    /// </para>
    ///
    /// <para>
    /// <see cref="SettledEventDispatchTests"/> holds the dispatcher and fires every overload here,
    /// so a shape that stops binding here, or a body that stops waiting, goes red.
    /// </para>
    /// </summary>
    public static class SettledEventDispatch
    {
        // bUnit's own route from an element to the context that rendered it (its triggers use it
        // to find the renderer). It is not public in bUnit 1.40, hence reflection. If an upgrade
        // renames or removes it, this is null, every trigger falls back to bUnit's unsettled
        // behaviour, and SettledEventDispatchTests goes red saying so.
        private static readonly System.Reflection.MethodInfo? GetTestContext =
            typeof(MouseEventDispatchExtensions).Assembly.GetType("Bunit.AngleSharpExtensions")?
                .GetMethod("GetTestContext",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                    new[] { typeof(INode) });

        internal static Dispatcher? DispatcherOf(IElement element) =>
            (GetTestContext?.Invoke(null, new object[] { element }) as TestContextBase)?.Renderer.Dispatcher;

        private static void Settle(IElement element, Action trigger)
        {
            var dispatcher = DispatcherOf(element);
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                trigger();
                return;
            }
            dispatcher.InvokeAsync(trigger).GetAwaiter().GetResult();
        }

        public static void Click(this IElement element) =>
            Settle(element, () => MouseEventDispatchExtensions.Click(element));

        public static void Click(this IElement element, MouseEventArgs eventArgs) =>
            Settle(element, () => MouseEventDispatchExtensions.Click(element, eventArgs));

        public static void Change(this IElement element, ChangeEventArgs eventArgs) =>
            Settle(element, () => InputEventDispatchExtensions.Change(element, eventArgs));

        public static void Change<T>(this IElement element, T value) =>
            Settle(element, () => InputEventDispatchExtensions.Change(element, value));

        public static void Input(this IElement element) =>
            Settle(element, () => InputEventDispatchExtensions.Input(element));

        public static void Input(this IElement element, ChangeEventArgs eventArgs) =>
            Settle(element, () => InputEventDispatchExtensions.Input(element, eventArgs));

        public static void Input<T>(this IElement element, T value) =>
            Settle(element, () => InputEventDispatchExtensions.Input(element, value));

        public static void KeyDown(this IElement element, KeyboardEventArgs eventArgs) =>
            Settle(element, () => KeyboardEventDispatchExtensions.KeyDown(element, eventArgs));

        public static void KeyDown(this IElement element, Key key) =>
            Settle(element, () => KeyboardEventDispatchExtensions.KeyDown(element, key));

        public static void Blur(this IElement element) =>
            Settle(element, () => FocusEventDispatchExtensions.Blur(element));

        public static void Focus(this IElement element) =>
            Settle(element, () => FocusEventDispatchExtensions.Focus(element));

        public static void FocusIn(this IElement element) =>
            Settle(element, () => FocusEventDispatchExtensions.FocusIn(element));

        public static void Toggle(this IElement element) =>
            Settle(element, () => DetailsElementEventDispatchExtensions.Toggle(element));

        public static void TriggerEvent(this IElement element, string eventName, EventArgs eventArgs) =>
            Settle(element, () => TriggerEventDispatchExtensions.TriggerEvent(element, eventName, eventArgs));
    }
}

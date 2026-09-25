using System.Collections.Concurrent;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins <see cref="SettledEventDispatch"/>: with the renderer's dispatcher HELD, every trigger
    /// shape this project uses has run its handler by the time it returns. The hold is a gate
    /// rather than a sleep, so nothing here depends on how fast the machine is.
    /// </summary>
    public class SettledEventDispatchTests
    {
        private sealed class Probe : ComponentBase
        {
            public readonly ConcurrentQueue<string> Log = new();
            public readonly TaskCompletionSource Parked = new();

            protected override void BuildRenderTree(RenderTreeBuilder b)
            {
                Element(b, 0, "button", "click", "onclick");
                Element(b, 10, "input", "change", "onchange");
                Element(b, 20, "input", "input", "oninput");
                Element(b, 30, "div", "keydown", "onkeydown");
                Element(b, 40, "input", "blur", "onblur");
                Element(b, 50, "input", "focus", "onfocus");
                Element(b, 60, "div", "focusin", "onfocusin");
                Element(b, 70, "details", "toggle", "ontoggle");

                b.OpenElement(80, "button");
                b.AddAttribute(81, "id", "parked");
                b.AddAttribute(82, "onclick", EventCallback.Factory.Create(this, async () =>
                {
                    Log.Enqueue("parked:before");
                    await Parked.Task;
                    Log.Enqueue("parked:after");
                }));
                b.CloseElement();
            }

            private void Element(RenderTreeBuilder b, int seq, string tag, string id, string eventName)
            {
                b.OpenElement(seq, tag);
                b.AddAttribute(seq + 1, "id", id);
                b.AddAttribute(seq + 2, eventName, EventCallback.Factory.Create<EventArgs>(this, () => Log.Enqueue(id)));
                b.CloseElement();
            }
        }

        /// <summary>Occupies the dispatcher until <paramref name="release"/> is set. Returns once
        /// the hold is actually running, so the dispatcher is known to be busy.</summary>
        private static Task Hold(IRenderedFragment cut, ManualResetEventSlim release)
        {
            var entered = new ManualResetEventSlim();
            var hold = Task.Run(async () =>
            {
                var occupied = cut.InvokeAsync(() => { entered.Set(); release.Wait(); });
                await occupied;
            });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "The hold never reached the dispatcher.");
            return hold;
        }

        public static TheoryData<string> Shapes => new()
        {
            "Click()", "Click(MouseEventArgs)", "Change(ChangeEventArgs)", "Change<T>",
            "Input()", "Input(ChangeEventArgs)", "Input<T>", "KeyDown(KeyboardEventArgs)", "KeyDown(Key)",
            "Blur()", "Focus()", "FocusIn()", "Toggle()", "TriggerEvent",
        };

        private static (string Id, Action<IRenderedFragment> Fire) Shape(string name) => name switch
        {
            "Click()"                   => ("click",   c => c.Find("#click").Click()),
            "Click(MouseEventArgs)"     => ("click",   c => c.Find("#click").Click(new MouseEventArgs())),
            "Change(ChangeEventArgs)"   => ("change",  c => c.Find("#change").Change(new ChangeEventArgs { Value = "x" })),
            "Change<T>"                 => ("change",  c => c.Find("#change").Change("x")),
            "Input()"                   => ("input",   c => c.Find("#input").Input()),
            "Input(ChangeEventArgs)"    => ("input",   c => c.Find("#input").Input(new ChangeEventArgs { Value = "x" })),
            "Input<T>"                  => ("input",   c => c.Find("#input").Input("x")),
            "KeyDown(KeyboardEventArgs)"=> ("keydown", c => c.Find("#keydown").KeyDown(new KeyboardEventArgs { Key = "Enter" })),
            "KeyDown(Key)"              => ("keydown", c => c.Find("#keydown").KeyDown(Key.Enter)),
            "Blur()"                    => ("blur",    c => c.Find("#blur").Blur()),
            "Focus()"                   => ("focus",   c => c.Find("#focus").Focus()),
            "FocusIn()"                 => ("focusin", c => c.Find("#focusin").FocusIn()),
            "Toggle()"                  => ("toggle",  c => c.Find("#toggle").Toggle()),
            "TriggerEvent"              => ("toggle",  c => c.Find("#toggle").TriggerEvent("ontoggle", EventArgs.Empty)),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        [Theory]
        [MemberData(nameof(Shapes))]
        public void A_trigger_fired_while_the_renderer_is_busy_has_run_its_handler_when_it_returns(string shape)
        {
            using var ctx = new TestContext();
            var cut = ctx.RenderComponent<Probe>();
            var (id, fire) = Shape(shape);

            using var release = new ManualResetEventSlim();
            var hold = Hold(cut, release);
            // Released from another thread: the trigger blocks the test thread until its queued
            // item runs, and that cannot happen until the hold lets go.
            _ = Task.Delay(100).ContinueWith(_ => release.Set());

            fire(cut);

            Assert.Contains(id, cut.Instance.Log);
            hold.GetAwaiter().GetResult();
        }

        [Fact]
        public void The_route_to_the_renderer_resolves()
        {
            // Said by name, so a bUnit upgrade that moves GetTestContext fails here with the cause
            // rather than as fourteen unexplained theory cases.
            using var ctx = new TestContext();
            var cut = ctx.RenderComponent<Probe>();
            Assert.Same(ctx.Renderer.Dispatcher, SettledEventDispatch.DispatcherOf(cut.Find("#click")));
        }

        [Fact]
        public void Control_bUnits_own_trigger_under_the_same_hold_returns_before_its_handler_runs()
        {
            // Without this the theory above could pass because the hold does not actually hold,
            // and then it proves nothing. bUnit's raw trigger, called by its full name so it
            // cannot bind to the settled version, must come back with the handler still queued.
            using var ctx = new TestContext();
            var cut = ctx.RenderComponent<Probe>();

            using var release = new ManualResetEventSlim();
            var hold = Hold(cut, release);

            MouseEventDispatchExtensions.Click(cut.Find("#click"));
            Assert.Empty(cut.Instance.Log);

            release.Set();
            hold.GetAwaiter().GetResult();
            cut.WaitForAssertion(() => Assert.Contains("click", cut.Instance.Log));
        }

        [Fact]
        public void A_handler_parked_on_an_await_does_not_hang_the_trigger()
        {
            // The settled trigger waits for the handler to START, not to finish. A handler awaiting
            // a JS call nobody answers must still hand control back, as bUnit's own trigger did.
            using var ctx = new TestContext();
            var cut = ctx.RenderComponent<Probe>();

            cut.Find("#parked").Click();

            Assert.Equal(new[] { "parked:before" }, cut.Instance.Log);
            cut.Instance.Parked.SetResult();
            cut.WaitForAssertion(() => Assert.Contains("parked:after", cut.Instance.Log));
        }

        [Fact]
        public void Fired_from_inside_the_dispatcher_it_runs_inline_rather_than_deadlocking()
        {
            // 26 sites wrote the settle by hand: cut.InvokeAsync(() => x.Click()). Queuing and
            // waiting from there would wait on itself.
            using var ctx = new TestContext();
            var cut = ctx.RenderComponent<Probe>();

            var done = cut.InvokeAsync(() => cut.Find("#click").Click());

            Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "Click() inside InvokeAsync deadlocked.");
            Assert.Contains("click", cut.Instance.Log);
        }
    }
}

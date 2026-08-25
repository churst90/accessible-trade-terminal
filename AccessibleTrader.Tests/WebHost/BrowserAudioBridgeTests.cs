using System.Diagnostics;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Tests.Mocks;
using AccessibleTrader.WebHost.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost
{
    /// <summary>
    /// "Is sound actually coming out?" must be answerable, and answered out loud.
    ///
    /// Sonification is a primary data channel here, and every way the browser audio path
    /// could fail used to fail quietly: an AudioContext still behind the autoplay gate
    /// discards every chunk, a context that failed to construct makes audioPush a permanent
    /// no-op, and the bridge's own interop catch could not tell a closing circuit from a
    /// dead audio stack. audio.js exported an audioState() probe the entire time and
    /// nothing in the tree called it — so the one failure a blind user cannot self-diagnose
    /// was the one the app never mentioned.
    ///
    /// These tests drive the real component against a scripted audioState.
    /// </summary>
    public class BrowserAudioBridgeTests
    {
        private static (Bunit.TestContext ctx, WebHostBrowserAudioSink sink, SpyEventBus bus)
            Build(string audioState)
        {
            var ctx = new Bunit.TestContext();
            var sink = new WebHostBrowserAudioSink();
            var bus = new SpyEventBus();

            ctx.Services.AddSingleton(sink);
            ctx.Services.AddSingleton<IEventBus>(bus);

            // Loose so audioPush (void, per chunk) is a no-op; audioState is scripted.
            ctx.JSInterop.Mode = JSRuntimeMode.Loose;
            ctx.JSInterop.Setup<string>("accessibleTrader.audioState").SetResult(audioState);

            return (ctx, sink, bus);
        }

        /// <summary>
        /// The push is fire-and-forget off a synchronous Subject, so the probe and its
        /// announcement land after Publish returns. Poll rather than assert instantly —
        /// the same lesson the bUnit modal races taught on 2026-08-24.
        /// </summary>
        private static bool WaitFor(Func<bool> condition, int timeoutMs = 2000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        private static string[] SpokenErrors(SpyEventBus bus) =>
            bus.Log.OfType<FeedbackRequestEvent>()
               .Where(e => e.Type == FeedbackType.Error)
               .Select(e => e.Message ?? "")
               .ToArray();

        [Fact]
        public void ASuspendedAudioContext_TellsTheUserToPressAKey()
        {
            // The browser autoplay gate. audio.js resumes on the first keydown/click, so
            // this is entirely fixable BY THE USER — but only if they are told, and a
            // silently discarded chunk stream is indistinguishable from a quiet market.
            var (ctx, sink, bus) = Build("suspended");
            using (ctx)
            {
                ctx.RenderComponent<AccessibleTrader.WebHost.Components.BrowserAudioBridge>();
                sink.Publish(new byte[8]);

                Assert.True(WaitFor(() => SpokenErrors(bus).Length > 0),
                    "A suspended AudioContext was never announced. The user hears nothing and " +
                    "has no way to learn that a keypress would fix it.");
                Assert.Contains(SpokenErrors(bus), m => m.Contains("waiting for a keypress"));
            }
        }

        [Fact]
        public void AContextThatFailedToStart_SaysSoAndSaysSpeechStillWorks()
        {
            var (ctx, sink, bus) = Build("uninitialized");
            using (ctx)
            {
                ctx.RenderComponent<AccessibleTrader.WebHost.Components.BrowserAudioBridge>();
                sink.Publish(new byte[8]);

                Assert.True(WaitFor(() => SpokenErrors(bus).Length > 0));
                string msg = SpokenErrors(bus)[0];
                Assert.Contains("could not start", msg);
                // Naming the surviving channel matters: "audio is broken" plus silence
                // reads as "the app is broken".
                Assert.Contains("Speech still works", msg);
            }
        }

        [Fact]
        public void AHealthyContext_SaysNothingAtAll()
        {
            // The other half of the contract. If this goes red the terminal has started
            // narrating its own plumbing on every chart that works, which trains the user
            // to ignore the channel that carries real failures.
            var (ctx, sink, bus) = Build("running");
            using (ctx)
            {
                ctx.RenderComponent<AccessibleTrader.WebHost.Components.BrowserAudioBridge>();
                for (int i = 0; i < 20; i++) sink.Publish(new byte[8]);

                Assert.False(WaitFor(() => SpokenErrors(bus).Length > 0, timeoutMs: 300),
                    "A running AudioContext must be silent: " +
                    string.Join(" | ", SpokenErrors(bus)));
            }
        }

        [Fact]
        public void TheSameProblem_IsAnnouncedOnce_NotOnEveryChunk()
        {
            // Chunks arrive ~43x a second. Announcing per chunk would be unusable, and the
            // probe is deliberately throttled to make sure it cannot happen.
            var (ctx, sink, bus) = Build("suspended");
            using (ctx)
            {
                ctx.RenderComponent<AccessibleTrader.WebHost.Components.BrowserAudioBridge>();
                for (int i = 0; i < 50; i++) sink.Publish(new byte[8]);

                Assert.True(WaitFor(() => SpokenErrors(bus).Length > 0));
                Thread.Sleep(150);
                Assert.Single(SpokenErrors(bus));
            }
        }
    }
}

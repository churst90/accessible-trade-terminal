using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-08-27 MEDIUM batch: six places where something happened and the user was told
    /// nothing, or told the wrong thing with full confidence.
    ///
    /// <para>
    /// They are grouped because they share a failure shape rather than a call stack. In a
    /// terminal whose entire output is speech, "no announcement" is not a degraded experience,
    /// it is a false negative — indistinguishable from the event not having occurred. Every test
    /// here was written by reintroducing the original line and watching it go red.
    /// </para>
    /// </summary>
    public class SilentFailureBatchTests
    {
        // ── A repeated error is announced, not swallowed ───────────────────────

        [Fact]
        public void ASecondIdenticalError_InsideTheDedupWindow_IsStillAnnounced()
        {
            // Two orders rejected for the same reason three seconds apart used to produce ONE
            // announcement. The trader has no second channel on which to notice the second
            // rejection, so the suppressed one reads exactly like a success.
            var bus = new SpyEventBus();
            using var coord = new GlobalErrorCoordinator(bus, NullLogger<GlobalErrorCoordinator>.Instance,
                                                         Substitute.For<IAudioFeedbackRouter>());

            var ev = new AppErrorEvent(ErrorSeverity.High, ErrorCategory.UserActionable,
                                       "Insufficient margin", "Broker");
            bus.Publish(ev);
            bus.Publish(ev);

            var spoken = bus.Log.OfType<FeedbackRequestEvent>().ToList();
            Assert.Equal(2, spoken.Count);
        }

        [Fact]
        public void TheRepeatIsShorterThanTheFirst_SoAChatteringProviderDoesNotReadTheSameSentenceTwice()
        {
            // The dedup window has a real job: stop a flapping provider from reciting a long
            // message over and over. The fix keeps that job and drops only the silence — the
            // repeat says "again" rather than the full text.
            var bus = new SpyEventBus();
            using var coord = new GlobalErrorCoordinator(bus, NullLogger<GlobalErrorCoordinator>.Instance,
                                                         Substitute.For<IAudioFeedbackRouter>());

            var ev = new AppErrorEvent(ErrorSeverity.High, ErrorCategory.UserActionable,
                                       "Insufficient margin", "Broker");
            bus.Publish(ev);
            bus.Publish(ev);

            var spoken = bus.Log.OfType<FeedbackRequestEvent>().Select(f => f.Message).ToList();
            Assert.Contains("Insufficient margin", spoken[0]);
            Assert.DoesNotContain("Insufficient margin", spoken[1]);
            Assert.Contains("again", spoken[1], StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TwoDifferentErrors_BothReadInFull()
        {
            // Vacuity guard on the two tests above: "again" must be triggered by the message
            // REPEATING, not by there simply having been a previous error. A dedup key that
            // stopped discriminating would leave both tests above green and make every second
            // error in a session unreadable.
            var bus = new SpyEventBus();
            using var coord = new GlobalErrorCoordinator(bus, NullLogger<GlobalErrorCoordinator>.Instance,
                                                         Substitute.For<IAudioFeedbackRouter>());

            bus.Publish(new AppErrorEvent(ErrorSeverity.High, ErrorCategory.UserActionable, "Insufficient margin", "Broker"));
            bus.Publish(new AppErrorEvent(ErrorSeverity.High, ErrorCategory.UserActionable, "Symbol halted", "Broker"));

            var spoken = bus.Log.OfType<FeedbackRequestEvent>().Select(f => f.Message).ToList();
            Assert.Contains("Insufficient margin", spoken[0]);
            Assert.Contains("Symbol halted", spoken[1]);
        }

        // ── A Critical earcon is never throttled by a Low one ──────────────────

        [Fact]
        public void ACriticalErrorEarcon_PlaysEvenImmediatelyAfterALowOne()
        {
            // PlayError throttled on the bare key "error" for all four severities, so a Critical
            // arriving inside the 200 ms window of an unrelated Low played no tone at all — the
            // most important earcon in the app, silenced by the least important one.
            var sonify = Substitute.For<ISonificationManager>();
            sonify.IsEnabled.Returns(true);
            var lib = Substitute.For<ISoundPatchLibrary>();
            lib.EarconOverrides.Returns(new EarconSettings());
            var svc = new EarconService(sonify, lib);

            svc.PlayError(ErrorSeverity.Low);
            sonify.ClearReceivedCalls();

            svc.PlayError(ErrorSeverity.Critical);

            // PlayError renders a dissonant PAIR of notes, so this pins "it made a sound",
            // not a note count that would break the next time the patch is voiced differently.
            sonify.ReceivedWithAnyArgs().PlayNote(default, default, default!, default, default, default, default);
        }

        [Fact]
        public void TwoCriticalsBackToBack_BothPlay()
        {
            // High and Critical are exempt from the throttle entirely. A burst of criticals is
            // precisely the moment to keep making noise.
            var sonify = Substitute.For<ISonificationManager>();
            sonify.IsEnabled.Returns(true);
            var lib = Substitute.For<ISoundPatchLibrary>();
            lib.EarconOverrides.Returns(new EarconSettings());
            var svc = new EarconService(sonify, lib);

            svc.PlayError(ErrorSeverity.Critical);
            sonify.ClearReceivedCalls();
            svc.PlayError(ErrorSeverity.Critical);

            sonify.ReceivedWithAnyArgs().PlayNote(default, default, default!, default, default, default, default);
        }

        [Fact]
        public void TwoLowsBackToBack_AreStillThrottled()
        {
            // Vacuity guard: the throttle must still exist. Removing it wholesale would make
            // both tests above pass while letting a chattering Low spam the user continuously.
            var sonify = Substitute.For<ISonificationManager>();
            sonify.IsEnabled.Returns(true);
            var lib = Substitute.For<ISoundPatchLibrary>();
            lib.EarconOverrides.Returns(new EarconSettings());
            var svc = new EarconService(sonify, lib);

            svc.PlayError(ErrorSeverity.Low);
            sonify.ClearReceivedCalls();
            svc.PlayError(ErrorSeverity.Low);

            sonify.DidNotReceiveWithAnyArgs().PlayNote(default, default, default!, default, default, default, default);
        }

        // ── A rebind that could not be saved says so ───────────────────────────

        [Fact]
        public void AShortcutSaveThatFails_IsReportedRatherThanSilentlyDiscarded()
        {
            // UpdateBinding reports success (and even carefully reports displaced commands)
            // while the write is thrown away, so the rebind works all session and is gone on
            // restart. The failure went to Debug.WriteLine, which is compiled out of Release:
            // no announcement AND no diagnostic.
            string dir = TestTemp.NewDir("shortcut-save-fail");

            // A directory where the shortcuts file should be makes the atomic write fail.
            string blocked = Path.Combine(dir, "shortcuts.json");
            Directory.CreateDirectory(blocked);

            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(dir);

            var mgr = new ShortcutManager(paths, NullLogger<ShortcutManager>.Instance);
            mgr.UpdateBinding(SystemCommand.OpenHelp, "F9");

            Assert.False(mgr.LastSaveSucceeded);
        }

        [Fact]
        public void AShortcutSaveThatSucceeds_ReportsSuccess()
        {
            // Vacuity guard: LastSaveSucceeded must be able to be true, or the announcement it
            // gates would fire on every single rebind and quickly be tuned out.
            string dir = TestTemp.NewDir("shortcut-save-ok");
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(dir);

            var mgr = new ShortcutManager(paths, NullLogger<ShortcutManager>.Instance);
            mgr.UpdateBinding(SystemCommand.OpenHelp, "F9");

            Assert.True(mgr.LastSaveSucceeded);
            Assert.True(File.Exists(Path.Combine(dir, "shortcuts.json")));
        }

        // ── A profile with one NaN bin still names its structure ───────────────

        [Fact]
        public void ASingleNaNBin_DoesNotStripEveryStructuralLabelFromTheProfile()
        {
            // allBins.Sum(...) / count went NaN, `mean > 0` went false, and HVN / LVN /
            // ValueArea were all skipped — every bin classified Normal, whose label is "".
            // The user heard prices and volumes but never "Point of Control", "Value Area High"
            // or "Low Volume Node": the entire point of a volume profile, gone, with nothing
            // said to indicate the classifier had given up.
            var bins = WithNaNTail(Profile());

            // The two INTERIOR bins. The outermost value-area bins are returned as VAH and VAL
            // by an earlier branch, so testing those would prove nothing about the mean.
            Assert.Equal(ProfileNodeType.HVN, ProfileBinClassifier.Classify(bins[1], bins));
            Assert.Equal(ProfileNodeType.LVN, ProfileBinClassifier.Classify(bins[2], bins));
        }

        [Fact]
        public void WithNoNaNBins_TheSameProfileClassifiesIdentically()
        {
            // Vacuity guard: proves the NaN bin is what was doing the damage, not the fixture.
            // Same two bins, no NaN — the expected classifications are unchanged, so the test
            // above is measuring the NaN's effect and nothing else.
            var bins = Profile();

            Assert.Equal(ProfileNodeType.HVN, ProfileBinClassifier.Classify(bins[1], bins));
            Assert.Equal(ProfileNodeType.LVN, ProfileBinClassifier.Classify(bins[2], bins));
        }

        /// <summary>
        /// Four value-area bins. Mean volume 76.25, so bin 1 (200) is comfortably above the 1.3×
        /// HVN threshold and bin 2 (5) comfortably below the 0.4× LVN one. Bins 0 and 3 are the
        /// price extremes and are claimed by the VAL/VAH branch before the mean is ever reached.
        /// </summary>
        private static List<ProfileBin> Profile() => new()
        {
            Bin(100, 101, volume: 50,  isValueArea: true),
            Bin(101, 102, volume: 200, isValueArea: true),
            Bin(102, 103, volume: 5,   isValueArea: true),
            Bin(103, 104, volume: 50,  isValueArea: true),
        };

        /// <summary>Appends the one unmeasured bin that used to poison the whole profile.</summary>
        private static List<ProfileBin> WithNaNTail(List<ProfileBin> bins)
        {
            bins.Add(Bin(104, 105, volume: double.NaN, isValueArea: false));
            return bins;
        }

        private static ProfileBin Bin(double lo, double hi, double volume, bool isValueArea) => new()
        {
            PriceLow = lo,
            PriceHigh = hi,
            TotalVolume = volume,
            TpoPeriodCount = 1,
            IsPOC = false,
            IsValueArea = isValueArea,
        };
    }
}

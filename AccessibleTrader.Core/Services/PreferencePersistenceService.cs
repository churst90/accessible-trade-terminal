using System;
using System.Reactive.Linq;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IPreferencePersistenceService
    {
        /// <summary>Seeds the store from persisted settings. Call once at startup,
        /// before the first user interaction; arming the write-back subscription
        /// happens here too.</summary>
        void Initialize();
    }

    /// <summary>
    /// Debt item 3, stage (b): the global preferences that live on WorkspaceState —
    /// speech verbosity (timestamps, headers, order), new-bar announcements, WASAPI
    /// latency, panning granularity — were NEVER persisted: they reset to defaults on
    /// every launch, and only an explicit profile export carried some of them. This
    /// service makes settings.json their durable home while the store remains the
    /// hot-path, in-session view:
    ///
    ///   startup  → seed the store from IAppSettings (one UpdateSettingsAction)
    ///   any edit → whatever dispatched it (Settings dialog, Shift+[ granularity
    ///              keys, profile import), the change is observed on StateStream
    ///              and written back to IAppSettings, debounced 1 s per burst.
    ///
    /// Observing the store rather than instrumenting every writer means new setters
    /// added later persist automatically.
    ///
    /// DELIBERATELY EXCLUDED: IsSpeechEnabled / IsSonificationEnabled (F2/F3). A
    /// blind user who muted speech to take a call must never come back to a terminal
    /// that starts silent — those toggles are session-only by design.
    /// </summary>
    public sealed class PreferencePersistenceService : IPreferencePersistenceService, IDisposable
    {
        private readonly IWorkspaceStore _store;
        private readonly IAppSettings _settings;
        private readonly ILogger<PreferencePersistenceService> _logger;
        private IDisposable? _sub;

        public PreferencePersistenceService(IWorkspaceStore store, IAppSettings settings,
            ILogger<PreferencePersistenceService> logger)
        {
            _store = store;
            _settings = settings;
            _logger = logger;
        }

        /// <summary>
        /// The preferences that survive a restart.
        ///
        /// <para>
        /// <b>Adding a preference means touching four places</b> — this record, <see cref="FromState"/>,
        /// the seed dispatch, and the write-back — and nothing about the compiler makes you. That is
        /// how <c>DescribeChartPatterns</c> shipped-wired: the settings dialog put it in the store,
        /// so it worked perfectly all session, and it was silently dropped on exit because it never
        /// reached the file. The user's report was "I have to turn it on every time I open the app".
        /// </para>
        ///
        /// <para>
        /// <c>PreferenceRoundTripTests</c> now reflects over the properties that exist on BOTH
        /// <see cref="WorkspaceState"/> and <see cref="IAppSettings"/> and fails if any of them does
        /// not survive a save-and-reload, so the next omission is caught by the build rather than by
        /// a user noticing their setting reset.
        /// </para>
        /// </summary>
        private record struct Prefs(
            bool SpeakTimestamps, string TimestampReadLocation, bool ReadColumnHeaders,
            string SpeechOrder, bool AnnounceNewBars, int WasapiLatency, int PanningGranularity,
            bool DescribeChartPatterns);

        private static Prefs FromState(WorkspaceState s) => new(
            s.SpeakTimestamps, s.TimestampReadLocation, s.ReadColumnHeaders,
            s.SpeechOrder, s.AnnounceNewBars, s.WasapiLatency, s.PanningGranularity,
            s.DescribeChartPatterns);

        public void Initialize()
        {
            if (_sub != null) return; // idempotent

            // 1. Seed the store from persisted values (defaults match Initial, so a
            //    fresh install is a no-op dispatch).
            try
            {
                _store.Dispatch(new UpdateSettingsAction(s => s with
                {
                    SpeakTimestamps = _settings.SpeakTimestamps,
                    TimestampReadLocation = _settings.TimestampReadLocation,
                    ReadColumnHeaders = _settings.ReadColumnHeaders,
                    SpeechOrder = _settings.SpeechOrder,
                    AnnounceNewBars = _settings.AnnounceNewBars,
                    WasapiLatency = _settings.WasapiLatency,
                    PanningGranularity = _settings.PanningGranularity,
                    DescribeChartPatterns = _settings.DescribeChartPatterns,
                }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed preferences from settings; defaults in use.");
            }

            // 2. Write changes back. Skip(1) ignores the current (just-seeded) value;
            //    Throttle coalesces bursts (Shift+[ held down) into one file write.
            _sub = _store.StateStream
                .Select(FromState)
                .DistinctUntilChanged()
                .Skip(1)
                .Throttle(TimeSpan.FromSeconds(1))
                .Subscribe(p =>
                {
                    try
                    {
                        _settings.SpeakTimestamps = p.SpeakTimestamps;
                        _settings.TimestampReadLocation = p.TimestampReadLocation;
                        _settings.ReadColumnHeaders = p.ReadColumnHeaders;
                        _settings.SpeechOrder = p.SpeechOrder;
                        _settings.AnnounceNewBars = p.AnnounceNewBars;
                        _settings.WasapiLatency = p.WasapiLatency;
                        _settings.PanningGranularity = p.PanningGranularity;
                        _settings.DescribeChartPatterns = p.DescribeChartPatterns;
                        _settings.Save();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to persist preference change.");
                    }
                });
        }

        public void Dispose() => _sub?.Dispose();
    }
}

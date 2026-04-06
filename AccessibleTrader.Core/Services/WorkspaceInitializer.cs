using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Core.Services
{
    public interface IWorkspaceInitializer
    {
        void InitializeDefaultSeries();
        /// <summary>Saves the current series layout to the default workspace profile.</summary>
        void SaveWorkspace();
    }

    public class WorkspaceInitializer : IWorkspaceInitializer
    {
        private const string DefaultProfileName = "default";

        private readonly ISeriesManagementService _seriesService;
        private readonly IWorkspaceStore _store;
        private readonly IWorkspaceLibraryService _library;
        private readonly IIndicatorService _indicatorService;

        public WorkspaceInitializer(
            ISeriesManagementService seriesService,
            IWorkspaceStore store,
            IWorkspaceLibraryService library,
            IIndicatorService indicatorService)
        {
            _seriesService = seriesService;
            _store = store;
            _library = library;
            _indicatorService = indicatorService;
        }

        public void InitializeDefaultSeries()
        {
            if (_store.State.ActiveSeries.Any(s => s.Id == CoreSeriesIds.Candles)) return;

            // Try to restore the previously-saved workspace layout.
            var saved = _library.LoadProfile(DefaultProfileName);
            if (saved?.Series?.Count > 0)
            {
                // Migration: obtain current indicator metadata to merge any new cloud fills
                // and remove any stale Cloud-type components (now handled by CloudFills).
                var allMeta = _indicatorService.GetAvailableIndicators();

                foreach (var config in saved.Series)
                {
                    MigrateSeriesConfig(config, allMeta);
                    // Smart restore: rebuilds indicator series through the factory so current
                    // metadata defaults (waveforms, colors, envelope types) are always applied.
                    // Core series (Candles/Price/Volume/Heatmap) and unknown indicators fall
                    // back to direct config restore inside RestoreSeriesFromSaved.
                    var meta = allMeta.FirstOrDefault(m =>
                        m.Code.Equals(config.IndicatorCode, StringComparison.OrdinalIgnoreCase));
                    _seriesService.RestoreSeriesFromSaved(config, meta);
                }

                // Restore persisted pane height ratios if any were saved.
                var savedRatios = saved.PaneHeightRatios?.Count > 0
                    ? new Dictionary<string, float>(saved.PaneHeightRatios)
                    : new Dictionary<string, float>();

                // Set default pane height for Cipher B if not already saved.
                // 0.35 gives Cipher B a generous initial allocation so oscillators are clearly visible.
                if (saved.Series.Any(c => c.Pane == "Pane_CIPHER_B") && !savedRatios.ContainsKey("Pane_CIPHER_B"))
                    savedRatios["Pane_CIPHER_B"] = 0.35f;

                // Clamp any saved ratio that was dragged below a minimum viable height.
                // 0.15 (15% of total height) ensures every pane — including Cipher B — remains
                // readable. The overflow guard in ChartRenderer then rebalances if the total
                // exceeds the available canvas height.
                const float MinRatioFloor = 0.15f;
                foreach (var key in savedRatios.Keys.ToList())
                    savedRatios[key] = Math.Max(savedRatios[key], MinRatioFloor);

                if (savedRatios.Count > 0)
                    _store.Dispatch(new SetPaneHeightRatiosAction(savedRatios.ToImmutableDictionary()));
            }
            else
            {
                // No saved profile — create the defaults via metadata so the correct factory path
                // (CreateSeriesFromMetadata) is always used, applying Default* color/audio hints.
                var allMeta = _indicatorService.GetAvailableIndicators();
                var candlesMeta = allMeta.FirstOrDefault(m => m.Code.Equals("CANDLES", StringComparison.OrdinalIgnoreCase));
                var volumeMeta  = allMeta.FirstOrDefault(m => m.Code.Equals("VOLUME",  StringComparison.OrdinalIgnoreCase));
                var priceMeta   = allMeta.FirstOrDefault(m => m.Code.Equals("PRICE",   StringComparison.OrdinalIgnoreCase));

                if (candlesMeta != null) _seriesService.RegisterSeriesFromMetadata(candlesMeta);
                if (volumeMeta  != null) _seriesService.RegisterSeriesFromMetadata(volumeMeta);
                if (priceMeta   != null) _seriesService.RegisterSeriesFromMetadata(priceMeta);
                SaveWorkspace();
            }

            // Default focus to candles for immediate keyboard/speech feedback.
            _store.Dispatch(new SelectSeriesAction(CoreSeriesIds.Candles));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Series));
        }

        public void SaveWorkspace()
        {
            var state = _store.State;
            var config = new WorkspaceConfiguration
            {
                Series = state.ActiveSeries
                    .Select(s => s.Config)
                    .ToList(),
                PaneHeightRatios = state.PaneHeightRatios != null
                    ? new System.Collections.Generic.Dictionary<string, float>(state.PaneHeightRatios)
                    : new()
            };
            _library.SaveProfile(DefaultProfileName, config);
        }

        /// <summary>
        /// Migrates a saved <see cref="SeriesConfig"/> to the current metadata:
        /// <list type="bullet">
        ///   <item>Removes any Cloud-type components (now rendered via CloudFills).</item>
        ///   <item>Adds any CloudFill definitions that are in the current metadata but missing from the saved config.</item>
        /// </list>
        /// </summary>
        private static void MigrateSeriesConfig(SeriesConfig config, List<IndicatorMetadata> allMeta)
        {
            // Remove stale Cloud-type components (they are now handled by CloudFills).
            var cloudComps = config.Components
                .Where(c => c.DisplayType == ComponentDisplayType.Cloud)
                .ToList();
            foreach (var c in cloudComps) config.Components.Remove(c);

            // Merge missing cloud fills from current metadata defaults.
            var meta = allMeta.FirstOrDefault(m =>
                m.Code.Equals(config.IndicatorCode, StringComparison.OrdinalIgnoreCase));
            if (meta == null) return;

            foreach (var fill in meta.DefaultCloudFills)
            {
                bool exists = config.CloudFills.Any(f =>
                    f.UpperComponentName.Equals(fill.UpperComponentName, StringComparison.OrdinalIgnoreCase) &&
                    f.LowerComponentName.Equals(fill.LowerComponentName, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                    config.CloudFills.Add(fill.Clone());
            }

            // Merge missing zone bands from current metadata defaults.
            foreach (var band in meta.DefaultZoneBands)
            {
                bool exists = config.ZoneBands.Any(b =>
                    b.ComponentName.Equals(band.ComponentName, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                    config.ZoneBands.Add(band.Clone());
            }
        }
    }
}

using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Builds the minimum DI graph required to compute indicators and run a backtest with no
/// MAUI/Blazor UI, no plugin loader, no network for cross-series, and no event bus consumers.
///
/// What's WIRED:
///   - Every <see cref="IIndicatorProvider"/> in <c>AccessibleTrader.Core</c> (including the
///     four cross-series providers, which are kept registerable via the no-op cache stub).
///   - <see cref="IIndicatorService"/> + <see cref="IIndicatorEngine"/> for indicator math.
///   - <see cref="ISignalCatalog"/>, <see cref="IConditionEvaluator"/>, <see cref="IRiskPlanResolver"/>,
///     <see cref="IConfigurableStrategyFactory"/> for strategy construction from a StrategySpec.
///   - <see cref="IStrategyBacktester"/> with all optional services null (no profile replay, no MTF).
///   - <see cref="IEventBus"/>: real instance with zero subscribers — backtester sets
///     IsBacktesting=true so ConfigurableStrategy never publishes anyway.
///
/// What's STUBBED:
///   - <see cref="ICrossSeriesCache"/> → <see cref="NoOpCrossSeriesCache"/> returning empty data.
///     The four cross-series providers (FundingRate / OpenInterest / FearGreed / CrowdingIndex)
///     can be constructed and registered, but they will produce all-NaN component arrays at
///     calculation time. v11/v12 don't use them — when we tackle v9-style strategies that need
///     real funding/OI/FNG data, we'll replace this with a snapshot-backed implementation.
///
/// What's MISSING (deliberately):
///   - <see cref="IMultiTimeframeDataService"/>: passed as null to evaluator/factory/backtester.
///     HTF leaves (anything with a Timeframe value) will silently return false during evaluation.
///     Acceptable for v11/v12 — neither uses HTF.
///   - Level providers / <see cref="ILevelService"/>: null. Phase-4 stop/target sources
///     (BelowSupport, BelowKijun, NextResistance, etc.) won't resolve. Risk plans that use
///     ATR-multiple stops + R-multiple TPs (the v11 family) work fine without it.
///   - Profile services: null. No VPVR replay, no TPO replay. Not used by v11/v12.
/// </summary>
public sealed class LabHost
{
    public IServiceProvider Services { get; }

    private LabHost(IServiceProvider sp) { Services = sp; }

    public static LabHost Build(string? crossSeriesSnapshotDir = null)
    {
        var services = new ServiceCollection();

        // ── Logging ──────────────────────────────────────────────────────────
        // Several Core services (IndicatorService, etc.) now require ILogger<T>
        // after the 2026-04-10 logging overhaul. Register a minimal logger factory
        // so DI resolves their constructors. Lab harness logs nothing by default.
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // ── Cross-series cache ───────────────────────────────────────────────
        // Convention: if no explicit dir is supplied, look for xs_*.json files in
        // ./strategy-lab-data (the same directory snapshot/xs-snapshot writes to). This
        // means every command picks up the cross-series cache automatically once you've
        // run xs-snapshot once — no per-command flag needed. Fall back to no-op only if
        // neither the explicit dir nor the convention dir exists or has files.
        // Auto-resolve: prefer ./strategy-lab-data, fall back to ../strategy-lab-data so the
        // harness works whether it's invoked from the repo root or from inside the lab project.
        if (crossSeriesSnapshotDir == null)
        {
            if (Directory.Exists("strategy-lab-data") && Directory.EnumerateFiles("strategy-lab-data", "xs_*.json").Any())
                crossSeriesSnapshotDir = "strategy-lab-data";
            else if (Directory.Exists("../strategy-lab-data") && Directory.EnumerateFiles("../strategy-lab-data", "xs_*.json").Any())
                crossSeriesSnapshotDir = "../strategy-lab-data";
            else
                crossSeriesSnapshotDir = "strategy-lab-data";
        }
        bool hasXsFiles = Directory.Exists(crossSeriesSnapshotDir)
                       && Directory.EnumerateFiles(crossSeriesSnapshotDir, "xs_*.json").Any();
        if (hasXsFiles)
        {
            var cache = new SnapshottingCrossSeriesCache(crossSeriesSnapshotDir);
            services.AddSingleton<ICrossSeriesCache>(cache);
            Console.WriteLine($"Cross-series cache loaded from {crossSeriesSnapshotDir}:");
            foreach (var (key, count) in cache.AvailableKeys)
                Console.WriteLine($"  {key,-50} {count} points");
        }
        else
        {
            services.AddSingleton<ICrossSeriesCache, NoOpCrossSeriesCache>();
        }

        // ── Indicator providers ──────────────────────────────────────────────
        // Mirror of ServiceCollectionExtensions.AddIndicatorPipeline for the providers only.
        // Order doesn't matter — IIndicatorService aggregates by metadata Code at lookup time.
        services.AddSingleton<IIndicatorProvider, CoreIndicatorProvider>();
        services.AddSingleton<IIndicatorProvider, SkenderBoundedOscillatorProvider>();
        services.AddSingleton<IIndicatorProvider, SkenderZeroCrossProvider>();
        services.AddSingleton<IIndicatorProvider, SkenderBandProvider>();
        services.AddSingleton<IIndicatorProvider, SkenderTrendProvider>();
        services.AddSingleton<IIndicatorProvider, SkenderVolatilityProvider>();
        services.AddSingleton<IIndicatorProvider, SkenderVolumeProvider>();
        services.AddSingleton<IIndicatorProvider, ProfileIndicatorProvider>();
        services.AddSingleton<IIndicatorProvider, CipherBProvider>();
        services.AddSingleton<IIndicatorProvider, CipherAProvider>();
        services.AddSingleton<IIndicatorProvider, CipherSrProvider>();
        services.AddSingleton<IIndicatorProvider, MACloudProvider>();
        services.AddSingleton<IIndicatorProvider, SpiderLinesProvider>();
        services.AddSingleton<IIndicatorProvider, IchimokuProvider>();
        services.AddSingleton<IIndicatorProvider, CipherCProvider>();
        services.AddSingleton<IIndicatorProvider, CipherSProvider>();
        services.AddSingleton<IIndicatorProvider, LoukasCyclesProvider>();
        services.AddSingleton<IIndicatorProvider, FundingRateProvider>();
        services.AddSingleton<IIndicatorProvider, OpenInterestProvider>();
        services.AddSingleton<IIndicatorProvider, FearGreedProvider>();
        services.AddSingleton<IIndicatorProvider, CrowdingIndexProvider>();
        services.AddSingleton<IIndicatorProvider, RegimeProvider>();
        services.AddSingleton<IIndicatorProvider, VolRegimeProvider>();
        services.AddSingleton<IIndicatorProvider, CoinMetricsProvider>();
        services.AddSingleton<IIndicatorProvider, TopBottomDetectorProvider>();
        services.AddSingleton<IIndicatorProvider, AnchoredVwapProvider>();
        services.AddSingleton<IIndicatorProvider, HurstExponentProvider>();
        services.AddSingleton<IIndicatorProvider, PivotLevelsProvider>();
        services.AddSingleton<IIndicatorProvider, BtcStrengthProvider>();
        services.AddSingleton<IIndicatorProvider, BinanceVisionFundingProvider>();
        services.AddSingleton<IIndicatorProvider, BinanceVisionOiProvider>();
        services.AddSingleton<IIndicatorProvider, CftcCotProvider>();
        services.AddSingleton<IIndicatorProvider, PulseProvider>();

        // ── Indicator engine ─────────────────────────────────────────────────
        services.AddSingleton<ICustomIndicatorRegistry, CustomIndicatorRegistry>();
        services.AddSingleton<IIndicatorService, IndicatorService>();
        services.AddSingleton<IIndicatorEngine, IndicatorEngine>();

        // ── Strategy / signal pipeline ───────────────────────────────────────
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<ISignalCatalog, SignalCatalog>();
        services.AddSingleton<IConditionEvaluator>(sp =>
            new ConditionEvaluator(
                sp.GetRequiredService<ISignalCatalog>(),
                mtf: null,
                levels: null));
        services.AddSingleton<IRiskPlanResolver>(_ => new RiskPlanResolver(levels: null));
        services.AddSingleton<IConfigurableStrategyFactory>(sp =>
            new ConfigurableStrategyFactory(
                sp.GetRequiredService<IConditionEvaluator>(),
                sp.GetRequiredService<IRiskPlanResolver>(),
                sp.GetRequiredService<ISignalCatalog>(),
                sp.GetRequiredService<IEventBus>(),
                mtf: null));

        // Backtester — all optional services null. v11/v12 don't need profile replay or MTF.
        services.AddSingleton<IStrategyBacktester>(_ => new StrategyBacktester(
            profileService: null, profileCache: null, mtf: null));

        return new LabHost(services.BuildServiceProvider());
    }

    /// <summary>
    /// Lookup a built-in strategy spec by its id constant. Walks the public seed enumeration
    /// from <see cref="BuiltInStrategySeeds.GetAllSeeds"/>.
    /// </summary>
    public static StrategySpec? FindBuiltInSpec(string id)
    {
        foreach (var spec in BuiltInStrategySeeds.GetAllSeeds())
            if (spec.Id == id) return spec;
        return null;
    }
}

/// <summary>
/// Returns empty data for every cross-series request. Lets the four cross-series indicator
/// providers be constructed and registered without dragging in IDataOrchestrator and the
/// entire data pipeline. Indicators that call this will produce all-NaN component arrays —
/// fine for strategies that don't reference cross-series signals (v11, v12).
///
/// When v9-style cross-series strategies are needed, replace this with an implementation
/// that reads from on-disk snapshots written by SnapshotCommand.
/// </summary>
public sealed class NoOpCrossSeriesCache : ICrossSeriesCache
{
    public IReadOnlyList<(long Ts, double Value)> GetOrFetch(CrossSeriesRequest request)
        => Array.Empty<(long, double)>();
}

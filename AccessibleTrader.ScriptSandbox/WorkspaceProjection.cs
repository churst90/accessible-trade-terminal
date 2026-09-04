using System.Collections.Immutable;
using System.Text.Json;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.ScriptSandbox;

/// <summary>
/// The <see cref="WorkspaceState"/> a strategy sees inside the sandbox worker, and the wire form
/// that gets it there.
///
/// <para>
/// <c>OnBar</c>'s third argument is why moving strategies out of process is feature-sized where
/// moving indicators was not. An indicator's whole input is <c>Ohlcv[]</c> plus a
/// <c>Dictionary&lt;string,double&gt;</c>. A strategy's is a 49-property record holding the
/// chart's identity, its entire bar buffer, an <c>ImmutableList&lt;ChartSeries&gt;</c> of computed
/// indicator output, and a pile of UI and audio settings. Some of that a strategy genuinely reads
/// — the bars, the indicator components its conditions are built from, the symbol, whether this
/// is a backtest. Some of it is presentation the strategy has no business acting on, and one
/// field (<c>TabSnapshots</c>) carries every OTHER chart's whole series stack, which would put
/// unrelated symbols' data inside the sandbox for no reason at all.
/// </para>
///
/// <para>
/// <b>So this is a projection, and a hand-maintained projection silently goes stale.</b> Add a
/// property to <see cref="WorkspaceState"/> a year from now and the natural failure is that
/// strategies quietly see its default forever, with nothing red anywhere. The guard is
/// <see cref="Carried"/> / <see cref="NotCarried"/> plus the reflection test over them
/// (<c>WorkspaceProjectionTests</c>): a property that is on neither list fails the build's test
/// run, so growing the record forces a decision instead of taking a default.
/// </para>
///
/// <para>
/// What a strategy running out-of-process does NOT get, and would have got in-process: the host's
/// dependency-injection graph. <c>PluginHostServices</c> statics are unset in the worker and
/// Core's cached services (the strategy indicator cache, the profile cache) are empty there. A
/// script strategy that reads its inputs from <c>OnBar</c>'s arguments — which is every strategy
/// the templates teach — is unaffected; one that reaches for a host singleton gets null. That
/// boundary is the point of the exercise, not a defect of it.
/// </para>
/// </summary>
public static class WorkspaceProjection
{
    /// <summary>
    /// Every <see cref="WorkspaceState"/> property that crosses into the worker. The reflection
    /// guard checks this against the real record, so it is a claim the test suite enforces rather
    /// than a comment that ages.
    /// </summary>
    public static readonly IReadOnlyList<string> Carried = new[]
    {
        nameof(WorkspaceState.Identity),
        nameof(WorkspaceState.Data),
        nameof(WorkspaceState.ActiveSeries),
        nameof(WorkspaceState.FocusedSeriesIndex),
        nameof(WorkspaceState.FocusedSeriesId),
        nameof(WorkspaceState.FocusedComponentIndex),
        nameof(WorkspaceState.FocusedBinIndex),
        nameof(WorkspaceState.CurrentDataIndex),
        nameof(WorkspaceState.ViewportStartIndex),
        nameof(WorkspaceState.ViewportLength),
        nameof(WorkspaceState.ViewportRange),
        nameof(WorkspaceState.ChartVolume),
        nameof(WorkspaceState.PlaybackSpeed),
        nameof(WorkspaceState.PanningGranularity),
        nameof(WorkspaceState.LastInteractionContext),
        nameof(WorkspaceState.IsHeikinAshi),
        nameof(WorkspaceState.IsLogScale),
        nameof(WorkspaceState.BackgroundColor),
        nameof(WorkspaceState.SpeakTimestamps),
        nameof(WorkspaceState.TimestampReadLocation),
        nameof(WorkspaceState.ReadColumnHeaders),
        nameof(WorkspaceState.SpeechOrder),
        nameof(WorkspaceState.AnnounceNewBars),
        nameof(WorkspaceState.DescribeChartPatterns),
        nameof(WorkspaceState.RightMarginBars),
        nameof(WorkspaceState.IsSpeechEnabled),
        nameof(WorkspaceState.IsSonificationEnabled),
        nameof(WorkspaceState.IsEventSpeechEnabled),
        nameof(WorkspaceState.IsEarconsEnabled),
        nameof(WorkspaceState.Mode),
        nameof(WorkspaceState.SelectedMarketType),
        nameof(WorkspaceState.IsPlaying),
        nameof(WorkspaceState.IsPaused),
        nameof(WorkspaceState.PlaybackScope),
        nameof(WorkspaceState.ReadXAxisHeaders),
        nameof(WorkspaceState.WasapiLatency),
        nameof(WorkspaceState.InitStatus),
        nameof(WorkspaceState.DataStatus),
        nameof(WorkspaceState.IndicatorPaneScrollIndex),
        nameof(WorkspaceState.IsCoordinateEntryMode),
        nameof(WorkspaceState.PendingDrawingTool),
        nameof(WorkspaceState.CoordinateEntryAnchorCount),
        nameof(WorkspaceState.CoordinateEntryAnchor1Index),
        nameof(WorkspaceState.ActiveTabIndex),
        nameof(WorkspaceState.IsBacktesting),
        nameof(WorkspaceState.PrimarySeriesId),
        nameof(WorkspaceState.CurrentDataShape),
        nameof(WorkspaceState.SymbolDisplayName),
        nameof(WorkspaceState.IsReplaying),
    };

    /// <summary>
    /// Properties deliberately left behind, each with the reason it stays on the host side. A
    /// strategy reads these at <see cref="WorkspaceState.Initial"/>'s defaults inside the worker,
    /// which <c>WorkspaceProjectionTests</c> asserts rather than assumes.
    ///
    /// <list type="bullet">
    ///   <item><c>PaneRanges</c> — the y-axis extents the renderer computed for each pane. Pure
    ///   presentation; a decision that moved with the zoom level would be a bug.</item>
    ///   <item><c>PaneHeightRatios</c> — how tall the user dragged each pane. Same.</item>
    ///   <item><c>TabSnapshots</c> — the frozen state of every OTHER open tab, each carrying its
    ///   own full <c>Data</c> buffer and series stack. Sending it would put unrelated symbols'
    ///   history inside the sandbox to answer a question about this one, and would make the
    ///   per-bar payload scale with how many charts the user happens to have open.</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlyList<string> NotCarried = new[]
    {
        // When the last live tick arrived. Host-side by design: a strategy decides from BARS,
        // and a bar's own Date already says when it closed. Handing a script the wall-clock
        // freshness of the feed would invite exactly the kind of rule that behaves differently
        // in a backtest than it does live — the replay has no live feed at all.
        nameof(WorkspaceState.LastTickUtc),
        nameof(WorkspaceState.PaneRanges),
        nameof(WorkspaceState.PaneHeightRatios),
        nameof(WorkspaceState.TabSnapshots),

        // The two narration switches. AnnounceNewBars and DescribeChartPatterns ARE carried,
        // which makes this look inconsistent, so the reason is written down: those two predate
        // the projection and a strategy could at least argue it wants to know whether a bar
        // close was announced. These say whether the TERMINAL is talking — a strategy whose
        // signals differed depending on whether the user had playback narration on would be a
        // defect, not a feature, and it would differ between a live run and a backtest where
        // there is no speech channel at all.
        nameof(WorkspaceState.NarrateSignalsOnBarClose),
        nameof(WorkspaceState.NarrateDuringPlayback),
    };

    /// <summary>
    /// Computed properties — nothing to carry, because they are functions of what is already
    /// carried or of what is deliberately not. Listed so the census guard can tell "derived" from
    /// "someone forgot".
    /// </summary>
    public static readonly IReadOnlyList<string> Derived = new[]
    {
        nameof(WorkspaceState.TabCount),
    };

    // ObservableObject-derived config types round-trip through the default options. Cased
    // exactly as the CLR names them so a rename shows up as a dropped value in the round-trip
    // test rather than being papered over by case-insensitive matching.
    private static readonly JsonSerializerOptions ConfigJson = new()
    {
        IncludeFields = false,
        WriteIndented = false,
    };

    // ── WorkspaceState ────────────────────────────────────────────────────────────

    public static void Write(Stream s, WorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        WriteIdentity(s, state.Identity);
        WriteData(s, state.Data);
        WriteSeriesList(s, state.ActiveSeries);

        Wire.WriteI32(s, state.FocusedSeriesIndex);
        Wire.WriteNullableString(s, state.FocusedSeriesId);
        Wire.WriteI32(s, state.FocusedComponentIndex);
        Wire.WriteI32(s, state.FocusedBinIndex);
        Wire.WriteI32(s, state.CurrentDataIndex);
        Wire.WriteI32(s, state.ViewportStartIndex);
        Wire.WriteI32(s, state.ViewportLength);
        Wire.WriteF64(s, state.ViewportRange.Min);
        Wire.WriteF64(s, state.ViewportRange.Max);
        Wire.WriteF32(s, state.ChartVolume);
        Wire.WriteF32(s, state.PlaybackSpeed);
        Wire.WriteI32(s, state.PanningGranularity);
        Wire.WriteI32(s, (int)state.LastInteractionContext);
        Wire.WriteBool(s, state.IsHeikinAshi);
        Wire.WriteBool(s, state.IsLogScale);
        Wire.WriteString(s, state.BackgroundColor);
        Wire.WriteBool(s, state.SpeakTimestamps);
        Wire.WriteString(s, state.TimestampReadLocation);
        Wire.WriteBool(s, state.ReadColumnHeaders);
        Wire.WriteString(s, state.SpeechOrder);
        Wire.WriteBool(s, state.AnnounceNewBars);
        Wire.WriteBool(s, state.DescribeChartPatterns);
        Wire.WriteI32(s, state.RightMarginBars);
        Wire.WriteBool(s, state.IsSpeechEnabled);
        Wire.WriteBool(s, state.IsSonificationEnabled);
        Wire.WriteBool(s, state.IsEventSpeechEnabled);
        Wire.WriteBool(s, state.IsEarconsEnabled);
        Wire.WriteI32(s, (int)state.Mode);
        Wire.WriteI32(s, (int)state.SelectedMarketType);
        Wire.WriteBool(s, state.IsPlaying);
        Wire.WriteBool(s, state.IsPaused);
        Wire.WriteI32(s, (int)state.PlaybackScope);
        Wire.WriteString(s, state.ReadXAxisHeaders);
        Wire.WriteI32(s, state.WasapiLatency);
        Wire.WriteI32(s, (int)state.InitStatus);
        Wire.WriteI32(s, (int)state.DataStatus);
        Wire.WriteI32(s, state.IndicatorPaneScrollIndex);
        Wire.WriteBool(s, state.IsCoordinateEntryMode);
        Wire.WriteNullableI32(s, state.PendingDrawingTool is { } tool ? (int)tool : null);
        Wire.WriteI32(s, state.CoordinateEntryAnchorCount);
        Wire.WriteI32(s, state.CoordinateEntryAnchor1Index);
        Wire.WriteI32(s, state.ActiveTabIndex);
        Wire.WriteBool(s, state.IsBacktesting);
        Wire.WriteString(s, state.PrimarySeriesId);
        Wire.WriteI32(s, (int)state.CurrentDataShape);
        Wire.WriteString(s, state.SymbolDisplayName);
        Wire.WriteBool(s, state.IsReplaying);
    }

    public static WorkspaceState Read(ref WireReader r)
    {
        var identity = ReadIdentity(ref r);
        var data     = ReadData(ref r);
        var series   = ReadSeriesList(ref r);

        int focusedSeriesIndex    = r.ReadI32();
        string? focusedSeriesId   = r.ReadNullableString();
        int focusedComponentIndex = r.ReadI32();
        int focusedBinIndex       = r.ReadI32();
        int currentDataIndex      = r.ReadI32();
        int viewportStartIndex    = r.ReadI32();
        int viewportLength        = r.ReadI32();
        double rangeMin           = r.ReadF64();
        double rangeMax           = r.ReadF64();
        float chartVolume         = r.ReadF32();
        float playbackSpeed       = r.ReadF32();
        int panningGranularity    = r.ReadI32();
        var lastInteraction       = (InteractionContext)r.ReadI32();
        bool isHeikinAshi         = r.ReadBool();
        bool isLogScale           = r.ReadBool();
        string backgroundColor    = r.ReadString();
        bool speakTimestamps      = r.ReadBool();
        string timestampLocation  = r.ReadString();
        bool readColumnHeaders    = r.ReadBool();
        string speechOrder        = r.ReadString();
        bool announceNewBars      = r.ReadBool();
        bool describePatterns     = r.ReadBool();
        int rightMarginBars       = r.ReadI32();
        bool isSpeechEnabled      = r.ReadBool();
        bool isSonifyEnabled      = r.ReadBool();
        bool isEventSpeechEnabled = r.ReadBool();
        bool isEarconsEnabled     = r.ReadBool();
        var mode                  = (TerminalMode)r.ReadI32();
        var marketType            = (MarketType)r.ReadI32();
        bool isPlaying            = r.ReadBool();
        bool isPaused             = r.ReadBool();
        var playbackScope         = (PlaybackScope)r.ReadI32();
        string readXAxisHeaders   = r.ReadString();
        int wasapiLatency         = r.ReadI32();
        var initStatus            = (InitializationStatus)r.ReadI32();
        var dataStatus            = (DataStatus)r.ReadI32();
        int paneScrollIndex       = r.ReadI32();
        bool isCoordinateEntry    = r.ReadBool();
        int? pendingTool          = r.ReadNullableI32();
        int anchorCount           = r.ReadI32();
        int anchor1Index          = r.ReadI32();
        int activeTabIndex        = r.ReadI32();
        bool isBacktesting        = r.ReadBool();
        string primarySeriesId    = r.ReadString();
        var currentDataShape      = (ProviderDataShape)r.ReadI32();
        string symbolDisplayName  = r.ReadString();
        bool isReplaying          = r.ReadBool();

        // The not-carried three keep Initial's defaults. Stated here rather than left implicit
        // because "a strategy sees an empty PaneRanges in the worker" is a behaviour, not an
        // accident, and WorkspaceProjectionTests asserts it.
        return WorkspaceState.Initial with
        {
            Identity = identity,
            Data = data,
            ActiveSeries = series,
            FocusedSeriesIndex = focusedSeriesIndex,
            FocusedSeriesId = focusedSeriesId,
            FocusedComponentIndex = focusedComponentIndex,
            FocusedBinIndex = focusedBinIndex,
            CurrentDataIndex = currentDataIndex,
            ViewportStartIndex = viewportStartIndex,
            ViewportLength = viewportLength,
            ViewportRange = (rangeMin, rangeMax),
            ChartVolume = chartVolume,
            PlaybackSpeed = playbackSpeed,
            PanningGranularity = panningGranularity,
            LastInteractionContext = lastInteraction,
            IsHeikinAshi = isHeikinAshi,
            IsLogScale = isLogScale,
            BackgroundColor = backgroundColor,
            SpeakTimestamps = speakTimestamps,
            TimestampReadLocation = timestampLocation,
            ReadColumnHeaders = readColumnHeaders,
            SpeechOrder = speechOrder,
            AnnounceNewBars = announceNewBars,
            DescribeChartPatterns = describePatterns,
            RightMarginBars = rightMarginBars,
            IsSpeechEnabled = isSpeechEnabled,
            IsSonificationEnabled = isSonifyEnabled,
            IsEventSpeechEnabled = isEventSpeechEnabled,
            IsEarconsEnabled = isEarconsEnabled,
            Mode = mode,
            SelectedMarketType = marketType,
            IsPlaying = isPlaying,
            IsPaused = isPaused,
            PlaybackScope = playbackScope,
            ReadXAxisHeaders = readXAxisHeaders,
            WasapiLatency = wasapiLatency,
            InitStatus = initStatus,
            DataStatus = dataStatus,
            IndicatorPaneScrollIndex = paneScrollIndex,
            IsCoordinateEntryMode = isCoordinateEntry,
            PendingDrawingTool = pendingTool is { } t ? (DrawingType)t : null,
            CoordinateEntryAnchorCount = anchorCount,
            CoordinateEntryAnchor1Index = anchor1Index,
            ActiveTabIndex = activeTabIndex,
            IsBacktesting = isBacktesting,
            PrimarySeriesId = primarySeriesId,
            CurrentDataShape = currentDataShape,
            SymbolDisplayName = symbolDisplayName,
            IsReplaying = isReplaying,
        };
    }

    // ── ChartIdentity ─────────────────────────────────────────────────────────────

    private static void WriteIdentity(Stream s, ChartIdentity id)
    {
        Wire.WriteString(s, id.Market);
        Wire.WriteString(s, id.Provider);
        Wire.WriteString(s, id.Symbol);
        Wire.WriteString(s, id.Timeframe);
    }

    private static ChartIdentity ReadIdentity(ref WireReader r) =>
        new(r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString());

    // ── TimeSeriesBuffer<Ohlcv> ───────────────────────────────────────────────────

    private static void WriteData(Stream s, TimeSeriesBuffer<Ohlcv>? data)
    {
        int n = data?.Count ?? 0;
        Wire.WriteU32(s, (uint)n);
        for (int i = 0; i < n; i++) Wire.WriteOhlcv(s, data![i]);
    }

    private static TimeSeriesBuffer<Ohlcv> ReadData(ref WireReader r)
    {
        int n = Wire.CheckCount(r.ReadU32(), "WorkspaceState.Data");
        if (n == 0) return TimeSeriesBuffer<Ohlcv>.Empty;
        var bars = new Ohlcv[n];
        for (int i = 0; i < n; i++) bars[i] = r.ReadOhlcv();
        return new TimeSeriesBuffer<Ohlcv>(bars);
    }

    // ── ChartSeries ───────────────────────────────────────────────────────────────
    // Config goes as JSON — it is the same shape WorkspaceConfiguration already persists, so
    // it is serialisable by construction and stays that way for reasons that have nothing to
    // do with this file. The per-bar arrays go binary: a 5,000-bar component array is 40 KB of
    // doubles and ~120 KB of JSON text, and there are several of them per indicator.

    private static void WriteSeriesList(Stream s, ImmutableList<ChartSeries>? series)
    {
        int n = series?.Count ?? 0;
        Wire.WriteU32(s, (uint)n);
        for (int i = 0; i < n; i++) WriteSeries(s, series![i]);
    }

    private static ImmutableList<ChartSeries> ReadSeriesList(ref WireReader r)
    {
        int n = Wire.CheckCount(r.ReadU32(), "WorkspaceState.ActiveSeries");
        if (n == 0) return ImmutableList<ChartSeries>.Empty;
        var builder = ImmutableList.CreateBuilder<ChartSeries>();
        for (int i = 0; i < n; i++) builder.Add(ReadSeries(ref r));
        return builder.ToImmutable();
    }

    private static void WriteSeries(Stream s, ChartSeries series)
    {
        Wire.WriteString(s, JsonSerializer.Serialize(series.Config, ConfigJson));
        Wire.WriteNullableString(s, series.Drawing is null ? null : JsonSerializer.Serialize(series.Drawing, ConfigJson));
        Wire.WriteBool(s, series.IsProfile);
        Wire.WriteI32(s, series.FocusedBinIndex);
        Wire.WriteBool(s, series.RequiresFullRecalcOnTick);

        var data = series.Data;
        Wire.WriteString(s, data?.SeriesId ?? "");
        Wire.WriteNullableDate(s, data?.FirstBarDate);

        var components = data?.ComponentData ?? new Dictionary<string, double[]>();
        Wire.WriteU32(s, (uint)components.Count);
        foreach (var kv in components)
        {
            Wire.WriteString(s, kv.Key);
            Wire.WriteDoubleArray(s, kv.Value);
        }

        WriteProfileBins(s, data?.ProfileBins);

        var heatmap = data?.HeatmapData ?? new List<List<ProfileBin>>();
        Wire.WriteU32(s, (uint)heatmap.Count);
        foreach (var row in heatmap) WriteProfileBins(s, row);
    }

    private static ChartSeries ReadSeries(ref WireReader r)
    {
        var configJson = r.ReadString(Wire.MaxBlobBytes);
        var config = JsonSerializer.Deserialize<SeriesConfig>(configJson, ConfigJson) ?? new SeriesConfig();

        var drawingJson = r.ReadNullableString(Wire.MaxBlobBytes);
        var drawing = drawingJson is null ? null : JsonSerializer.Deserialize<DrawingData>(drawingJson, ConfigJson);

        bool isProfile = r.ReadBool();
        int focusedBin = r.ReadI32();
        bool requiresFullRecalc = r.ReadBool();

        var data = new SeriesDataBuffer
        {
            SeriesId = r.ReadString(),
            FirstBarDate = r.ReadNullableDate(),
        };

        int componentCount = Wire.CheckCount(r.ReadU32(), "SeriesDataBuffer.ComponentData");
        for (int i = 0; i < componentCount; i++)
        {
            var name = r.ReadString();
            data.ComponentData[name] = r.ReadDoubleArray("SeriesDataBuffer.ComponentData[i]");
        }

        data.ProfileBins = ReadProfileBins(ref r);

        int heatmapRows = Wire.CheckCount(r.ReadU32(), "SeriesDataBuffer.HeatmapData");
        for (int i = 0; i < heatmapRows; i++) data.HeatmapData.Add(ReadProfileBins(ref r));

        return new ChartSeries(config, data)
        {
            Drawing = drawing,
            IsProfile = isProfile,
            FocusedBinIndex = focusedBin,
            RequiresFullRecalcOnTick = requiresFullRecalc,
        };
    }

    private static void WriteProfileBins(Stream s, List<ProfileBin>? bins)
    {
        bins ??= new List<ProfileBin>();
        Wire.WriteU32(s, (uint)bins.Count);
        foreach (var b in bins)
        {
            Wire.WriteF64(s, b.PriceLow);
            Wire.WriteF64(s, b.PriceHigh);
            Wire.WriteF64(s, b.TotalVolume);
            Wire.WriteF64(s, b.TpoPeriodCount);
            Wire.WriteBool(s, b.IsPOC);
            Wire.WriteBool(s, b.IsValueArea);
            Wire.WriteNullableI32(s, b.TpoLetter is { } c ? c : null);
            Wire.WriteI32(s, b.TpoBinIndex);
            Wire.WriteBool(s, b.IsSinglePrint);
            var letters = b.TpoLetters ?? ImmutableList<char>.Empty;
            Wire.WriteU32(s, (uint)letters.Count);
            foreach (var ch in letters) Wire.WriteI32(s, ch);
        }
    }

    private static List<ProfileBin> ReadProfileBins(ref WireReader r)
    {
        int n = Wire.CheckCount(r.ReadU32(), "ProfileBins");
        var bins = new List<ProfileBin>(n);
        for (int i = 0; i < n; i++)
        {
            double priceLow  = r.ReadF64();
            double priceHigh = r.ReadF64();
            double volume    = r.ReadF64();
            double tpoCount  = r.ReadF64();
            bool isPoc       = r.ReadBool();
            bool isValueArea = r.ReadBool();
            int? letter      = r.ReadNullableI32();
            int binIndex     = r.ReadI32();
            bool singlePrint = r.ReadBool();

            int letterCount = Wire.CheckCount(r.ReadU32(), "ProfileBin.TpoLetters");
            var letters = ImmutableList.CreateBuilder<char>();
            for (int j = 0; j < letterCount; j++) letters.Add((char)r.ReadI32());

            bins.Add(new ProfileBin
            {
                PriceLow = priceLow,
                PriceHigh = priceHigh,
                TotalVolume = volume,
                TpoPeriodCount = tpoCount,
                IsPOC = isPoc,
                IsValueArea = isValueArea,
                TpoLetter = letter is { } l ? (char)l : null,
                TpoBinIndex = binIndex,
                IsSinglePrint = singlePrint,
                TpoLetters = letters.ToImmutable(),
            });
        }
        return bins;
    }
}

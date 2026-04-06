using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.AI;

/// <summary>
/// Orchestrates AI-powered chart analysis.
/// Priority: Claude → OpenAI → Ollama (first configured key wins).
/// Returns null when no key is found; throws on network/LLM failure.
/// </summary>
public sealed class AIAnalystService : IAIAnalystService
{
    private const string SystemPrompt =
        "You are an expert technical analyst for financial markets. " +
        "You will be given recent OHLCV candle data and a summary of active indicators. " +
        "You may also receive a chart screenshot. " +
        "Provide a concise, actionable technical analysis (3–5 bullet points) covering: " +
        "trend direction, key support/resistance levels, momentum signals, and a short-term outlook. " +
        "Write for a visually impaired trader who uses text-to-speech — be clear and use plain language.";

    private const int SnapshotWidth  = 800;
    private const int SnapshotHeight = 480;

    private readonly IApiKeyService      _apiKeyService;
    private readonly IWorkspaceStore     _store;
    private readonly ChartRenderer       _renderer;
    private readonly IEnumerable<ILLMProvider> _providers;

    public AIAnalystService(
        IApiKeyService apiKeyService,
        IWorkspaceStore store,
        ChartRenderer renderer,
        IEnumerable<ILLMProvider> providers)
    {
        _apiKeyService = apiKeyService;
        _store         = store;
        _renderer      = renderer;
        _providers     = providers;
    }

    public async Task<string?> AnalyseAsync(CancellationToken ct = default)
    {
        // ── 1. Find the first configured LLM key ─────────────────────────────────
        ILLMProvider? provider = null;
        string?       apiKey   = null;

        foreach (var p in _providers)
        {
            var cfg = await _apiKeyService.GetKeyForProviderAsync(p.ProviderId);
            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                provider = p;
                apiKey   = cfg.ApiKey;
                break;
            }
        }

        // NOTE: Ollama is treated the same as any other provider — the user must add an "Ollama"
        // entry in API Keys (the key field is used as the base URL, e.g. http://localhost:11434).
        // There is no automatic localhost fallback; requiring an explicit entry prevents surprise
        // connection attempts when Ollama isn't installed.
        if (provider == null)
            return null; // No configured LLM key — caller announces "no API key configured"

        // ── 2. Build OHLCV summary (viewport bars) ────────────────────────────────
        var state       = _store.State;
        var userMessage = BuildUserMessage(state);

        // ── 3. Capture offscreen chart screenshot ─────────────────────────────────
        string? imageBase64 = CaptureChartSnapshot(state);

        // ── 4. Call LLM ───────────────────────────────────────────────────────────
        return await provider.CompleteAsync(SystemPrompt, userMessage, imageBase64, apiKey!, ct);
    }

    // ── Prompt construction ───────────────────────────────────────────────────────

    private static string BuildUserMessage(WorkspaceState state)
    {
        var sb = new StringBuilder();

        var id = state.Identity;
        sb.AppendLine($"Symbol: {id.Symbol}  Provider: {id.Provider}  Timeframe: {id.Timeframe}");
        sb.AppendLine();

        // Viewport OHLCV rows (up to 50 most recent visible bars)
        if (state.Data != null && state.Data.Count > 0)
        {
            int start = Math.Max(0, state.ViewportStartIndex);
            int end   = Math.Min(state.Data.Count - 1, start + state.ViewportLength - 1);
            int from  = Math.Max(start, end - 49); // at most 50 rows

            sb.AppendLine("Recent OHLCV (Date, Open, High, Low, Close, Volume):");
            for (int i = from; i <= end; i++)
            {
                var bar = state.Data[i];
                sb.AppendLine($"  {bar.Date:yyyy-MM-dd HH:mm}  O={bar.Open:F2}  H={bar.High:F2}  L={bar.Low:F2}  C={bar.Close:F2}  V={bar.Volume:F0}");
            }
            sb.AppendLine();
        }

        // Active indicator summaries (name + last non-NaN values per component)
        var indicators = state.ActiveSeries
            .Where(s => !s.IsDrawing && !string.IsNullOrEmpty(s.Name))
            .ToList();

        if (indicators.Any())
        {
            sb.AppendLine("Active indicators (last bar values):");
            foreach (var series in indicators)
            {
                var comps = new List<string>();
                foreach (var comp in series.Components)
                {
                    var data = series.GetComponentData(comp.Name);
                    if (data.Length == 0) continue;
                    double last = double.NaN;
                    for (int i = data.Length - 1; i >= 0; i--)
                    {
                        if (!double.IsNaN(data[i])) { last = data[i]; break; }
                    }
                    if (!double.IsNaN(last))
                        comps.Add($"{comp.Name}={last:F4}");
                }
                if (comps.Any())
                    sb.AppendLine($"  {series.Name}: {string.Join("  ", comps)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Please provide your technical analysis:");
        return sb.ToString();
    }

    // ── Offscreen chart snapshot ──────────────────────────────────────────────────

    private string? CaptureChartSnapshot(WorkspaceState state)
    {
        if (state.Data == null || state.Data.Count == 0) return null;
        try
        {
            var info    = new SKImageInfo(SnapshotWidth, SnapshotHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface == null) return null;

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);

            _renderer.Render(
                canvas,
                SnapshotWidth,
                SnapshotHeight,
                state.Data,
                state.ActiveSeries,
                state.CurrentDataIndex,
                state.ViewportStartIndex,
                state.ViewportLength,
                state.ViewportRange,
                state.PaneRanges,
                state.IsHeikinAshi,
                state.IsLogScale,
                density: 1.0f,
                state.PaneHeightRatios,
                state.IndicatorPaneScrollIndex);

            using var image = surface.Snapshot();
            using var data  = image.Encode(SKEncodedImageFormat.Png, 85);
            return Convert.ToBase64String(data.ToArray());
        }
        catch
        {
            return null; // Non-fatal — analysis continues without screenshot
        }
    }
}

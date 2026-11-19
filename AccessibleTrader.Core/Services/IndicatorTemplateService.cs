using Newtonsoft.Json;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IIndicatorTemplateService
    {
        List<string> GetAvailable();
        void Save(string name, IndicatorTemplate template);
        IndicatorTemplate? Load(string name);
        void Delete(string name);

        /// <summary>
        /// The template the chart in front would save: every non-core, non-drawing indicator
        /// series with its settings, plus the tab's pane height ratios.
        /// </summary>
        IndicatorTemplate Capture(IWorkspaceStore store);
    }

    public class IndicatorTemplateService : IIndicatorTemplateService
    {
        private string _libraryDir;
        private readonly ILogger<IndicatorTemplateService> _logger;
        private readonly IWorkspaceLibraryService _workspaceLibrary;

        public IndicatorTemplateService(
            ILogger<IndicatorTemplateService> logger,
            IPlatformPathService paths,
            IWorkspaceLibraryService workspaceLibrary)
        {
            _logger = logger;
            _workspaceLibrary = workspaceLibrary;
            _libraryDir = Path.Combine(paths.AppDataDirectory, "IndicatorTemplates");
            Directory.CreateDirectory(_libraryDir);
        }

        /// <summary>Test seam, as <see cref="WorkspaceLibraryService.LibraryDirectoryOverride"/>.</summary>
        internal string LibraryDirectoryOverride
        {
            get => _libraryDir;
            set { _libraryDir = value; Directory.CreateDirectory(value); }
        }

        public List<string> GetAvailable()
        {
            try
            {
                return Directory.GetFiles(_libraryDir, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(x => x != null)
                    .Cast<string>()
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list indicator templates.");
                return new List<string>();
            }
        }

        public void Save(string name, IndicatorTemplate template)
        {
            var safe = ProfileNameGuard.Sanitize(name);
            if (safe == null)
            {
                _logger.LogWarning("Refused to save indicator template with unsafe name: {Name}", name);
                return;
            }
            try
            {
                string path = Path.Combine(_libraryDir, $"{safe}.json");
                string json = JsonConvert.SerializeObject(template, Formatting.Indented);
                AtomicFile.WriteAllText(path, json);
                _logger.LogInformation("Indicator template {Name} saved.", safe);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save indicator template {Name}.", safe);
            }
        }

        public IndicatorTemplate? Load(string name)
        {
            var safe = ProfileNameGuard.Sanitize(name);
            if (safe == null) return null;
            string path = Path.Combine(_libraryDir, $"{safe}.json");
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<IndicatorTemplate>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load indicator template {Name}.", safe);
                if (ex is JsonException)
                    CorruptFileQuarantine.MoveAside(path, ex);
                return null;
            }
        }

        public void Delete(string name)
        {
            var safe = ProfileNameGuard.Sanitize(name);
            if (safe == null) return;
            try
            {
                string path = Path.Combine(_libraryDir, $"{safe}.json");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete indicator template {Name}.", safe);
            }
        }

        public IndicatorTemplate Capture(IWorkspaceStore store)
        {
            var full = _workspaceLibrary.CaptureWorkspace(store);
            var tab = full.Tabs.Count == 0
                ? new TabConfiguration()
                : full.Tabs[Math.Clamp(full.ActiveTabIndex, 0, full.Tabs.Count - 1)];

            return new IndicatorTemplate
            {
                Series = tab.Series
                    .Where(IsTemplateSeries)
                    .Select(s => s.Clone())
                    .ToList(),
                PaneHeightRatios = tab.PaneHeightRatios != null
                    ? new Dictionary<string, float>(tab.PaneHeightRatios)
                    : new(),
                SavedFrom = WorkspaceLibraryService.ChartName(tab.Provider, tab.Symbol, tab.Timeframe),
                SavedUtc = DateTime.UtcNow,
            };
        }

        /// <summary>
        /// Core series belong to the chart's data shape, drawings to this symbol's prices, and a
        /// retired indicator has no provider behind it. None of them travel in a template.
        /// </summary>
        public static bool IsTemplateSeries(SeriesConfig series) =>
            !SeriesManagementService.IsCoreCode(series.IndicatorCode)
            && series.Drawing is not { Type: not DrawingType.None }
            && !Indicators.RetiredIndicators.IsRetired(series.IndicatorCode);

        /// <summary>
        /// What is said when a template goes on a chart. <paramref name="applied"/> is the number
        /// that actually went on (<see cref="IWorkspaceInitializer.ApplyIndicatorTemplate"/>'s
        /// return), not the template's size: a retired indicator in the template is skipped and
        /// named by <paramref name="notice"/>, and counting it as well would say "2 indicators"
        /// with one on the chart. The dialog and the Go bar both say this sentence.
        /// </summary>
        public static string AppliedSentence(string name, string chart, int applied, string? notice)
        {
            string sentence = $"Indicator template {name} applied to {chart}: "
                + $"{applied} {(applied == 1 ? "indicator" : "indicators")}.";
            return string.IsNullOrEmpty(notice) ? sentence : sentence + " " + notice;
        }

        /// <summary>
        /// "4 indicators: RSI 14, EMA 20, EMA 50, Cipher B; 2 panes" — names in series order,
        /// panes counted apart from Main.
        /// </summary>
        public static string Describe(IndicatorTemplate? template)
        {
            if (template == null) return "could not be read";

            var names = template.Series
                .Select(s => string.IsNullOrWhiteSpace(s.FriendlyName) ? s.Name : s.FriendlyName)
                .ToList();
            string count = names.Count == 1 ? "1 indicator" : $"{names.Count} indicators";
            string listed = names.Count == 0 ? "" : ": " + string.Join(", ", names);

            int panes = template.Series
                .Select(s => s.Pane)
                .Where(p => !string.IsNullOrWhiteSpace(p)
                         && !string.Equals(p, "Main", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Count();
            string paneClause = panes switch
            {
                0 => "",
                1 => "; 1 pane",
                _ => $"; {panes} panes",
            };

            return count + listed + paneClause;
        }
    }
}

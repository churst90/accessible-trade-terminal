// No `async void` in the component library.
//
// WHAT IS ACTUALLY WRONG WITH IT. An `async void` method has no Task for anyone to observe, so
// an exception thrown after its first await does not surface to the caller, to Blazor's error
// boundary, or to the circuit — it is raised on whatever context the continuation resumed on,
// which for a Blazor Server circuit means the thread pool. In this app that matters twice over:
// there is no compensating channel for a blind user, so an error nobody catches is an error
// nobody HEARS, and Blazor cannot re-render after a handler it was never given a Task to await.
//
// This is filed honestly as a robustness fix, NOT a demonstrated defect. Seven sites existed in
// the RCL on 2026-08-29 (ChartContextMenu ×3, DrawingContextMenu, SettingsModal ×2,
// VisualEarconOverlay) and none was shown to fault in practice: every one of them ends in a
// best-effort `focusElement` inside its own try/catch, which is the throw that would otherwise
// have gone nowhere. What the conversion removes is the path, not a reproduction. The stale
// finding that named an eighth site — TradingDashboardModal's `async void` timer callback — was
// struck the same day because the code it described no longer exists.
//
// THE CONVERSION IS SAFE AT EVERY CALL SITE and that is what made it worth doing at all. The
// callers are of exactly two kinds: `@onclick` / `@onchange` bindings, where Blazor prefers the
// Task-returning overload and now awaits the handler (so a fault reaches the error boundary and
// a re-render follows the handler instead of racing it), and `InvokeAsync(() => Handler(...))`
// inside an EventBus subscription, which binds to the Func<Task> overload and dispatches the
// same way. No site changed shape; only the return type did.
//
// THIS GUARD IS PROVEN BY REINTRODUCTION: turn any of the seven back into `async void` and it
// goes red naming the file and line.

using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests.Blazor;

public class AsyncVoidScanTests
{
    private static readonly Regex AsyncVoid =
        new(@"\basync\s+void\b", RegexOptions.Compiled);

    private static string ComponentsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "AccessibleTrader.BlazorClient.Components");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(ComponentsDir(), "*.razor", SearchOption.AllDirectories)
                 .Concat(Directory.EnumerateFiles(ComponentsDir(), "*.cs", SearchOption.AllDirectories))
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                          && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void No_component_declares_an_async_void_method()
    {
        var hits = new List<string>();
        foreach (var file in SourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (AsyncVoid.IsMatch(lines[i]))
                    hits.Add($"{Path.GetFileName(file)}:{i + 1}  {lines[i].Trim()}");
        }

        Assert.True(hits.Count == 0,
            "`async void` in the component library — an exception after the first await has "
          + "nowhere to surface, and Blazor has no Task to await before re-rendering. Return "
          + "Task instead; every call site in this project takes one (see the header of this "
          + "file for why each kind is safe):\n  " + string.Join("\n  ", hits));
    }

    [Fact]
    public void The_scan_reads_a_real_population_of_files()
    {
        // Vacuity floor. "No matches" is also what a wrong directory, a bad glob or an empty
        // enumeration returns, and this assertion carries no exemption list to notice that for
        // it. The RCL had 50+ .razor files on 2026-08-29; a count in single digits means the
        // scan lost its way, not that the library shrank.
        int files = SourceFiles().Count();
        Assert.True(files >= 40,
            $"The async-void scan found only {files} source files in the component library. "
          + "It is looking in the wrong place, so its clean result means nothing.");
    }
}

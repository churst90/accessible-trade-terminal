using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab.Catalogue;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// The catalogue's CLI surface: list what the lab holds and what the evidence is, and export
/// specs as a bundle file the terminal can import.
///
/// <para>
/// This is the only sanctioned route from research into a trading application. The terminal
/// starts with an empty library and imports nothing on its own, so a spec reaching a chart is
/// always the result of someone typing an export here and choosing a file there.
/// </para>
///
/// <code>
///   StrategyLab catalogue list [--status Untested|InSampleOnly|WalkForward|ControlTested|Fragile|Falsified] [--verbose]
///   StrategyLab catalogue export --out my-strategies.json [--id builtin.long.trend-baseline]... [--min-evidence WalkForward]
/// </code>
/// </summary>
public static class CatalogueCommand
{
    public static int Run(string[] args)
    {
        string sub = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "list";
        return sub switch
        {
            "list"   => List(args),
            "export" => Export(args),
            _        => Usage($"unknown catalogue subcommand '{sub}'"),
        };
    }

    private static int Usage(string? problem)
    {
        if (problem != null) Console.Error.WriteLine(problem);
        Console.WriteLine("Usage:");
        Console.WriteLine("  StrategyLab catalogue list [--status <level>] [--verbose]");
        Console.WriteLine("  StrategyLab catalogue export --out <file.json> [--id <spec-id>]... [--min-evidence <level>]");
        Console.WriteLine();
        Console.WriteLine("Evidence levels, weakest to strongest: Untested, InSampleOnly, WalkForward, ControlTested.");
        Console.WriteLine("Fragile and Falsified are separate outcomes, not points on that scale — a Falsified spec");
        Console.WriteLine("is never included by --min-evidence and must be named explicitly with --id.");
        return problem == null ? 0 : 2;
    }

    private static int List(string[] args)
    {
        bool verbose = args.Contains("--verbose");
        string? statusFilter = Flag(args, "--status");
        StrategyEvidenceLevel? want = null;
        if (statusFilter != null)
        {
            if (!Enum.TryParse<StrategyEvidenceLevel>(statusFilter, ignoreCase: true, out var parsed))
                return Usage($"unknown evidence level '{statusFilter}'");
            want = parsed;
        }

        var rows = CatalogueProvenance.SpecsWithProvenance()
            .Where(s => want == null || s.Provenance!.Evidence == want)
            .ToList();

        Console.WriteLine($"Catalogue version {StrategyCatalogue.Version} — {rows.Count} spec(s)");
        Console.WriteLine();

        foreach (var group in rows.GroupBy(s => s.Provenance!.Evidence).OrderBy(g => (int)g.Key))
        {
            Console.WriteLine($"── {group.Key} ({group.Count()}) {new string('─', Math.Max(0, 50 - group.Key.ToString().Length))}");
            foreach (var spec in group)
            {
                Console.WriteLine($"  {spec.Id}");
                Console.WriteLine($"      {spec.Name}  [{spec.Side}]");
                if (verbose)
                {
                    Console.WriteLine($"      tested:   {spec.Provenance!.TestedOn}");
                    Console.WriteLine($"      controls: {spec.Provenance!.Controls}");
                    Console.WriteLine($"      verdict:  {Wrap(spec.Provenance!.Verdict, 74, "                ")}");
                }
            }
            Console.WriteLine();
        }

        if (!verbose)
            Console.WriteLine("Add --verbose to see what each spec was tested on and the verdict.");
        return 0;
    }

    private static int Export(string[] args)
    {
        string? outPath = Flag(args, "--out");
        if (string.IsNullOrWhiteSpace(outPath)) return Usage("--out <file.json> is required");

        var ids = args.Select((a, i) => (a, i))
                      .Where(t => t.a == "--id" && t.i + 1 < args.Length)
                      .Select(t => args[t.i + 1])
                      .ToList();

        StrategyEvidenceLevel? min = null;
        string? minFlag = Flag(args, "--min-evidence");
        if (minFlag != null)
        {
            if (!Enum.TryParse<StrategyEvidenceLevel>(minFlag, ignoreCase: true, out var parsed))
                return Usage($"unknown evidence level '{minFlag}'");
            min = parsed;
        }

        var all = CatalogueProvenance.SpecsWithProvenance().ToList();
        List<StrategySpec> selected;

        if (ids.Count > 0)
        {
            var unknown = ids.Where(id => all.All(s => s.Id != id)).ToList();
            if (unknown.Count > 0)
            {
                Console.Error.WriteLine("Unknown spec id(s): " + string.Join(", ", unknown));
                Console.Error.WriteLine("Run `StrategyLab catalogue list` for the ids.");
                return 2;
            }
            selected = all.Where(s => ids.Contains(s.Id)).ToList();
        }
        else if (min != null)
        {
            // Fragile and Falsified are outcomes, not rungs — a bulk export by evidence level
            // must never sweep them in. Naming one with --id still works, on purpose.
            selected = all.Where(s =>
                s.Provenance!.Evidence != StrategyEvidenceLevel.Fragile &&
                s.Provenance!.Evidence != StrategyEvidenceLevel.Falsified &&
                (int)s.Provenance!.Evidence >= (int)min.Value).ToList();
        }
        else
        {
            return Usage("choose what to export: --id <spec-id> (repeatable) or --min-evidence <level>");
        }

        if (selected.Count == 0)
        {
            Console.Error.WriteLine("Nothing matched — no file written.");
            return 3;
        }

        string json = StrategyBundleService.Write(
            selected,
            source: "AccessibleTrader StrategyLab catalogue",
            catalogueVersion: StrategyCatalogue.Version,
            exportedUtc: DateTime.UtcNow);

        File.WriteAllText(outPath, json);

        Console.WriteLine($"Wrote {selected.Count} spec(s) to {Path.GetFullPath(outPath)}");
        foreach (var s in selected)
            Console.WriteLine($"  [{s.Provenance!.Evidence}] {s.Id} — {s.Name}");
        Console.WriteLine();
        Console.WriteLine("Import it in the terminal: Strategy modal → Library tab → Import strategies.");
        Console.WriteLine("Provenance travels with each spec; nothing is started by importing.");
        return 0;
    }

    private static string? Flag(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Wraps a verdict onto continuation lines so a long sentence stays readable in a terminal.</summary>
    private static string Wrap(string text, int width, string indent)
    {
        var words = text.Split(' ');
        var sb = new System.Text.StringBuilder();
        int line = 0;
        foreach (var w in words)
        {
            if (line > 0 && line + w.Length + 1 > width)
            {
                sb.Append('\n').Append(indent);
                line = 0;
            }
            else if (line > 0)
            {
                sb.Append(' ');
                line++;
            }
            sb.Append(w);
            line += w.Length;
        }
        return sb.ToString();
    }
}

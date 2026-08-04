using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab.Catalogue;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Reading and checking the edge registry from the command line.
///
/// <code>
///   StrategyLab edges list [--class crypto|equities|…] [--evidence ControlTested] [--family trend]
///   StrategyLab edges show &lt;edge-id&gt;
///   StrategyLab edges scorable [--class crypto]      # what may contribute to a score, and why
///   StrategyLab edges overlaps                       # pairs that must not be counted twice
///   StrategyLab edges stale [--days 180]             # what has not been re-measured lately
///   StrategyLab edges validate                       # structural check; exit 1 on problems
/// </code>
///
/// <para>
/// All output is plain text in reading order, because the primary consumer reads it with a screen
/// reader. Nothing here fetches data or runs a study — the registry is a record, and re-measurement
/// is a separate, deliberate act.
/// </para>
/// </summary>
public static class EdgesCommand
{
    public static int Run(string[] args)
    {
        string sub = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "list";

        EdgeRegistry registry;
        try
        {
            registry = EdgeRegistry.Load();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not load the edge registry: {ex.Message}");
            return 4;
        }

        return sub switch
        {
            "list"     => List(registry, args),
            "show"     => Show(registry, args),
            "scorable" => Scorable(registry, args),
            "overlaps" => Overlaps(registry),
            "breadth"  => Breadth(registry),
            "stale"    => Stale(registry, args),
            "validate" => Validate(registry),
            _          => Usage($"unknown edges subcommand '{sub}'"),
        };
    }

    private static int Usage(string? problem)
    {
        if (problem != null) Console.Error.WriteLine(problem);
        Console.WriteLine("Usage:");
        Console.WriteLine("  StrategyLab edges list [--class <asset-class>] [--evidence <level>] [--family <name>]");
        Console.WriteLine("  StrategyLab edges show <edge-id>");
        Console.WriteLine("  StrategyLab edges scorable [--class <asset-class>]");
        Console.WriteLine("  StrategyLab edges overlaps");
        Console.WriteLine("  StrategyLab edges breadth        # on how many instruments did each edge hold");
        Console.WriteLine("  StrategyLab edges stale [--days 180]");
        Console.WriteLine("  StrategyLab edges validate");
        Console.WriteLine();
        Console.WriteLine("An EDGE is a measured relationship. A STRATEGY is a bundle of them — see `catalogue`.");
        Console.WriteLine("Only ControlTested edges may contribute to a score.");
        return problem == null ? 0 : 2;
    }

    private static int List(EdgeRegistry reg, string[] args)
    {
        var rows = reg.Edges.AsEnumerable();

        string? cls = Flag(args, "--class");
        if (cls != null) rows = rows.Where(e => e.AppliesTo(cls));

        string? fam = Flag(args, "--family");
        if (fam != null) rows = rows.Where(e => string.Equals(e.Family, fam, StringComparison.OrdinalIgnoreCase));

        string? ev = Flag(args, "--evidence");
        if (ev != null)
        {
            if (!Enum.TryParse<StrategyEvidenceLevel>(ev, ignoreCase: true, out var level))
                return Usage($"unknown evidence level '{ev}'");
            rows = rows.Where(e => e.Evidence == level);
        }

        var list = rows.ToList();
        Console.WriteLine($"Edge registry {reg.RegistryVersion} — {list.Count} of {reg.Edges.Count} edge(s)");
        Console.WriteLine();

        foreach (var group in list.GroupBy(e => e.Evidence).OrderByDescending(g => (int)g.Key == 3).ThenBy(g => (int)g.Key))
        {
            Console.WriteLine($"── {group.Key} ({group.Count()})");
            foreach (var e in group)
            {
                Console.WriteLine($"  {e.Id}");
                Console.WriteLine($"      {e.Title}");
                Console.WriteLine($"      {string.Join(", ", e.Scope.AssetClasses)} · {e.Family} · last measured {e.LastMeasured}");
            }
            Console.WriteLine();
        }

        int scorable = list.Count(e => e.CanScore);
        Console.WriteLine($"{scorable} of these may contribute to a score. `edges show <id>` for the evidence.");
        return 0;
    }

    /// <summary>
    /// Every measured edge ranked by how widely it held.
    ///
    /// <para>
    /// The question this answers is the one a p-value cannot: <b>did it show up in more than one
    /// place?</b> A pooled result that is significant across thirty symbols but driven by two of
    /// them is one instrument's behaviour wearing a statistic. Ranking by share puts the edges that
    /// generalise at the top and makes the narrow ones impossible to mistake for broad ones.
    /// </para>
    ///
    /// <para>
    /// Narrow is not the same as bad. The signal-reversed exit held on 2 of 4 and is one of the
    /// better results here — because the two it held on are crypto and the two it failed on are
    /// equities, which is the asset-class fork rather than noise. The column exists to make that
    /// visible, not to penalise it.
    /// </para>
    /// </summary>
    private static int Breadth(EdgeRegistry reg)
    {
        var measured = reg.Edges
            .Where(e => e.Evidence != StrategyEvidenceLevel.Untested)
            .OrderByDescending(e => e.Breadth?.Share ?? -1)
            .ThenByDescending(e => e.Breadth?.Tested ?? 0)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("How widely did each measured edge actually hold?");
        Console.WriteLine();
        Console.WriteLine($"{"edge",-34}{"evidence",-16}{"held",8}{"share",8}");
        Console.WriteLine(new string('-', 68));

        foreach (var e in measured)
        {
            string held = e.Breadth?.Summary ?? "—";
            string share = e.Breadth?.Share is double s ? $"{s * 100:F0}%" : "—";
            Console.WriteLine($"{Trunc(e.Id, 33),-34}{e.Evidence,-16}{held,8}{share,8}");
        }

        int missing = measured.Count(e => e.Breadth == null);
        Console.WriteLine(new string('-', 68));
        Console.WriteLine($"{measured.Count} measured edges, {missing} with no breadth recorded.");
        Console.WriteLine();
        Console.WriteLine("A low share is not automatically a weakness — the signal-reversed exit held");
        Console.WriteLine("on 2 of 4 because the two it failed on were equities, which is the asset-class");
        Console.WriteLine("fork rather than noise. Read the note on each edge before judging the number.");
        Console.WriteLine();
        return 0;
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    private static int Show(EdgeRegistry reg, string[] args)
    {
        string? id = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        if (id == null) return Usage("edges show <edge-id>");

        var e = reg[id];
        if (e == null)
        {
            Console.Error.WriteLine($"No edge '{id}'. Run `edges list` for the ids.");
            return 2;
        }

        Console.WriteLine(e.Title);
        Console.WriteLine(new string('─', Math.Min(78, e.Title.Length)));
        Console.WriteLine($"id:        {e.Id}");
        Console.WriteLine($"claim:     {e.Claim}");
        Console.WriteLine($"family:    {e.Family}");
        Console.WriteLine($"evidence:  {e.Evidence}{(e.CanScore ? "  (may contribute to a score)" : "  (may NOT contribute to a score)")}");
        Console.WriteLine($"scope:     {string.Join(", ", e.Scope.AssetClasses)} · horizon {e.Scope.HorizonBars} bars · {e.Scope.Timeframe ?? "any timeframe"}"
                          + (e.Scope.UniverseMin is int u ? $" · needs {u}+ symbols" : ""));
        if (!string.IsNullOrWhiteSpace(e.Scope.Notes))
            Console.WriteLine($"           {e.Scope.Notes}");

        Console.WriteLine($"effect:    {Describe(e.Effect)}");
        if (!string.IsNullOrWhiteSpace(e.Effect.Notes))
            Console.WriteLine($"           {e.Effect.Notes}");

        // Breadth sits directly beneath the effect and above the controls, deliberately. A
        // p-value says "this pooled number is unlikely by chance"; breadth says "and it showed up
        // in more than one place" — which is the question that catches one symbol's behaviour
        // wearing a statistic, and this project has been caught by that shape before.
        if (e.Breadth is { } b)
        {
            Console.WriteLine($"breadth:   held on {b.Summary} instruments"
                            + (b.Share is double sh ? $" ({sh * 100:F0}%)" : ""));
            if (b.Instruments.Count > 0)
                Console.WriteLine($"           {string.Join(", ", b.Instruments)}");
            if (!string.IsNullOrWhiteSpace(b.Notes))
                Console.WriteLine($"           {b.Notes}");
        }
        else if (e.Evidence == StrategyEvidenceLevel.ControlTested)
        {
            Console.WriteLine("breadth:   NOT RECORDED — required for a scorable edge.");
        }

        Console.WriteLine("controls:");
        foreach (var c in e.Controls) Console.WriteLine($"           · {c}");

        if (e.CorrelatesWith.Count > 0)
        {
            Console.WriteLine("overlaps:");
            foreach (var l in e.CorrelatesWith)
                Console.WriteLine($"           · {l.Id}{(l.Correlation is double c ? $" (rho {c:0.00})" : "")} — {l.Note}");
        }

        if (e.Decay.Count > 0)
        {
            Console.WriteLine("decay:");
            foreach (var d in e.Decay)
                Console.WriteLine($"           · {d.AsOf}: {(d.Value is double v ? v.ToString("0.####") : "—")} {d.Note}");
        }

        Console.WriteLine($"measured:  first {e.FirstMeasured}, last {e.LastMeasured}");
        Console.WriteLine($"source:    {e.Source}");
        if (e.ReMeasure != null)
            Console.WriteLine($"re-measure: {e.ReMeasure.Command ?? "(none)"} — {(e.ReMeasure.Implemented ? "implemented" : "NOT yet a one-command re-run")}");
        return 0;
    }

    private static int Scorable(EdgeRegistry reg, string[] args)
    {
        string? cls = Flag(args, "--class");
        var rows = reg.Scorable.Where(e => cls == null || e.AppliesTo(cls)).ToList();

        Console.WriteLine(cls == null
            ? $"{rows.Count} edge(s) may contribute to a score."
            : $"{rows.Count} edge(s) may contribute to a score for {cls}.");
        Console.WriteLine("Only ControlTested qualifies — an unbeaten null is not evidence, and a fragile");
        Console.WriteLine("or falsified result is evidence against.");
        Console.WriteLine();

        foreach (var e in rows)
        {
            Console.WriteLine($"  {e.Id} — {e.Title}");
            Console.WriteLine($"      effect: {Describe(e.Effect)}");
            Console.WriteLine($"      beat:   {string.Join(", ", e.Controls)}");
            var overlaps = e.CorrelatesWith.Where(l => reg[l.Id]?.CanScore == true).Select(l => l.Id).ToList();
            if (overlaps.Count > 0)
                Console.WriteLine($"      NOTE:   overlaps another scorable edge ({string.Join(", ", overlaps)}) — do not count both at full weight.");
            Console.WriteLine();
        }
        return 0;
    }

    private static int Overlaps(EdgeRegistry reg)
    {
        var pairs = reg.KnownOverlaps().ToList();
        Console.WriteLine($"{pairs.Count} recorded overlap(s). Stacking a recorded pair measures one thing twice —");
        Console.WriteLine("which is the single most repeated failure in this project's strategy work.");
        Console.WriteLine();
        foreach (var (a, b, note) in pairs)
        {
            Console.WriteLine($"  {a.Id}  ×  {b.Id}");
            Console.WriteLine($"      {note}");
            Console.WriteLine($"      ({a.Evidence} × {b.Evidence})");
            Console.WriteLine();
        }
        return 0;
    }

    private static int Stale(EdgeRegistry reg, string[] args)
    {
        int days = int.TryParse(Flag(args, "--days"), out var d) ? d : 180;

        // Deliberately compared against the newest measurement in the registry rather than the
        // clock: staleness that matters is "this edge was not re-checked when everything else was",
        // and it keeps the command's output reproducible.
        var newest = reg.Edges
            .Select(e => DateTime.TryParse(e.LastMeasured, out var t) ? t : DateTime.MinValue)
            .DefaultIfEmpty(DateTime.MinValue).Max();

        var stale = reg.Edges
            .Select(e => (Edge: e, When: DateTime.TryParse(e.LastMeasured, out var t) ? t : DateTime.MinValue))
            .Where(x => (newest - x.When).TotalDays > days)
            .OrderBy(x => x.When)
            .ToList();

        Console.WriteLine($"Newest measurement in the registry: {newest:yyyy-MM-dd}");
        Console.WriteLine($"{stale.Count} edge(s) not re-measured within {days} days of it:");
        Console.WriteLine();
        foreach (var (e, when) in stale)
            Console.WriteLine($"  {when:yyyy-MM-dd}  {e.Id} ({e.Evidence}) — {e.Title}");

        if (stale.Count == 0) Console.WriteLine("  (none)");
        return 0;
    }

    private static int Validate(EdgeRegistry reg)
    {
        var problems = reg.Validate();
        if (problems.Count == 0)
        {
            Console.WriteLine($"Edge registry {reg.RegistryVersion}: {reg.Edges.Count} edges, structurally sound.");
            Console.WriteLine("(This checks the RECORD, not the science.)");
            return 0;
        }

        Console.Error.WriteLine($"{problems.Count} problem(s):");
        foreach (var p in problems) Console.Error.WriteLine($"  · {p}");
        return 1;
    }

    private static string Describe(EdgeEffect e)
    {
        string v = e.Value is double d ? d.ToString("0.####") : "—";
        string p = e.P is double pv ? $", p = {pv:0.####}" : "";
        string n = e.N is double nv ? $", n = {nv:0}" : "";
        return $"{e.Measure ?? "unspecified"} = {v} {e.Unit ?? ""}{p}{n}".Trim();
    }

    private static string? Flag(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

using AccessibleTrader.StrategyLab;

// Simple positional/flag dispatcher. No System.CommandLine dependency on purpose — keeps the
// project lean and the build matrix small. Each subcommand parses its own arguments.
//
// Subcommands:
//   snapshot  --symbol BTC/USDT --tf 4h --bars 3000 [--out strategy-lab-data]
//       Pulls historical bars from Bitstamp into a JSON snapshot file.
//
//   run       --snapshot <path> --spec <id> [--start 2024-06-01] [--end 2025-01-01] [--warmup 200]
//       Runs a built-in strategy spec against a snapshot and prints metrics + the CSV path.
//
// All paths are resolved relative to the current working directory.

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "snapshot"     => await HandleSnapshot(args.Skip(1).ToArray()),
        "aggregate"    => await HandleAggregate(args.Skip(1).ToArray()),
        "xs-snapshot"  => await HandleXsSnapshot(args.Skip(1).ToArray()),
        "run"          => await HandleRun(args.Skip(1).ToArray()),
        "walk"         => await HandleWalk(args.Skip(1).ToArray()),
        "walk-windows" => await HandleWalkWindows(args.Skip(1).ToArray()),
        "diagnostic"   => await HandleDiagnostic(args.Skip(1).ToArray()),
        "combo"        => await HandleCombo(args.Skip(1).ToArray()),
        "combo-sweep"  => await HandleComboSweep(args.Skip(1).ToArray()),
        "battery"         => await HandleBattery(args.Skip(1).ToArray()),
        "bnv-funding"  => await HandleBnvFunding(args.Skip(1).ToArray()),
        "bnv-oi"       => await HandleBnvOi(args.Skip(1).ToArray()),
        "cftc-cot"     => await HandleCftcCot(args.Skip(1).ToArray()),
        "coinmetrics"  => await HandleCoinMetrics(args.Skip(1).ToArray()),
        "profile"      => ProfileCommand.Run(GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data"),
        "rolling-window" => await HandleRollingWindow(args.Skip(1).ToArray()),
        "asset-profile" => await AssetProfileCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d"),
        "respect" => await RespectCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--surrogates"), out var sg) ? sg : 30),
        "origin" => await OriginLineCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--surrogates"), out var og) ? og : 30),
        "origin-oos" => await OriginLineCommand.RunHoldoutAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--surrogates"), out var oo) ? oo : 40,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--fit"), out var ff) ? ff : 0.6),
        "micro" => await MicroRicochetCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--csv") ?? "bnv1m",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "4h",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--perms"), out var pm) ? pm : 5000,
            DateTime.TryParse(GetFlag(args.Skip(1).ToArray(), "--from"), out var fr) ? fr : null,
            DateTime.TryParse(GetFlag(args.Skip(1).ToArray(), "--to"), out var tt) ? tt : null),
        "channel-prog" => await ChannelProgressionCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--surrogates"), out var cp) ? cp : 40),
        "confluence" => await ConfluenceCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--perms"), out var cf) ? cf : 5000,
            GetFlag(args.Skip(1).ToArray(), "--bull") ?? AccessibleTrader.Core.Services.Indicators.CipherBProvider.CompBlue,
            GetFlag(args.Skip(1).ToArray(), "--bear") ?? AccessibleTrader.Core.Services.Indicators.CipherBProvider.CompRed,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--srgate"), out var sg2) ? sg2 : 0.5),
        "favourability" => await FavourabilityCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--perms"), out var fv) ? fv : 5000,
            GetFlag(args.Skip(1).ToArray(), "--mode") ?? "price"),
        "cross" => await TradingCrossCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--window"), out var xw) ? xw : 200,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--entry"), out var xe) ? xe : 1.0,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--exit"), out var xx) ? xx : 0.0,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--surrogates"), out var xs) ? xs : 2000,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--cost"), out var xc) ? xc : 10.0),
        "exits" => ExitCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only") ?? "BTC_USDT",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var exp) ? exp : 2000),
        "walkfwd" => WalkForwardCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only") ?? "BTC_USDT",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--folds"), out var wf) ? wf : 6,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var wp) ? wp : 2000),
        "btcstrat" => BtcStrategyCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only") ?? "BTC_USDT",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var bsp) ? bsp : 2000),
        "fib" => FibCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only") ?? "BTC_USDT",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var fbp) ? fbp : 4000),
        "macro" => MacroEventCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var mep) ? mep : 2000),
        "fomc" => FomcCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var fp) ? fp : 2000),
        "events" => EventsCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--horizon"), out var eh) ? eh : 20,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var ep) ? ep : 4000),
        "onchain" => OnChainCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--horizon"), out var oh) ? oh : 20,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var op) ? op : 4000),
        "cycles" => CycleCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var cy) ? cy : 20000),
        "volume" => VolumeCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var vp) ? vp : 4000),
        "xsmom" => XsMomentumCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            GetFlag(args.Skip(1).ToArray(), "--universe") ?? "all",
            GetFlag(args.Skip(1).ToArray(), "--rank") ?? "raw",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var xm) ? xm : 20000),
        "crowding" => await CrowdingCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var cp) ? cp : 20000),
        "gate" => await GateCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var gp) ? gp : 20000,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--noise"), out var gn) ? gn : 0,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--noiseseed"), out var gs) ? gs : 0),
        "polarity" => PolarityCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var pp) ? pp : 20000),
        "poc-dev" => await PocDeviationCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--window"), out var pw) ? pw : 120,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--perms"), out var pp) ? pp : 5000,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--forward"), out var pf) ? pf : 20),
        "poc-tiers" => await PocTierCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--window"), out var tw) ? tw : 120,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--forward"), out var tfw) ? tfw : 5,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--cost"), out var tc) ? tc : 5.0,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--tiers"), out var tt) ? tt : 3,
            GetFlag(args.Skip(1).ToArray(), "--anchor") ?? "va",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--slowmult"), out var sm3) ? sm3 : 4),
        "probe" => await IndicatorProbeCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshot") ?? "../strategy-lab-data/bitstamp_BTC_USDT_1d.json",
            GetFlag(args.Skip(1).ToArray(), "--code") ?? "VALUE_DEVIATION",
            GetFlag(args.Skip(1).ToArray(), "--params")),
        "swing-trade" => await SwingTradeCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--span"), out var sp2) ? sp2 : 5,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--minatr"), out var ma2) ? ma2 : 1.0,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--cost"), out var ct2) ? ct2 : 5.0),
        "ml-export" => await MlExportCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "../strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            GetFlag(args.Skip(1).ToArray(), "--out") ?? "ml-data.csv"),
        "catalogue" or "catalog" => CatalogueCommand.Run(args.Skip(1).ToArray()),
        "edges" or "edge" => EdgesCommand.Run(args.Skip(1).ToArray()),
        "ladder" => LadderCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--clicks"), out var lc) ? lc : 3,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--trail"), out var lt) ? lt : 2,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--click-atr"), out var la) ? la : 0.5,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--random-trials"), out var lr) ? lr : 40),
        "earnings" => await EarningsCommand.RunAsync(args.Skip(1).ToArray(),
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data"),
        "sweep" => SweepReclaimCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d"),
        "mtf-size" => MtfSizingCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d"),
        "risk-metric" => RiskMetricCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--horizon"), out var rmh) ? rmh : 90),
        "targets" => TargetsCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d"),
        "approach" => ApproachCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d"),
        "weekly-persistence" => WeeklyPersistenceCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var wpp) ? wpp : 5000),
        "record-universe" => await UniverseRecorderCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--out") ?? "universe-archive",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--pages"), out var urp) ? urp : 4,
            HasFlag(args.Skip(1).ToArray(), "--force"),
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--delay-ms"), out var urd) ? urd : 3000),
        "universe-status" => UniverseRecorderCommand.Status(
            GetFlag(args.Skip(1).ToArray(), "--out") ?? "universe-archive"),
        "screen-crypto" => ScreenerCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--archive") ?? "universe-archive",
            GetFlag(args.Skip(1).ToArray(), "--date"),
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--top"), out var sct) ? sct : 1000,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--min-flags"), out var scf) ? scf : 3,
            HasFlag(args.Skip(1).ToArray(), "--show-clean"),
            GetFlag(args.Skip(1).ToArray(), "--only")),
        "record-gdelt" => await GdeltRecorderCommand.RunAsync(
            GetFlag(args.Skip(1).ToArray(), "--out") ?? "gdelt-archive",
            HasFlag(args.Skip(1).ToArray(), "--force"),
            GetFlag(args.Skip(1).ToArray(), "--timespan") ?? "3m"),
        "gdelt-status" => GdeltRecorderCommand.Status(
            GetFlag(args.Skip(1).ToArray(), "--out") ?? "gdelt-archive"),
        "grades" => await GradesCommand.RunAsync(args.Skip(1).ToArray(),
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data"),
        "translation" => TranslationCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--span"), out var trs) ? trs : 20,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--surrogates"), out var trg) ? trg : 400),
        "pattern-speech" => PatternSpeechCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d"),
        "late-session" => LateSessionCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var lsp) ? lsp : 20000),
        "pyramid" => PyramidCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--tf") ?? "1d",
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var pyp) ? pyp : 2000,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--slippage-bps"), out var pys) ? pys : 5.0,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--span"), out var pysp) ? pysp : 3,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--min-swing-atr"), out var pyms) ? pyms : 0.25,
            double.TryParse(GetFlag(args.Skip(1).ToArray(), "--exit-z"), out var pyez) ? pyez : 0.5),
        "regime-persistence" => RegimePersistenceCommand.Run(
            GetFlag(args.Skip(1).ToArray(), "--snapshots") ?? "strategy-lab-data",
            GetFlag(args.Skip(1).ToArray(), "--only"),
            GetFlag(args.Skip(1).ToArray(), "--recent-start"),
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--permutations"), out var rpp) ? rpp : 20000,
            int.TryParse(GetFlag(args.Skip(1).ToArray(), "--surrogates"), out var rps) ? rps : 200),
        "help" or "--help" or "-h" => PrintUsage(),
        _ => UnknownCommand(args[0])
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal: {ex.Message}");
    Console.Error.WriteLine(ex);
    return 99;
}

static int PrintUsage()
{
    Console.WriteLine("AccessibleTrader StrategyLab — headless research harness");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  StrategyLab snapshot --symbol BTC/USDT --tf 4h --bars 3000 [--out strategy-lab-data]");
    Console.WriteLine("  StrategyLab xs-snapshot [--out strategy-lab-data] [--points 3000]   # funding/OI/FNG");
    Console.WriteLine("  StrategyLab run  --snapshot <path> --spec <id> [--start yyyy-mm-dd] [--end yyyy-mm-dd] [--warmup 200] [--no-reverse]");
    Console.WriteLine("  StrategyLab walk --snapshot <path> --spec <id> [--warmup 200] [--no-reverse]");
    Console.WriteLine("  StrategyLab walk-windows --snapshot <path> --spec <id> [--windows 6] [--warmup 200]");
    Console.WriteLine("  StrategyLab diagnostic --snapshot <path> [--indicators CIPHER_A,CIPHER_B] [--warmup 200]");
    Console.WriteLine("  StrategyLab combo --snapshot <path> --entry <id> --filter <id> --filter-op <Op> --filter-value <num>");
    Console.WriteLine("  StrategyLab catalogue list [--status <level>] [--verbose]        # specs + what the evidence is");
    Console.WriteLine("  StrategyLab catalogue export --out <file.json> [--id <spec-id>]  # bundle for the terminal to import");
    Console.WriteLine("  StrategyLab edges list [--class crypto] [--evidence ControlTested]  # what we have MEASURED");
    Console.WriteLine("  StrategyLab edges show <edge-id> | scorable | overlaps | stale | validate");
    Console.WriteLine();
    Console.WriteLine("Forward recorders — run these on a schedule. Every day not recorded is");
    Console.WriteLine("permanently outside the sample, which is the one cost no later cleverness undoes:");
    Console.WriteLine("  StrategyLab record-universe [--out universe-archive]     # daily: crypto universe (survivorship)");
    Console.WriteLine("  StrategyLab universe-status                              # how close it is to answering anything");
    Console.WriteLine("  StrategyLab record-gdelt   [--timespan 24m]              # daily: news attention by theme");
    Console.WriteLine("  StrategyLab gdelt-status                                 # incl. whether GDELT restates history");
    Console.WriteLine("  StrategyLab grades record --key <fmp>                    # monthly: analyst rating mix");
    Console.WriteLine();
    Console.WriteLine("Screens and studies built on those archives:");
    Console.WriteLine("  StrategyLab screen-crypto [--min-flags 3] [--show-clean] [--only KAS,TAO]");
    Console.WriteLine("  StrategyLab grades fetch --key <fmp> | grades study [--horizon 21]");
    Console.WriteLine("  StrategyLab pattern-speech [--tf 1d]                     # chart-formation narration density");
    Console.WriteLine("  StrategyLab translation [--tf 1d] [--span 20]            # does a late cycle high mean a harder fall?");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  StrategyLab snapshot --symbol BTC/USDT --tf 4h --bars 3000");
    Console.WriteLine("  StrategyLab run --snapshot strategy-lab-data/bitstamp_BTC_USDT_1d.json --spec builtin.long.v16-trilogy");
    return 0;
}

static int UnknownCommand(string c)
{
    Console.Error.WriteLine($"Unknown command '{c}'. Try 'help'.");
    return 1;
}

static async Task<int> HandleAggregate(string[] a)
{
    string? src = GetFlag(a, "--src");
    int group = int.TryParse(GetFlag(a, "--group"), out var g) ? g : 7;
    string tf = GetFlag(a, "--tf") ?? "1w";
    if (src == null) { Console.Error.WriteLine("--src is required"); return 1; }
    return await SnapshotCommand.AggregateAsync(src, group, tf);
}

static async Task<int> HandleSnapshot(string[] a)
{
    string symbol = GetFlag(a, "--symbol") ?? "BTC/USDT";
    string tf     = GetFlag(a, "--tf") ?? "4h";
    int bars      = int.TryParse(GetFlag(a, "--bars"), out var b) ? b : 3000;
    string outDir = GetFlag(a, "--out") ?? "strategy-lab-data";
    string prov   = GetFlag(a, "--provider") ?? "bitstamp";
    string? key   = GetFlag(a, "--key");
    string? sec   = GetFlag(a, "--secret");
    Console.WriteLine($"snapshot: [{prov}] {symbol} {tf} target={bars} → {outDir}");
    return await SnapshotCommand.RunAsync(symbol, tf, bars, outDir, prov, key, sec);
}

static async Task<int> HandleRun(string[] a)
{
    string? snapshotPath = GetFlag(a, "--snapshot");
    string? specId       = GetFlag(a, "--spec");
    DateTime? start = ParseDate(GetFlag(a, "--start"));
    DateTime? end   = ParseDate(GetFlag(a, "--end"));
    int warmup = int.TryParse(GetFlag(a, "--warmup"), out var w) ? w : 200;
    bool noReverse = HasFlag(a, "--no-reverse");

    if (snapshotPath == null) { Console.Error.WriteLine("--snapshot is required"); return 1; }
    if (specId == null)       { Console.Error.WriteLine("--spec is required"); return 1; }

    return await RunCommand.RunAsync(snapshotPath, specId, start, end, warmup, noReverse);
}

static async Task<int> HandleWalk(string[] a)
{
    string? snapshotPath = GetFlag(a, "--snapshot");
    string? specId       = GetFlag(a, "--spec");
    int warmup = int.TryParse(GetFlag(a, "--warmup"), out var w) ? w : 200;
    bool noReverse = HasFlag(a, "--no-reverse");

    if (snapshotPath == null) { Console.Error.WriteLine("--snapshot is required"); return 1; }
    if (specId == null)       { Console.Error.WriteLine("--spec is required"); return 1; }

    return await RunCommand.WalkAsync(snapshotPath, specId, warmup, noReverse);
}

static async Task<int> HandleWalkWindows(string[] a)
{
    string? snapshotPath = GetFlag(a, "--snapshot");
    string? specId       = GetFlag(a, "--spec");
    int windows = int.TryParse(GetFlag(a, "--windows"), out var ws) ? ws : 6;
    int warmup  = int.TryParse(GetFlag(a, "--warmup"),  out var w)  ? w  : 200;
    bool noReverse = HasFlag(a, "--no-reverse");

    if (snapshotPath == null) { Console.Error.WriteLine("--snapshot is required"); return 1; }
    if (specId == null)       { Console.Error.WriteLine("--spec is required"); return 1; }

    return await WalkWindowsCommand.RunAsync(snapshotPath, specId, windows, warmup, noReverse);
}

static async Task<int> HandleXsSnapshot(string[] a)
{
    string outDir = GetFlag(a, "--out") ?? "strategy-lab-data";
    int target = int.TryParse(GetFlag(a, "--points"), out var p) ? p : 3000;
    return await CrossSeriesSnapshotCommand.RunAsync(outDir, target);
}

static async Task<int> HandleDiagnostic(string[] a)
{
    string? snapshotPath = GetFlag(a, "--snapshot");
    string? indicators   = GetFlag(a, "--indicators");
    int warmup = int.TryParse(GetFlag(a, "--warmup"), out var w) ? w : 200;

    if (snapshotPath == null) { Console.Error.WriteLine("--snapshot is required"); return 1; }

    var filter = string.IsNullOrWhiteSpace(indicators)
        ? new[] { "CIPHER_A", "CIPHER_B" }
        : indicators.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();

    var sideStr = GetFlag(a, "--side") ?? "long";
    var side = sideStr.Equals("short", StringComparison.OrdinalIgnoreCase)
        ? AccessibleTrader.Sdk.Plugins.OrderSide.Sell
        : AccessibleTrader.Sdk.Plugins.OrderSide.Buy;

    return await DiagnosticCommand.RunAsync(snapshotPath, filter, warmup, side);
}

static async Task<int> HandleCombo(string[] a)
{
    string? snapshotPath = GetFlag(a, "--snapshot");
    string? entry        = GetFlag(a, "--entry");
    string? filter       = GetFlag(a, "--filter");
    string? op           = GetFlag(a, "--filter-op");
    string? valStr       = GetFlag(a, "--filter-value");
    int warmup = int.TryParse(GetFlag(a, "--warmup"), out var w) ? w : 200;

    if (snapshotPath == null) { Console.Error.WriteLine("--snapshot is required"); return 1; }
    if (entry == null)        { Console.Error.WriteLine("--entry is required"); return 1; }
    if (filter == null)       { Console.Error.WriteLine("--filter is required"); return 1; }
    if (op == null)           { Console.Error.WriteLine("--filter-op is required (LessThan / GreaterThan / Fired / etc.)"); return 1; }
    if (!double.TryParse(valStr, out var val)) { Console.Error.WriteLine("--filter-value must be a number"); return 1; }

    return await ComboCommand.RunAsync(snapshotPath, entry, filter, op, val, warmup);
}

static async Task<int> HandleComboSweep(string[] a)
{
    string? snapshotPath = GetFlag(a, "--snapshot");
    string entryInds = GetFlag(a, "--entry-indicators") ?? "CIPHER_B";
    string filterId  = GetFlag(a, "--filter") ?? "CIPHER_B.Money Flow Wave";
    string opsCsv    = GetFlag(a, "--ops") ?? "GreaterThan,LessThan";
    string valsCsv   = GetFlag(a, "--values") ?? "-50,-25,0,25,50";
    int warmup       = int.TryParse(GetFlag(a, "--warmup"), out var w) ? w : 200;
    int minTrades    = int.TryParse(GetFlag(a, "--min-trades"), out var mt) ? mt : 10;
    bool baselineRel = !HasFlag(a, "--raw-thresholds");
    double? baselineOverride = double.TryParse(GetFlag(a, "--baseline"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bo) ? bo : (double?)null;

    if (snapshotPath == null) { Console.Error.WriteLine("--snapshot is required"); return 1; }

    var entryArr = entryInds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
    var ops = opsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => Enum.Parse<AccessibleTrader.Sdk.Strategies.LeafOperator>(s.Trim(), ignoreCase: true))
        .ToArray();
    var vals = valsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => double.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture))
        .ToArray();

    return await ComboSweepCommand.RunAsync(snapshotPath, entryArr, filterId, ops, vals, warmup, minTrades, baselineRel, baselineOverride);
}

static async Task<int> HandleBnvFunding(string[] a)
{
    string outDir = GetFlag(a, "--out") ?? "../strategy-lab-data";
    string symbolsCsv = GetFlag(a, "--symbols") ?? "BTCUSDT,ETHUSDT,XRPUSDT,SOLUSDT";
    var symbols = symbolsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
    return await BinanceVisionFundingCommand.RunAsync(outDir, symbols);
}

static async Task<int> HandleCoinMetrics(string[] a)
{
    string outDir = GetFlag(a, "--out") ?? "../strategy-lab-data";
    string assetsCsv = GetFlag(a, "--assets") ?? "btc,eth";
    string metricsCsv = GetFlag(a, "--metrics") ?? string.Join(",", CoinMetricsCommand.DefaultMetrics);
    string startDate = GetFlag(a, "--from") ?? "2015-01-01";
    var assets = assetsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
    var metrics = metricsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
    return await CoinMetricsCommand.RunAsync(outDir, assets, metrics, startDate);
}

static async Task<int> HandleCftcCot(string[] a)
{
    string outDir = GetFlag(a, "--out") ?? "../strategy-lab-data";
    string filtersCsv = GetFlag(a, "--contracts") ?? "BITCOIN,GOLD,WTI,E-MINI S&P 500,EURO FX";
    int? startYear = int.TryParse(GetFlag(a, "--from"), out var y) ? y : (int?)null;
    var filters = filtersCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
    return await CftcCotCommand.RunAsync(outDir, filters, startYear);
}

static async Task<int> HandleBnvOi(string[] a)
{
    string outDir = GetFlag(a, "--out") ?? "../strategy-lab-data";
    string symbolsCsv = GetFlag(a, "--symbols") ?? "BTCUSDT,ETHUSDT,XRPUSDT,SOLUSDT";
    var symbols = symbolsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
    return await BinanceVisionOiCommand.RunAsync(outDir, symbols);
}

static async Task<int> HandleBattery(string[] a)
{
    string? snap = GetFlag(a, "--snapshot");
    int warmup = int.TryParse(GetFlag(a, "--warmup"), out var w) ? w : 200;
    if (snap == null) { Console.Error.WriteLine("--snapshot is required"); return 1; }
    return await StrategyBatteryCommand.RunAsync(snap, warmup);
}

static async Task<int> HandleRollingWindow(string[] a)
{
    string? snap = GetFlag(a, "--snapshot");
    int window = int.TryParse(GetFlag(a, "--window"), out var win) ? win : 1500;
    int step   = int.TryParse(GetFlag(a, "--step"),   out var stp) ? stp : 250;
    int warmup = int.TryParse(GetFlag(a, "--warmup"), out var w)   ? w   : 200;
    string? filter = GetFlag(a, "--filter");
    var overrides = ParseSetOverrides(a);
    if (snap == null) { Console.Error.WriteLine("--snapshot is required"); return 1; }
    return await RollingWindowCommand.RunAsync(snap, window, step, filter, warmup, overrides);
}

// Parses repeatable `--set CODE.Param=Value` flags into per-indicator parameter
// overrides for WorkspaceFactory. Numeric values become double (what GetDbl/GetInt
// readers expect); everything else stays a string (e.g. ThresholdMode=Percentile).
// Example: --set CIPHER_B.ThresholdMode=Percentile --set CIPHER_B.AdaptiveLookback=250
static Dictionary<string, Dictionary<string, object>>? ParseSetOverrides(string[] a)
{
    Dictionary<string, Dictionary<string, object>>? result = null;
    for (int i = 0; i < a.Length - 1; i++)
    {
        if (!string.Equals(a[i], "--set", StringComparison.OrdinalIgnoreCase)) continue;
        string assignment = a[i + 1];
        int dot = assignment.IndexOf('.');
        int eq  = assignment.IndexOf('=');
        if (dot <= 0 || eq <= dot + 1)
        {
            Console.Error.WriteLine($"Ignoring malformed --set '{assignment}' (expected CODE.Param=Value)");
            continue;
        }
        string code  = assignment[..dot];
        string param = assignment[(dot + 1)..eq];
        string raw   = assignment[(eq + 1)..];
        object value = double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : raw;
        result ??= new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        if (!result.TryGetValue(code, out var map))
            result[code] = map = new Dictionary<string, object>();
        map[param] = value;
    }
    return result;
}

static bool HasFlag(string[] a, string flag)
{
    foreach (var s in a)
        if (string.Equals(s, flag, StringComparison.OrdinalIgnoreCase)) return true;
    return false;
}

static string? GetFlag(string[] a, string flag)
{
    for (int i = 0; i < a.Length - 1; i++)
        if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
    return null;
}

static DateTime? ParseDate(string? s)
{
    if (string.IsNullOrWhiteSpace(s)) return null;
    if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var d))
        return d;
    return null;
}

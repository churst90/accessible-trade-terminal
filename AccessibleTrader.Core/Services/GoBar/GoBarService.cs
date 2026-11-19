using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Core.Services.Screening;
using AccessibleTrader.Core.Services.Settings;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Screening;

namespace AccessibleTrader.Core.Services.GoBar
{
    /// <summary>
    /// Alt+G's two jobs: say what the typed text could mean, and run the chosen option after
    /// the box has closed (or say why it cannot).
    ///
    /// <para><b>Closed, then dispatched.</b> The dispatcher drops every command except a short
    /// allow-list while any modal is open, so a Go bar that dispatched from inside its own box
    /// would be silently swallowed. <see cref="Run"/> asks the box to close and watches the one
    /// ordered <see cref="ModalStack"/>; the option is dispatched or run only on the change that
    /// leaves the stack empty. If another dialog is still underneath, nothing runs and its name
    /// is said.</para>
    ///
    /// <para><b>Stage 6b: options, not only entries.</b> A typed verb with a value resolves to
    /// an action the Go bar runs itself. The four commands that had no handler at all — TF,
    /// SYM/GP, PROV and REF — are now four of those actions, so the registry's
    /// <see cref="GoBarEntry.HandledByGoBar"/> flag is honest.</para>
    ///
    /// <para><b>Every path speaks its outcome</b> on <see cref="SpeechChannel.Interface"/>,
    /// except a chart load whose own path already says what went wrong, a bar move whose bar
    /// read is the answer, and the alerts form which speaks itself.</para>
    /// </summary>
    public sealed class GoBarService : IDisposable
    {
        private readonly ICommandDispatcher _dispatcher;
        private readonly IShortcutManager _shortcuts;
        private readonly IEventBus _bus;
        private readonly ModalStack _modalStack;
        private readonly IWorkspaceStore? _store;
        private readonly IMarketOrchestrator? _orchestrator;
        private readonly IWatchlistLibrary? _watchlists;
        private readonly IIndicatorService? _indicators;
        private readonly ISeriesManagementService? _series;
        private readonly DemoPolicy? _demo;
        private readonly MarketEventNavigator? _marketEvents;
        private readonly IMarketEventBarMapCache? _eventMap;
        private readonly IChartPatternCache? _patterns;
        private readonly ISdkCandlePatternAnalyzer? _candles;
        private readonly TimeZoneInfo? _displayZone;
        private readonly IRuntimePlatform? _platform;
        private readonly IBrowserSpeechOutput? _browserSpeech;
        private readonly IIndicatorTemplateService? _templates;
        private readonly IWorkspaceInitializer? _workspaceInit;
        private GoBarOption? _pending;

        public GoBarService(
            ICommandDispatcher dispatcher,
            IShortcutManager shortcuts,
            IEventBus bus,
            ModalStack modalStack,
            IWorkspaceStore? store = null,
            IMarketOrchestrator? orchestrator = null,
            IWatchlistLibrary? watchlists = null,
            IIndicatorService? indicators = null,
            ISeriesManagementService? series = null,
            DemoPolicy? demo = null,
            MarketEventNavigator? marketEvents = null,
            IMarketEventBarMapCache? eventMap = null,
            IChartPatternCache? patterns = null,
            ISdkCandlePatternAnalyzer? candles = null,
            TimeZoneInfo? displayZone = null,
            IRuntimePlatform? platform = null,
            IBrowserSpeechOutput? browserSpeech = null,
            IIndicatorTemplateService? templates = null,
            IWorkspaceInitializer? workspaceInit = null)
        {
            _dispatcher = dispatcher;
            _shortcuts = shortcuts;
            _bus = bus;
            _modalStack = modalStack;
            _store = store;
            _orchestrator = orchestrator;
            _watchlists = watchlists;
            _indicators = indicators;
            _series = series;
            _demo = demo;
            _marketEvents = marketEvents;
            _eventMap = eventMap;
            _patterns = patterns;
            _candles = candles;
            _displayZone = displayZone;
            _platform = platform;
            _browserSpeech = browserSpeech;
            _templates = templates;
            _workspaceInit = workspaceInit;
            _modalStack.Changed += OnModalStackChanged;
        }

        // ── What the typed text can mean ───────────────────────────────────────

        /// <summary>
        /// The options for what has been typed, plus the one sentence that is read. Stage 6c
        /// adds three closed forms: a typed money command, <c>SET &lt;text&gt;</c> (settings
        /// only) and <c>HELP &lt;text&gt;</c> (words, never an execution).
        /// </summary>
        public GoBarSuggestion Suggest(string input)
        {
            string typed = (input ?? "").Trim();
            if (typed.Length == 0)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(), string.Empty);

            var parsed = GoBarParser.Parse(typed);

            // A command typed by its exact name/code always keeps its own meaning, even when
            // that word is also one of stage 6c's closed-form verbs ("Open Help", "Set the stop
            // from this bar", "Buy order", "Flat the position"). This is 6b's review lesson:
            // a new merged form must not hide an existing command's own words.
            if (parsed.Kind is GoBarParseKind.Money
                || (parsed.Kind == GoBarParseKind.Invalid
                    && parsed.Verb is GoBarVerb.Buy or GoBarVerb.Sell or GoBarVerb.Flat)
                || (parsed.Kind == GoBarParseKind.Argument
                    && parsed.Verb is GoBarVerb.Settings or GoBarVerb.Help))
            {
                var exact = GoBarMatcher.Match(typed, GoBarRegistry.All)
                    .Where(m => m.How is GoBarMatchKind.ExactCode
                        or GoBarMatchKind.ExactName or GoBarMatchKind.ExactAlias)
                    .ToList();
                if (exact.Count > 0)
                {
                    var moneyEntry = exact.Select(m => m.Entry)
                        .FirstOrDefault(e => e.Command is SystemCommand.TypedBuy
                            or SystemCommand.TypedSell or SystemCommand.TypedFlat);
                    if (moneyEntry != null)
                    {
                        var moneyOption = parsed.Kind == GoBarParseKind.Money
                            ? MoneyOptions(parsed, moneyEntry).Single()
                            : MoneyOptionForEntry(moneyEntry);
                        return new GoBarSuggestion(new[] { moneyOption }, moneyOption.Name + ".");
                    }

                    return Finish(exact.Select(m => EntryOption(m.Entry)).ToList());
                }
            }

            if (parsed.Kind == GoBarParseKind.Money)
                return ResolveMoney(parsed);

            if (parsed.Kind == GoBarParseKind.Invalid
                && parsed.Verb is GoBarVerb.Buy or GoBarVerb.Sell or GoBarVerb.Flat)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(), parsed.Refusal ?? "");

            if (parsed.Kind == GoBarParseKind.Invalid
                && string.Equals(parsed.Refusal, GoBarParser.MoneyOutOfOrder, StringComparison.Ordinal))
                return new GoBarSuggestion(Array.Empty<GoBarOption>(), GoBarParser.MoneyOutOfOrder);

            if (parsed.Kind == GoBarParseKind.Argument && parsed.Verb == GoBarVerb.Settings)
                return ResolveSettings(parsed.Argument);

            if (parsed.Kind == GoBarParseKind.Argument && parsed.Verb == GoBarVerb.Help)
                return ResolveHelp(parsed.Argument);

            if (parsed.Kind is GoBarParseKind.Invalid or GoBarParseKind.Argument)
                return MergeWithCommands(typed, parsed);

            if (parsed.Kind == GoBarParseKind.BareVerb)
                return ResolveBareVerb(parsed);

            // 6a commands first, then settings, then the two bare shapes that cannot mean
            // anything else. Settings never lead a plain search: a command's own words must
            // keep their command on top.
            var options = GoBarMatcher.Match(typed, GoBarRegistry.All)
                .Select(m => EntryOption(m.Entry))
                .ToList();
            options.AddRange(SettingOptions(typed));

            if (parsed.Kind == GoBarParseKind.BareTimeframe
                && parsed.Timeframe is { } tf && parsed.TimeframeWords is { } words)
            {
                options.Add(TimeframeOption(tf, words));
            }
            else if (parsed.Kind == GoBarParseKind.BareDate && parsed.Moment is { } moment)
            {
                options.Add(DateOption(moment, parsed.MomentHasTime, typed));
            }

            if (options.Count == 0)
                return new GoBarSuggestion(options, $"Nothing matches {typed}.");

            return new GoBarSuggestion(options, SentenceFor(options));
        }

        // ── Settings by name (stage 6c) ────────────────────────────────────────

        private SettingsHost Host => SettingsHost.From(_demo, _platform, _browserSpeech);

        private IEnumerable<GoBarOption> SettingOptions(string typed)
            => GoBarSettingsSearch.Find(typed, Host).Select(m => SettingOption(m.Entry));

        private static GoBarOption SettingOption(SettingEntry entry) =>
            new(entry.Label,
                Action: new GoBarSettingAction(entry),
                CanonicalText: $"SET {entry.Label}",
                Help: $"setting, {entry.Tab} tab");

        private GoBarSuggestion ResolveSettings(string argument)
        {
            var matches = GoBarSettingsSearch.Find(argument, Host);
            if (matches.Count == 0)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(),
                    $"No setting matches {argument}.");

            return Finish(matches.Select(m => SettingOption(m.Entry)).ToList());
        }

        // ── HELP (stage 6c) ────────────────────────────────────────────────────

        private GoBarSuggestion ResolveHelp(string argument)
        {
            var options = new List<GoBarOption>();
            foreach (var match in GoBarMatcher.Match(argument, GoBarRegistry.All))
                options.Add(HelpOption(CommandHelpSentence(match.Entry)));

            foreach (var match in GoBarSettingsSearch.Find(argument, Host))
            {
                options.Add(HelpOption(
                    $"{match.Entry.Label}, a setting on the {match.Entry.Tab} tab. "
                    + $"Type SET {match.Entry.Label} to go there."));
            }

            if (options.Count == 0)
                return new GoBarSuggestion(options,
                    $"No command or setting matches {argument}.");

            return new GoBarSuggestion(options, SentenceFor(options));
        }

        private static GoBarOption HelpOption(string sentence) =>
            new(sentence, Action: new GoBarHelpAction(sentence));

        private string CommandHelpSentence(GoBarEntry entry)
        {
            var parts = new List<string> { entry.Name + "." };
            if (entry.Codes.Count > 0)
                parts.Add($"Codes {string.Join(", ", entry.Codes)}.");
            if (KeyLabel(entry.Command) is { } key)
                parts.Add($"Key {key}.");

            string help = entry.Help.Trim();
            if (help.Length > 0)
                parts.Add(help.EndsWith('.') ? help : help + ".");

            return string.Join(" ", parts);
        }

        // ── Typed money (stage 6c) ─────────────────────────────────────────────

        private static GoBarSuggestion ResolveMoney(GoBarParseResult parsed)
        {
            string side = parsed.Verb == GoBarVerb.Sell ? "Sell" : "Buy";
            var entry = GoBarRegistry.All.FirstOrDefault(e => e.Command
                == (parsed.Flat ? SystemCommand.TypedFlat
                    : parsed.Verb == GoBarVerb.Sell ? SystemCommand.TypedSell
                    : SystemCommand.TypedBuy));
            var option = MoneyOptions(parsed, entry).Single();
            return new GoBarSuggestion(new[] { option }, option.Name + ".");
        }

        private static IReadOnlyList<GoBarOption> MoneyOptions(GoBarParseResult parsed, GoBarEntry? entry)
        {
            if (parsed.Flat)
                return new[]
                {
                    MoneyOptionForEntry(entry ?? GoBarRegistry.All.Single(
                        e => e.Command == SystemCommand.TypedFlat), parsed),
                };

            string side = parsed.Verb == GoBarVerb.Sell ? "Sell" : "Buy";
            string name;
            string canonical;

            if (parsed.Quantity is { } quantity)
            {
                string price = parsed.LimitPrice is { } limit
                    ? "at limit " + PlainNumber(limit)
                    : "at market";
                name = $"{side} {PlainNumber(quantity)} {price}";
                canonical = parsed.LimitPrice is { } canonicalLimit
                    ? $"{side.ToUpperInvariant()} {PlainNumber(quantity)} {PlainNumber(canonicalLimit)}"
                    : $"{side.ToUpperInvariant()} {PlainNumber(quantity)}";
            }
            else
            {
                name = $"{side}, fill in the ticket";
                canonical = side.ToUpperInvariant();
            }

            return new[]
            {
                new GoBarOption(
                    name,
                    Entry: entry,
                    Action: new GoBarMoneyAction(
                        side, parsed.Quantity, parsed.LimitPrice, parsed.Market, Flat: false),
                    CanonicalText: canonical,
                    Help: "Fill in the ticket; nothing is sent until Submit"),
            };
        }

        /// <summary>The command's own name typed exactly ("Buy order", "Flat the position").</summary>
        private static GoBarOption MoneyOptionForEntry(GoBarEntry entry, GoBarParseResult? parsed = null)
        {
            if (entry.Command == SystemCommand.TypedFlat)
            {
                return new GoBarOption(
                    "Find the chart's open position",
                    Entry: entry,
                    Action: new GoBarMoneyAction("", null, null, Market: false, Flat: true),
                    CanonicalText: "FLAT",
                    Help: "Find the open position and focus it; nothing is closed");
            }

            string side = entry.Command == SystemCommand.TypedSell ? "Sell" : "Buy";
            return new GoBarOption(
                parsed?.Quantity is { } quantity
                    ? $"{side} {PlainNumber(quantity)} "
                      + (parsed.LimitPrice is { } limit ? "at limit " + PlainNumber(limit) : "at market")
                    : $"{side}, fill in the ticket",
                Entry: entry,
                Action: new GoBarMoneyAction(
                    side, parsed?.Quantity, parsed?.LimitPrice, parsed?.Market ?? false, Flat: false),
                CanonicalText: side.ToUpperInvariant(),
                Help: "Fill in the ticket; nothing is sent until Submit");
        }

        private static string PlainNumber(double value) =>
            value.ToString("0.##########", CultureInfo.InvariantCulture);

        /// <summary>
        /// A verb word inside a command's own name ("add tab", "Change the timeframe",
        /// "focus the chart", "alert manager") must not hide that command. The 6a matches are
        /// always kept: a command typed by its exact code, name or alias comes first and the
        /// argument options follow; otherwise the argument options lead and the commands
        /// follow. A refusal is only the answer when no command matches either.
        /// </summary>
        private GoBarSuggestion MergeWithCommands(string typed, GoBarParseResult parsed)
        {
            var matches = GoBarMatcher.Match(typed, GoBarRegistry.All);
            var commands = matches.Select(m => EntryOption(m.Entry)).ToList();

            var argument = parsed.Kind == GoBarParseKind.Argument
                ? ResolveArgument(parsed)
                : new GoBarSuggestion(Array.Empty<GoBarOption>(), parsed.Refusal ?? $"Nothing matches {typed}.");

            if (commands.Count == 0) return argument;
            if (argument.Options.Count == 0) return Finish(commands);

            bool exactCommand = matches[0].How is GoBarMatchKind.ExactCode
                or GoBarMatchKind.ExactName or GoBarMatchKind.ExactAlias;
            if (exactCommand)
                return Finish(commands.Concat(argument.Options).ToList());

            var merged = argument.Options.Concat(commands).ToList();
            // The several-symbols sentence names the matches; say the commands are there too.
            string sentence = argument.Options[0].GroupSentence is { } group
                ? $"{group} {commands.Count} {(commands.Count == 1 ? "command also matches" : "commands also match")}."
                : SentenceFor(merged);
            return new GoBarSuggestion(merged, sentence);
        }

        private GoBarSuggestion ResolveBareVerb(GoBarParseResult parsed)
        {
            var spec = parsed.Verb is { } verb ? GoBarVerbs.For(verb) : null;

            if (spec?.Entry is { } command)
            {
                // The 6a entry. Run() turns TF/SYM/PROV into the "type a value" hint.
                var entry = GoBarRegistry.All.Single(e => e.Command == command);
                return new GoBarSuggestion(new[] { EntryOption(entry) }, SentenceFor(new[] { EntryOption(entry) }));
            }

            GoBarOption? hint = parsed.Verb switch
            {
                GoBarVerb.GoTo => HintOption(
                    "Go to a date or an event",
                    "Type a date or an event after GOTO, like GOTO 2024-03-01.",
                    "GOTO "),
                GoBarVerb.Find => HintOption(
                    "Find a candle pattern or chart formation",
                    "Type a pattern after FIND, like FIND hammer.",
                    "FIND "),
                _ => null,
            };

            return hint == null
                ? new GoBarSuggestion(Array.Empty<GoBarOption>(),
                    $"Nothing matches {parsed.Argument}.")
                : new GoBarSuggestion(new[] { hint }, SentenceFor(new[] { hint }));
        }

        private GoBarSuggestion ResolveArgument(GoBarParseResult parsed)
        {
            switch (parsed.Verb)
            {
                case GoBarVerb.Timeframe when parsed.Timeframe is { } tf:
                    return Finish(new[] { TimeframeOption(tf, parsed.TimeframeWords ?? GoBarParser.WordsFor(tf)) });

                case GoBarVerb.Symbol:
                    return ResolveSymbol(parsed.Argument);

                case GoBarVerb.Provider:
                    return ResolveProvider(parsed.Argument);

                case GoBarVerb.Indicator:
                    return ResolveIndicator(parsed.Argument);

                case GoBarVerb.Template:
                    return ResolveTemplate(parsed.Argument);

                case GoBarVerb.Renko:
                    if (!GoBarParser.TryParseRenko(parsed.Argument, out var renko))
                        return new GoBarSuggestion(Array.Empty<GoBarOption>(), BrickChartSpeech.BadArgument);
                    string renkoName = renko.Mode == RenkoBoxMode.Fixed
                        ? $"Renko chart, fixed box {SpeechPriceFormatter.FormatPrice(renko.BoxSize)}"
                        : $"Renko chart, ATR {renko.AtrLength}";
                    return new GoBarSuggestion(new[]
                    {
                        new GoBarOption(
                            renkoName,
                            Action: new GoBarRenkoAction(renko),
                            CanonicalText: parsed.Argument.StartsWith("ATR", StringComparison.OrdinalIgnoreCase)
                                ? $"RENKO ATR {renko.AtrLength}"
                                : $"RENKO {renko.BoxSize.ToString("0.##########", CultureInfo.InvariantCulture)}",
                            Help: "Draw the price as Renko bricks with this box"),
                    }, renkoName + ".");

                case GoBarVerb.PriceAlert when parsed.Price is { } price:
                    return Finish(new[]
                    {
                        new GoBarOption(
                            $"Price alert at {AccessibleTrader.Core.Services.Alerts.AlertDescriptions.FormatForSpeech(price)}",
                            Action: new GoBarPriceAlertAction(price),
                            CanonicalText: $"ALERT {price.ToString("0.##########", CultureInfo.InvariantCulture)}",
                            Help: "Open the alerts form with this price filled in; it does not create the alert"),
                    });

                case GoBarVerb.GoTo:
                    return ResolveGoTo(parsed);

                case GoBarVerb.Find:
                    return ResolveFind(parsed.Argument);

                default:
                    return new GoBarSuggestion(Array.Empty<GoBarOption>(),
                        $"Nothing matches {(string.IsNullOrWhiteSpace(parsed.Argument) ? "that" : parsed.Argument)}.");
            }
        }

        // ── Each argument's options ────────────────────────────────────────────

        private GoBarOption TimeframeOption(string timeframe, string words) =>
            new($"Timeframe {words}",
                Action: new GoBarTimeframeAction(timeframe, words),
                CanonicalText: $"TF {timeframe}",
                Help: "Change the chart on screen to this timeframe");

        private GoBarOption DateOption(DateTime moment, bool hasTime, string canonicalText) =>
            new($"Go to {DateLong(moment, hasTime)}",
                Action: new GoBarDateAction(moment, hasTime, DateLong(moment, hasTime)),
                CanonicalText: $"GOTO {canonicalText}",
                Help: "Move the cursor to the last bar at or before that moment, "
                    + "in the clock the bar readout uses");

        private GoBarSuggestion ResolveSymbol(string argument)
        {
            if (_orchestrator == null)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(), $"No {argument} on this venue.");

            string currentProvider = CurrentVenueProvider();
            var matches = GoBarSymbolSearch.Find(
                argument,
                currentProvider,
                _orchestrator.AvailableSymbols,
                _watchlists?.All ?? Array.Empty<Watchlist>(),
                otherVenues: EmptyVenues,
                _store?.State.SelectedMarketType ?? MarketType.Crypto,
                _orchestrator.SelectedSubType ?? "Spot");

            if (matches.Count == 0)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(),
                    $"No {argument} on {currentProvider} or your watchlists.");

            // An exact full-symbol match on the current venue wins alone. Otherwise every
            // match is its own option, and Enter alone must not quietly run the first.
            var exactCurrent = matches
                .Where(m => !m.FromWatchlist
                    && string.Equals(m.Provider, currentProvider, StringComparison.OrdinalIgnoreCase)
                    && GoBarSymbolSearch.Rank(m.Symbol, GoBarSymbolSearch.Normalize(argument)) == 0)
                .ToList();

            var chosen = exactCurrent.Count > 0 ? exactCurrent : matches;
            bool several = chosen.Count > 1;
            string? groupSentence = several ? SymbolGroupSentence(argument, chosen) : null;
            string? prompt = several ? $"{argument}, {chosen.Count} matches. Arrow down to pick one." : null;

            var options = chosen.Select(m =>
            {
                bool sameVenue = string.Equals(m.Provider, currentProvider, StringComparison.OrdinalIgnoreCase);
                return new GoBarOption(
                    $"Open {m.Provider} {m.Symbol}",
                    Action: new GoBarSymbolAction(
                        m.Provider, m.Symbol, m.Market, m.SubType, SameVenue: sameVenue),
                    CanonicalText: $"GP {m.Symbol}",
                    Help: sameVenue
                        ? "Open it on the active tab, keeping its timeframe"
                        : "Switch to that provider and open it on the active tab",
                    RequiresExplicitPick: several,
                    ExplicitPickPrompt: prompt,
                    GroupCount: chosen.Count,
                    GroupSentence: groupSentence);
            }).ToList();

            return new GoBarSuggestion(options, groupSentence ?? SentenceFor(options));
        }

        private static string SymbolGroupSentence(string argument, IReadOnlyList<GoBarSymbolMatch> matches)
        {
            var names = matches.Take(5).Select(m => $"{m.Provider} {m.Symbol}").ToList();
            string list = string.Join(", ", names);
            if (matches.Count > 5) list += $", and {matches.Count - 5} more";
            return $"{argument}, {matches.Count} matches: {list}.";
        }

        private GoBarSuggestion ResolveProvider(string argument)
        {
            if (_orchestrator == null)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(), $"No provider called {argument}.");

            var matches = MatchNames(argument, _orchestrator.AvailableProviders);
            if (matches.Count == 0)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(), $"No provider called {argument}.");

            var options = matches.Select(name => new GoBarOption(
                $"Provider {name}",
                Action: new GoBarProviderAction(name),
                CanonicalText: $"PROV {name}",
                Help: "Choose it in the toolbar and list its symbols; it does not load a chart")).ToList();

            return new GoBarSuggestion(options, SentenceFor(options));
        }

        private GoBarSuggestion ResolveIndicator(string argument)
        {
            if (_indicators == null || _demo == null)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(), $"No indicator called {argument}.");

            var all = IndicatorCatalog.Available(_indicators, _demo);
            var matches = IndicatorCatalog.Search(all, argument);
            if (matches.Count == 0)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(), $"No indicator called {argument}.");

            var options = matches.Select(meta => new GoBarOption(
                $"Add {meta.Name}",
                Action: new GoBarIndicatorAction(meta.Code, meta.Name),
                CanonicalText: $"IND {meta.Code}",
                Help: $"{meta.Category}, {IndicatorCatalog.PaneDescription(meta)}; added with its defaults")).ToList();

            return new GoBarSuggestion(options, SentenceFor(options));
        }

        /// <summary>
        /// TPL with a name applies that template on Enter, with no dialog. An exact name wins;
        /// otherwise templates containing the typed words are listed so the user can pick one.
        /// </summary>
        private GoBarSuggestion ResolveTemplate(string argument)
        {
            var available = _templates?.GetAvailable() ?? new List<string>();
            var exact = available
                .Where(n => string.Equals(n, argument, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var matches = exact.Count > 0
                ? exact
                : available
                    .Where(n => n.Contains(argument, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (matches.Count == 0)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(),
                    $"No indicator template called {argument}.");

            var options = matches.Select(name => new GoBarOption(
                $"Apply indicator template {name}",
                Action: new GoBarApplyTemplateAction(name),
                CanonicalText: $"TPL {name}",
                Help: "Replaces this chart's indicators; Control plus Z undoes it")).ToList();

            return new GoBarSuggestion(options, SentenceFor(options));
        }

        private GoBarSuggestion ResolveGoTo(GoBarParseResult parsed)
        {
            if (parsed.Moment is { } moment)
                return Finish(new[] { DateOption(moment, parsed.MomentHasTime, parsed.Argument) });

            string argument = parsed.Argument;
            var state = _store?.State;

            if (state != null
                && MarketEventLevels.Parse(state.MarketEventsLevel) == MarketEventLevel.Off)
            {
                return new GoBarSuggestion(Array.Empty<GoBarOption>(),
                    "Market events are off. Turn them on in Settings to use GOTO with an event.");
            }

            // A chart with no bars is answered by the common "No chart loaded." rule on run;
            // the option itself can still name what was typed.
            if (state == null || state.Data == null || state.Data.Count == 0 || _eventMap == null)
            {
                var only = new GoBarOption(
                    $"Go to the last {argument}",
                    Action: new GoBarEventAction("", argument),
                    CanonicalText: $"GOTO {argument}");
                return Finish(new[] { only });
            }

            var placements = _eventMap.For(state, DateTime.UtcNow)
                .Where(p => p.IsPlaced && p.BarIndex <= state.Data.Count - 1)
                .Where(p => NameMatches(argument, p.Event.SpokenName)
                         || NameMatches(argument, p.Event.LongName))
                .ToList();

            var groups = placements
                .GroupBy(p => p.Event.SpokenName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p => p.BarIndex)
                              .ThenByDescending(p => p.Event.MomentUtc).First())
                .OrderByDescending(p => p.BarIndex)
                .ToList();

            if (groups.Count == 0)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(), $"No {argument} on this chart.");

            var options = groups.Select(p => new GoBarOption(
                $"Go to the last {p.Event.SpokenName}",
                Action: new GoBarEventAction(p.Event.Id, p.Event.SpokenName),
                CanonicalText: $"GOTO {p.Event.SpokenName}",
                Help: "Move the cursor to the latest placed bar for that event")).ToList();

            return new GoBarSuggestion(options, SentenceFor(options));
        }

        private GoBarSuggestion ResolveFind(string argument)
        {
            var names = PatternNames()
                .Where(n => NameMatches(argument, n.Name))
                .OrderBy(n => GoBarSymbolSearch.Rank(n.Name, GoBarSymbolSearch.Normalize(argument)))
                .ToList();

            if (names.Count == 0)
                return new GoBarSuggestion(Array.Empty<GoBarOption>(), $"No pattern called {argument}.");

            var options = names.Select(n => new GoBarOption(
                $"Find the next {n.Name}",
                Action: new GoBarFindAction(n.Name, n.IsFormation),
                CanonicalText: $"FIND {n.Name}",
                Help: n.IsFormation
                    ? "Move to the next edge of that chart formation right of the cursor"
                    : "Move to the next bar the candle classifier names that way, right of the cursor")).ToList();

            return new GoBarSuggestion(options, SentenceFor(options));
        }

        private static IReadOnlyList<(string Name, bool IsFormation)> PatternNames()
        {
            var names = new List<(string, bool)>();
            foreach (var type in Enum.GetValues<CandleType>())
            {
                string name = CandlePatternSpeech.TypeName(type);
                if (name.Length > 0) names.Add((name, false));
            }
            foreach (var pattern in Enum.GetValues<CandlePattern>())
            {
                string name = CandlePatternSpeech.PatternName(pattern);
                if (name.Length > 0) names.Add((name, false));
            }
            foreach (var kind in Enum.GetValues<ChartPatternKind>())
            {
                string name = ChartPatternNarrator.Name(kind);
                if (name.Length > 0) names.Add((name, true));
            }
            return names
                .DistinctBy(n => n.Item1, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool NameMatches(string typed, string? name)
            => !string.IsNullOrEmpty(name)
               && name.Contains(typed, StringComparison.OrdinalIgnoreCase);

        /// <summary>Exact name, then starts-with, then start-of-word. No near miss for providers.</summary>
        private static IReadOnlyList<string> MatchNames(string typed, IReadOnlyList<string> candidates)
        {
            string wanted = typed.Trim().ToUpperInvariant();
            if (wanted.Length == 0 || candidates == null) return Array.Empty<string>();

            var exact = candidates.Where(c => c.ToUpperInvariant() == wanted).ToList();
            if (exact.Count > 0) return exact;

            var prefix = candidates.Where(c => c.ToUpperInvariant().StartsWith(wanted, StringComparison.Ordinal)).ToList();
            if (prefix.Count > 0) return prefix;

            return candidates
                .Where(c => c.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(w => w.ToUpperInvariant().StartsWith(wanted, StringComparison.Ordinal)))
                .ToList();
        }

        // ── Running the chosen option ──────────────────────────────────────────

        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyVenues =
            new Dictionary<string, IReadOnlyList<string>>();

        /// <summary>Run one chosen option, or say why it cannot run.</summary>
        public void Run(GoBarOption option) => Run(option, explicitlyChosen: false);

        /// <summary>
        /// Run one chosen option. <paramref name="explicitlyChosen"/> is true when the user
        /// moved onto it with the arrows or clicked it; it is what lets the several-symbols
        /// rule tell "Enter on the top match" from "I picked this one".
        /// </summary>
        public void Run(GoBarOption option, bool explicitlyChosen)
        {
            if (option == null) return;

            // HELP is a reading command: its option says the whole help again, and the box
            // stays open with the typed text exactly as it was. Nothing is dispatched.
            if (option.Action is GoBarHelpAction help)
            {
                Speak(help.Sentence, interrupt: true);
                return;
            }

            // A bare verb keeps the box open and completes the text. Nothing is dispatched.
            if (option.Action is GoBarArgHintAction hint)
            {
                SpeakAndComplete(hint);
                return;
            }

            if (option.RequiresExplicitPick && !explicitlyChosen)
            {
                Speak(option.ExplicitPickPrompt ?? "Several matches. Arrow down to pick one.", interrupt: true);
                return;
            }

            if (option.Entry is { } entry)
            {
                if (entry.Kind == GoBarKind.KeyOnly)
                {
                    Speak(KeyOnlySentence(entry), interrupt: true);
                    return;
                }

                if (BareHint(entry.Command) is { } entryHint)
                {
                    SpeakAndComplete(entryHint);
                    return;
                }

                // RefreshData is one of the four the Go bar owns now.
                if (entry.Command == SystemCommand.RefreshData)
                {
                    Schedule(option with { Action = new GoBarRefreshAction() });
                    return;
                }

                Schedule(option);
                return;
            }

            if (option.Action != null) Schedule(option);
        }

        private static GoBarArgHintAction? BareHint(SystemCommand command) => command switch
        {
            SystemCommand.ChangeTimeframe => new(
                "Type a timeframe after TF, like TF 4h.", "TF "),
            SystemCommand.ChangeSymbol => new(
                "Type a symbol after GP, like GP BTC.", "GP "),
            SystemCommand.ChangeProvider => new(
                "Type a provider after PROV, like PROV Kraken.", "PROV "),
            _ => null,
        };

        private void SpeakAndComplete(GoBarArgHintAction hint)
        {
            Speak(hint.Prompt, interrupt: true);
            _bus.Publish(new GoBarCompleteRequestEvent(hint.Completion));
        }

        private void Schedule(GoBarOption option)
        {
            // One action at a time: a second Enter while the first close is in flight is the
            // same intent, not a queue.
            if (Interlocked.CompareExchange(ref _pending, option, null) != null) return;

            _bus.Publish(new CloseGoBarRequestEvent());

            // No box was actually open (a caller outside the modal). Nothing will publish the
            // close for us, so run now — the stack is already empty.
            if (!_modalStack.IsAnyOpen)
            {
                var pending = Interlocked.Exchange(ref _pending, null);
                if (pending != null) Execute(pending);
            }
        }

        private void OnModalStackChanged(ModalStackChange change)
        {
            if (change.IsOpen) return;

            var pending = Interlocked.Exchange(ref _pending, null);
            if (pending == null) return;

            if (change.Stack.Count > 0)
            {
                string dialog = CommandDispatcher.SpokenModalName(change.Stack[^1]);
                Speak($"{pending.Name} can't run while {dialog} is open.", interrupt: true);
                return;
            }

            Execute(pending);
        }

        private void Execute(GoBarOption option)
        {
            if (option.Entry is { } entry && option.Action == null)
            {
                _dispatcher.DispatchFromGoBar(entry.Command);
                return;
            }

            _ = ExecuteActionAsync(option);
        }

        private async Task ExecuteActionAsync(GoBarOption option)
        {
            try
            {
                switch (option.Action)
                {
                    case GoBarTimeframeAction tf:
                        await ExecuteTimeframeAsync(tf).ConfigureAwait(false);
                        break;
                    case GoBarSymbolAction symbol:
                        await ExecuteSymbolAsync(symbol).ConfigureAwait(false);
                        break;
                    case GoBarProviderAction provider:
                        await ExecuteProviderAsync(provider).ConfigureAwait(false);
                        break;
                    case GoBarIndicatorAction indicator:
                        ExecuteIndicator(indicator);
                        break;
                    case GoBarPriceAlertAction alert:
                        ExecutePriceAlert(alert);
                        break;
                    case GoBarApplyTemplateAction template:
                        ExecuteTemplate(template);
                        break;
                    case GoBarRenkoAction renko:
                        _dispatcher.ShowRenko(renko.Settings);
                        break;
                    case GoBarDateAction date:
                        ExecuteDate(date);
                        break;
                    case GoBarEventAction marketEvent:
                        ExecuteEvent(marketEvent);
                        break;
                    case GoBarFindAction find:
                        ExecuteFind(find);
                        break;
                    case GoBarRefreshAction:
                        await ExecuteRefreshAsync().ConfigureAwait(false);
                        break;
                    case GoBarSettingAction setting:
                        ExecuteSetting(setting);
                        break;
                    case GoBarMoneyAction money:
                        ExecuteMoney(money);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Go bar action failed: {ex.Message}");
                Speak("The command could not run.", interrupt: true);
            }
        }

        // ── The actions ────────────────────────────────────────────────────────

        private async Task ExecuteTimeframeAsync(GoBarTimeframeAction action)
        {
            var state = _store?.State;
            if (state == null || state.Data == null || state.Data.Count == 0)
            {
                Speak("No chart loaded.", interrupt: true);
                return;
            }

            string current = FirstNonEmpty(state.Identity.Timeframe, _orchestrator?.SelectedTimeframe);
            if (string.Equals(current, action.Timeframe, StringComparison.Ordinal))
            {
                Speak($"Already on {action.Words}.", interrupt: true);
                return;
            }

            string symbol = FirstNonEmpty(state.Identity.Symbol, _orchestrator?.SelectedSymbol);
            Speak($"Loading {symbol}, {action.Words}.", interrupt: true);

            if (_orchestrator == null) return;
            var outcome = await _orchestrator.LoadOnActiveTabAsync(
                provider: null, symbol: null, timeframe: action.Timeframe,
                market: null, subType: null).ConfigureAwait(false);
            SayLoadOutcome(outcome);
        }

        private async Task ExecuteSymbolAsync(GoBarSymbolAction action)
        {
            var state = _store?.State;
            string timeframe = FirstNonEmpty(
                state?.Identity.Timeframe, _orchestrator?.SelectedTimeframe, "1h");
            string provider = action.SameVenue
                ? FirstNonEmpty(state?.Identity.Provider, action.Provider)
                : action.Provider;

            Speak($"Loading {provider} {action.Symbol}, {GoBarParser.WordsFor(timeframe)}.",
                interrupt: true);

            if (_orchestrator == null) return;
            var outcome = action.SameVenue
                ? await _orchestrator.LoadOnActiveTabAsync(
                    provider: null, symbol: action.Symbol, timeframe: null,
                    market: null, subType: null).ConfigureAwait(false)
                : await _orchestrator.LoadOnActiveTabAsync(
                    provider: action.Provider, symbol: action.Symbol, timeframe: null,
                    market: action.Market, subType: action.SubType).ConfigureAwait(false);
            SayLoadOutcome(outcome);
        }

        private async Task ExecuteProviderAsync(GoBarProviderAction action)
        {
            if (_orchestrator == null)
            {
                Speak($"No provider called {action.Provider}.", interrupt: true);
                return;
            }

            _orchestrator.SelectedProvider = action.Provider;
            if (!string.Equals(_orchestrator.SelectedProvider, action.Provider, StringComparison.Ordinal))
            {
                Speak("Not in the public demo.", interrupt: true);
                return;
            }

            try
            {
                await _orchestrator.RefreshSymbolsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Go bar provider refresh failed: {ex.Message}");
                Speak($"{action.Provider} is selected, but its symbols could not be loaded.",
                    interrupt: true);
                return;
            }

            if (!string.Equals(_orchestrator.SelectedProvider, action.Provider, StringComparison.Ordinal))
            {
                Speak("Not in the public demo.", interrupt: true);
                return;
            }

            int count = _orchestrator.AvailableSymbols?.Count ?? 0;
            Speak($"Provider {action.Provider}, {count.ToString("N0", CultureInfo.InvariantCulture)} symbols. "
                + "Type GP and a symbol to open one.", interrupt: true);
        }

        private void ExecuteIndicator(GoBarIndicatorAction action)
        {
            var state = _store?.State;
            if (state == null || state.Data == null || state.Data.Count == 0)
            {
                Speak("No chart loaded.", interrupt: true);
                return;
            }

            if (state.CurrentDataShape != Sdk.Plugins.ProviderDataShape.Ohlcv)
            {
                Speak("This is an analytics chart; indicators need a price chart.", interrupt: true);
                return;
            }

            var meta = _indicators == null || _demo == null
                ? null
                : IndicatorCatalog.Available(_indicators, _demo)
                    .FirstOrDefault(i => string.Equals(i.Code, action.Code, StringComparison.OrdinalIgnoreCase));

            if (meta == null || _series == null)
            {
                Speak($"No indicator called {action.Name}.", interrupt: true);
                return;
            }

            _series.RegisterSeriesFromMetadata(meta, IndicatorCatalog.DefaultParameters(meta));
            Speak($"{meta.Name} added, {IndicatorCatalog.PaneDescription(meta)}.", interrupt: true);
        }

        private void ExecuteTemplate(GoBarApplyTemplateAction action)
        {
            if (_demo?.IsDemo == true)
            {
                Speak("Not in the public demo.", interrupt: true);
                return;
            }

            var template = _templates?.Load(action.Name);
            if (template == null || _workspaceInit == null)
            {
                Speak($"Indicator template {action.Name} could not be read.", interrupt: true);
                return;
            }

            int applied = _workspaceInit.ApplyIndicatorTemplate(template, action.Name);

            var id = _store?.State.Identity;
            string chart = WorkspaceLibraryService.ChartName(id?.Provider, id?.Symbol, id?.Timeframe);
            Speak(IndicatorTemplateService.AppliedSentence(action.Name, chart, applied,
                _workspaceInit.LastRestoreNotice), interrupt: true);
        }

        private void ExecuteSetting(GoBarSettingAction action)
            => _bus.Publish(new OpenSettingsEvent(action.Entry.Tab, action.Entry.ControlId));

        /// <summary>
        /// Ask the dashboard to fill in or find the position. The Go bar never places, closes or
        /// arms anything, and it does not speak for the dashboard: only the dashboard knows
        /// whether a ticket exists on this chart.
        /// </summary>
        private void ExecuteMoney(GoBarMoneyAction action)
            => _bus.Publish(new OpenTradingDashboardEvent(
                Side: action.Flat ? null : action.Side,
                LimitPrice: action.LimitPrice,
                Quantity: action.Quantity,
                Market: action.Market,
                Flat: action.Flat));

        private void ExecutePriceAlert(GoBarPriceAlertAction action)
        {
            var state = _store?.State;
            if (state == null || state.Data == null || state.Data.Count == 0)
            {
                Speak("No chart loaded.", interrupt: true);
                return;
            }

            // The form reads itself; it does not create the alert until the user presses Add.
            _bus.Publish(new OpenAlertsEvent(AlertTarget.Price, Value: action.Price));
        }

        private void ExecuteDate(GoBarDateAction action)
        {
            var state = _store?.State;
            var data = state?.Data;
            if (state == null || data == null || data.Count == 0)
            {
                Speak("No chart loaded.", interrupt: true);
                return;
            }

            int barSeconds = PlaybackNarration.BarSeconds(state);
            var first = BarMoment(data[0].Date, barSeconds);
            var last = BarMoment(data[^1].Date, barSeconds);

            if (action.Wall < first)
            {
                Speak($"{DateLong(action.Wall, action.HasTime)} is before this chart's first bar, "
                    + $"{BarMomentText(data[0].Date, barSeconds)}.", interrupt: true);
                return;
            }

            if (action.Wall >= last.AddSeconds(Math.Max(1, barSeconds)))
            {
                Speak("That is after the latest bar.", interrupt: true);
                return;
            }

            int target = -1;
            for (int i = data.Count - 1; i >= 0; i--)
            {
                if (BarMoment(data[i].Date, barSeconds) <= action.Wall)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0)
            {
                Speak($"{DateLong(action.Wall, action.HasTime)} is before this chart's first bar, "
                    + $"{BarMomentText(data[0].Date, barSeconds)}.", interrupt: true);
                return;
            }

            NavigateTo(target);
        }

        private void ExecuteEvent(GoBarEventAction action)
        {
            var state = _store?.State;
            if (state == null || state.Data == null || state.Data.Count == 0)
            {
                Speak("No chart loaded.", interrupt: true);
                return;
            }

            if (MarketEventLevels.Parse(state.MarketEventsLevel) == MarketEventLevel.Off)
            {
                Speak("Market events are off. Turn them on in Settings to use GOTO with an event.",
                    interrupt: true);
                return;
            }

            var placements = _eventMap?.For(state, DateTime.UtcNow)
                .Where(p => p.IsPlaced && p.BarIndex <= state.Data.Count - 1)
                .ToList() ?? new List<MarketEventBarPlacement>();

            var placement = !string.IsNullOrEmpty(action.EventId)
                ? placements.FirstOrDefault(p => p.Event.Id == action.EventId)
                : placements
                    .Where(p => string.Equals(p.Event.SpokenName, action.Name, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(p.Event.LongName, action.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.BarIndex)
                    .ThenByDescending(p => p.Event.MomentUtc)
                    .FirstOrDefault();

            if (placement == null)
            {
                Speak($"No {action.Name} on this chart.", interrupt: true);
                return;
            }

            // The navigator and this fallback are the same two dispatches. If it refuses the
            // event for any reason, the placement we already found is still a real bar; moving
            // there is right, and doing nothing would be one of the silent drops 6a review
            // banned. (The placement came from the same map JumpTo consults.)
            if (_marketEvents == null || !_marketEvents.JumpTo(placement.Event))
                NavigateTo(placement.BarIndex);
        }

        private void ExecuteFind(GoBarFindAction action)
        {
            var state = _store?.State;
            var data = state?.Data;
            if (state == null || data == null || data.Count == 0)
            {
                Speak("No chart loaded.", interrupt: true);
                return;
            }

            if (action.IsFormation)
            {
                FindFormation(action, state, data);
                return;
            }

            if (_candles == null)
            {
                Speak("Candle patterns are unavailable.", interrupt: true);
                return;
            }

            int cursor = state.CurrentDataIndex < 0
                ? -1
                : Math.Clamp(state.CurrentDataIndex, 0, data.Count - 1);

            int target = -1;
            for (int i = cursor + 1; i < data.Count && target < 0; i++)
            {
                var analysis = CandlePatternSpeech.AnalyzeAt(
                    _candles, data, i, state.IsHeikinAshi);
                if (CandleIs(analysis, action.Name))
                {
                    target = i;
                }
            }

            if (target < 0)
            {
                int before = 0;
                for (int i = 0; i <= cursor && i < data.Count; i++)
                {
                    var analysis = CandlePatternSpeech.AnalyzeAt(
                        _candles, data, i, state.IsHeikinAshi);
                    if (CandleIs(analysis, action.Name))
                    {
                        before++;
                    }
                }

                Speak($"No {action.Name} after this bar; {before} before it.", interrupt: true);
                return;
            }

            NavigateTo(target);
        }

        /// <summary>
        /// A bar answers to its multi-bar pattern's name AND to its own shape's name. The
        /// readout's first word is the pattern ("Morning doji star"), but the bar that ends it is
        /// still a doji, and "FIND doji" skipping it would make the count "before it" wrong too.
        /// </summary>
        private static bool CandleIs(CandleAnalysis analysis, string name)
            => string.Equals(CandlePatternSpeech.DescribeShape(analysis), name, StringComparison.OrdinalIgnoreCase)
               || string.Equals(CandlePatternSpeech.TypeName(analysis.Type), name, StringComparison.OrdinalIgnoreCase);

        private void FindFormation(GoBarFindAction action, WorkspaceState state,
            IReadOnlyList<Ohlcv> data)
        {
            if (!state.DescribeChartPatterns)
            {
                Speak("Chart formation description is off. Turn it on in Settings to use these keys.",
                    interrupt: true);
                return;
            }

            if (_patterns == null)
            {
                Speak("No chart formations on this chart.", interrupt: true);
                return;
            }

            int cursor = state.CurrentDataIndex < 0
                ? -1
                : Math.Clamp(state.CurrentDataIndex, 0, data.Count - 1);

            var matching = _patterns.For(state.Identity, data)
                .Where(p => string.Equals(ChartPatternNarrator.Name(p.Kind),
                    action.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int target = -1;
            foreach (var pattern in matching)
            {
                foreach (int edge in new[] { pattern.KnownAtIndex, pattern.ResolvesAt })
                {
                    if (edge <= cursor || edge < 0 || edge >= data.Count) continue;
                    target = target < 0 ? edge : Math.Min(target, edge);
                }
            }

            if (target < 0)
            {
                int before = matching.Count(p => p.KnownAtIndex >= 0 && p.KnownAtIndex <= cursor);
                Speak($"No {action.Name} after this bar; {before} before it.", interrupt: true);
                return;
            }

            NavigateTo(target);
        }

        /// <summary>
        /// Exactly <see cref="MarketEventNavigator.NavigateTo"/>: navigate, set the interaction
        /// context to the component, and ask for a jump read. The bar is then read the same way
        /// as an arrow key, which is the point.
        /// </summary>
        private void NavigateTo(int barIndex)
        {
            if (_store == null) return;
            _store.Dispatch(new NavigateAction(barIndex));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
            _bus.Publish(new FeedbackRequestEvent(
                FeedbackType.Navigation, "", true, IsXMove: true, IsJump: true));
        }

        private void SayLoadOutcome(GoBarLoadOutcome outcome)
        {
            switch (outcome.Kind)
            {
                case GoBarLoadOutcomeKind.NotInDemo:
                    Speak("Not in the public demo.", interrupt: true);
                    break;
                case GoBarLoadOutcomeKind.ShapeChange:
                    Speak($"That is a different kind of chart and would remove your "
                        + $"{outcome.NonCoreSeries} {(outcome.NonCoreSeries == 1 ? "indicator" : "indicators")}. "
                        + "Load it from the toolbar to open it in a new tab.", interrupt: true);
                    break;
                case GoBarLoadOutcomeKind.NoChart:
                    Speak("No chart loaded.", interrupt: true);
                    break;
                // Loaded and Failed: the load's own path has already said what happened.
            }
        }

        private async Task ExecuteRefreshAsync()
        {
            var state = _store?.State;
            if (state == null || state.Data == null || state.Data.Count == 0)
            {
                Speak("No chart loaded.", interrupt: true);
                return;
            }

            string symbol = FirstNonEmpty(state.Identity.Symbol, _orchestrator?.SelectedSymbol);
            string timeframe = FirstNonEmpty(state.Identity.Timeframe, _orchestrator?.SelectedTimeframe);
            Speak($"Reloading {symbol}, {GoBarParser.WordsFor(timeframe)}.", interrupt: true);

            if (_orchestrator == null) return;
            var outcome = await _orchestrator.LoadOnActiveTabAsync(
                provider: null, symbol: null, timeframe: null,
                market: null, subType: null).ConfigureAwait(false);
            SayLoadOutcome(outcome);
        }

        // ── Wording helpers ────────────────────────────────────────────────────

        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

        private static string DateLong(DateTime moment, bool hasTime)
        {
            string date = moment.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            if (!hasTime) return date;
            return $"{date}, {moment.ToString(SpeechTimeFormatter.Clock("HH:mm"), CultureInfo.InvariantCulture)}";
        }

        /// <summary>
        /// The app's own display clock for a typed date: F12's "times in UTC" setting when on,
        /// otherwise the user's local zone. A test passes a zone in rather than inheriting the
        /// machine's, so the rule is proved on any build agent.
        /// </summary>
        private TimeZoneInfo DisplayZone =>
            _displayZone ?? (SpeechTimeFormatter.UseUtc ? TimeZoneInfo.Utc : TimeZoneInfo.Local);

        private DateTime BarMoment(DateTime stamp, int barSeconds)
            => SpeechTimeFormatter.ToBarDisplay(stamp, barSeconds, DisplayZone);

        private string BarMomentText(DateTime stamp, int barSeconds)
        {
            var display = BarMoment(stamp, barSeconds);
            return barSeconds < 86400
                ? DateLong(display, hasTime: true)
                : DateLong(display, hasTime: false);
        }

        private string CurrentVenueProvider()
        {
            // The symbol list the search can see belongs to the toolbar's selected provider.
            // That is kept in step with the chart on screen everywhere except a dropdown the
            // user changed and never loaded, where the load path corrects it anyway.
            string selected = _orchestrator?.SelectedProvider ?? "";
            return string.IsNullOrEmpty(selected)
                ? (_store?.State.Identity.Provider ?? "")
                : selected;
        }

        private static GoBarOption EntryOption(GoBarEntry entry) =>
            new(entry.Name,
                Entry: entry,
                CanonicalText: entry.Codes.Count > 0 ? entry.Codes[0] : entry.Name,
                Help: entry.Help);

        private static GoBarOption HintOption(string name, string prompt, string completion) =>
            new(name, Action: new GoBarArgHintAction(prompt, completion), CanonicalText: completion);

        private GoBarSuggestion Finish(IReadOnlyList<GoBarOption> options)
            => new(options, SentenceFor(options));

        private string SentenceFor(IReadOnlyList<GoBarOption> options)
        {
            if (options.Count == 0) return string.Empty;

            var top = options[0];
            string sentence;
            if (top.Action is GoBarSettingAction setting)
            {
                sentence = $"{setting.Entry.Label}, setting, {setting.Entry.Tab} tab.";
            }
            else if (top.Action is GoBarHelpAction help)
            {
                sentence = help.Sentence;
            }
            else if (top.Entry != null)
            {
                string? key = KeyLabel(top.Entry.Command);
                sentence = key == null ? $"{top.Name}." : $"{top.Name}, {key}.";
            }
            else
            {
                sentence = $"{top.Name}.";
            }

            int more = options.Count - 1;
            if (more == 1) sentence += " 1 more.";
            else if (more > 1) sentence += $" {more} more.";
            return sentence;
        }

        /// <summary>
        /// The live key for a command, in the words the app already uses for speech
        /// ("Control plus Alt plus W"), or null when it has no binding. The registry itself
        /// carries no key, so a rebind is reflected here with no second table to update.
        /// </summary>
        public string? KeyLabel(SystemCommand command)
        {
            // CloseModal has no binding of its own by design: Escape is bound to CancelDrawing
            // and the dispatcher re-routes it to CloseModal while a dialog is open. A key-only
            // sentence for it should say the key the user actually presses.
            if (command == SystemCommand.CloseModal)
                command = SystemCommand.CancelDrawing;

            return ShortcutWords.ForCommand(_shortcuts, command);
        }

        private string KeyOnlySentence(GoBarEntry entry)
        {
            string? key = KeyLabel(entry.Command);
            return key == null
                ? $"{entry.Name} is a key; it does nothing from the Go bar."
                : $"{entry.Name} is a key, {key}; it does nothing from the Go bar.";
        }

        private void Speak(string text, bool interrupt)
            => _bus.Publish(new AnnouncementEvent(
                text, interrupt, Channel: SpeechChannel.Interface));

        public void Dispose()
        {
            _modalStack.Changed -= OnModalStackChanged;
            _pending = null;
        }
    }
}

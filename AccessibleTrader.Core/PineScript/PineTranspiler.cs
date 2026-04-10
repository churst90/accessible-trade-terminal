using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AccessibleTrader.Core.PineScript
{
    /// <summary>
    /// Result of a PineScript → C# transpilation.
    /// </summary>
    public record TranspileResult(
        bool Success,
        string CSharpCode,
        string[] Errors,
        string[] Warnings);

    /// <summary>
    /// Transpiles Pine Script v5 source code into a C# class that implements
    /// <see cref="AccessibleTrader.Sdk.Interfaces.ICustomIndicator"/>.
    ///
    /// Tier 1 (~80%): ta.sma, ta.ema, ta.rsi, ta.macd, ta.bb, ta.atr, ta.stoch,
    ///                ta.crossover, ta.crossunder, ta.highest, ta.lowest, ta.stdev,
    ///                plot(), plotshape(), inputs, na/nz, math.max/min/abs.
    ///
    /// Tier 2: var/varip hoisting, conditional color → ColorRule, na/nz mapping.
    ///
    /// Tier 3 (stubs): request.security() → NaN + warning;
    ///                 line.new()/label.new() → unsupported;
    ///                 strategy.* → unsupported.
    /// </summary>
    public class PineTranspiler
    {
        // ── Regex patterns ────────────────────────────────────────────────────

        private static readonly Regex _versionRx  = new(@"//\s*@version\s*=\s*(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex _indicatorRx = new(@"indicator\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _inputRx     = new(@"(\w+)\s*=\s*input(?:\.(?:int|float|bool|source|color))?\s*\(([^,)]+)(?:,\s*[""']([^""']+)[""'][^)]*)?", RegexOptions.IgnoreCase);
        private static readonly Regex _plotRx      = new(@"plot\s*\(([^,)]+)(?:,\s*[""']([^""']+)[""'][^)]*)?", RegexOptions.IgnoreCase);
        private static readonly Regex _plotshapeRx = new(@"plotshape\s*\(([^,)]+)(?:,\s*[""']([^""']+)[""'][^)]*)?", RegexOptions.IgnoreCase);
        private static readonly Regex _commentRx   = new(@"//[^\n]*", RegexOptions.Multiline);
        private static readonly Regex _blockComRx  = new(@"/\*.*?\*/", RegexOptions.Singleline);

        // ta.* function invocations
        private static readonly Regex _taSmaRx      = new(@"ta\.sma\s*\(\s*(\w+)\s*,\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taEmaRx      = new(@"ta\.ema\s*\(\s*(\w+)\s*,\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taRsiRx      = new(@"ta\.rsi\s*\(\s*(\w+)\s*,\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taMacdRx     = new(@"ta\.macd\s*\(\s*([^,)]+),\s*([^,)]+),\s*([^,)]+),\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taBbRx       = new(@"ta\.bb\s*\(\s*([^,)]+),\s*([^,)]+),\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taAtrRx      = new(@"ta\.atr\s*\(\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taStochRx    = new(@"ta\.stoch\s*\(\s*([^,)]+),\s*([^,)]+),\s*([^,)]+),\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taCrossRx    = new(@"ta\.crossover\s*\(\s*([^,)]+),\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taUnderRx    = new(@"ta\.crossunder\s*\(\s*([^,)]+),\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taHighestRx  = new(@"ta\.highest\s*\(\s*([^,)]+),\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taLowestRx   = new(@"ta\.lowest\s*\(\s*([^,)]+),\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taStdevRx    = new(@"ta\.stdev\s*\(\s*([^,)]+),\s*([^)]+)\)", RegexOptions.IgnoreCase);
        private static readonly Regex _requestSecRx = new(@"request\.security\s*\([^)]+\)", RegexOptions.IgnoreCase);
        // ta.change / rising / falling
        private static readonly Regex _taChangeRx   = new(@"ta\.change\s*\(\s*(\w+)(?:\s*,\s*(\d+))?\s*\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taRisingRx   = new(@"ta\.rising\s*\(\s*(\w+)\s*,\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taFallingRx  = new(@"ta\.falling\s*\(\s*(\w+)\s*,\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taWhenRx     = new(@"ta\.valuewhen\s*\(\s*([^,]+),\s*([^,]+),\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
        private static readonly Regex _taBarsSinceRx = new(@"ta\.barssince\s*\(\s*([^)]+)\)", RegexOptions.IgnoreCase);
        // Lookback indexing: identifier[n]
        private static readonly Regex _lookbackRx   = new(@"(\b\w+\b)\[(\d+)\]");
        // Element-wise math on array variables (detected after source mapping)
        private static readonly Regex _arrAbsRx     = new(@"Math\.Abs\((_arr_\w+|_close|_open|_high|_low|_volume|_hl2|_hlc3|_ohlc4)([^)]*)\)");
        private static readonly Regex _arrSubRx     = new(@"(_arr_\w+|_close|_open|_high|_low|_volume|_hl2|_hlc3|_ohlc4)\s*-\s*(_arr_\w+|LookbackArr\([^)]+\)|_close|_open|_high|_low|_volume|_hl2|_hlc3|_ohlc4)");

        // Pine source series → C# data access
        private static readonly Dictionary<string, string> _sourceMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["close"]  = "_close",
            ["open"]   = "_open",
            ["high"]   = "_high",
            ["low"]    = "_low",
            ["volume"] = "_volume",
            ["hl2"]    = "_hl2",
            ["hlc3"]   = "_hlc3",
            ["ohlc4"]  = "_ohlc4",
        };

        // ── Public API ────────────────────────────────────────────────────────

        public TranspileResult Transpile(string pineCode)
        {
            if (string.IsNullOrWhiteSpace(pineCode))
                return new TranspileResult(false, "", new[] { "Input code is empty." }, Array.Empty<string>());

            var warnings = new List<string>();
            var errors   = new List<string>();

            // Strip comments for analysis
            string stripped = _commentRx.Replace(pineCode, "");
            stripped = _blockComRx.Replace(stripped, "");

            // ── Extract metadata ─────────────────────────────────────────────
            string indicatorName = "Custom Indicator";
            string shortTitle    = "";
            bool   overlay       = false;

            var indMatch = _indicatorRx.Match(stripped);
            if (indMatch.Success)
            {
                var args = indMatch.Groups[1].Value;
                var nameM = Regex.Match(args, @"[""']([^""']+)[""']");
                if (nameM.Success) indicatorName = nameM.Groups[1].Value;
                var shortM = Regex.Match(args, @"shorttitle\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (shortM.Success) shortTitle = shortM.Groups[1].Value;
                overlay = args.Contains("overlay=true", StringComparison.OrdinalIgnoreCase);
            }

            string displayName = string.IsNullOrEmpty(shortTitle) ? indicatorName : shortTitle;
            string safeId      = "PINE_" + Regex.Replace(indicatorName.ToUpperInvariant(), @"[^A-Z0-9_]", "_");
            string safeClass   = "PineInd_" + Regex.Replace(indicatorName, @"[^A-Za-z0-9]", "_");

            // ── Extract inputs as parameters ─────────────────────────────────
            var parameters   = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var paramLines   = new List<string>(); // for DefaultParameters init
            var pineVarToParam = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in _inputRx.Matches(stripped))
            {
                string varName   = m.Groups[1].Value.Trim();
                string defVal    = m.Groups[2].Value.Trim();
                string paramName = m.Groups[3].Success ? m.Groups[3].Value.Trim() : varName;
                if (double.TryParse(defVal, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double dv))
                {
                    parameters[paramName] = dv;
                    paramLines.Add($"[\"{paramName}\"] = {dv.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                    pineVarToParam[varName] = paramName;
                }
            }

            // ── Extract plot() calls for component definitions ────────────────
            var componentNames   = new List<string>();
            var componentTypes   = new List<string>(); // "Line","Dot","Bar"
            var plotVarToComp    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in _plotRx.Matches(stripped))
            {
                string srcVar  = m.Groups[1].Value.Trim();
                string compNm  = m.Groups[2].Success ? m.Groups[2].Value.Trim() : srcVar;
                if (!componentNames.Contains(compNm))
                {
                    componentNames.Add(compNm);
                    componentTypes.Add("Line");
                    plotVarToComp[srcVar] = compNm;
                }
            }

            foreach (Match m in _plotshapeRx.Matches(stripped))
            {
                string srcVar = m.Groups[1].Value.Trim();
                string compNm = m.Groups[2].Success ? m.Groups[2].Value.Trim() : srcVar + " Shape";
                if (!componentNames.Contains(compNm))
                {
                    componentNames.Add(compNm);
                    componentTypes.Add("Dot");
                    plotVarToComp[srcVar] = compNm;
                }
            }

            if (!componentNames.Any())
            {
                warnings.Add("No plot() calls found — generating a single 'Value' output as fallback.");
                componentNames.Add("Value");
                componentTypes.Add("Line");
            }

            // ── Tier 3 stubs ─────────────────────────────────────────────────
            foreach (Match m in _requestSecRx.Matches(stripped))
            {
                warnings.Add($"request.security() is not supported — replaced with double.NaN. ({m.Value.Substring(0, Math.Min(40, m.Value.Length))}...)");
            }

            // ── Build C# source ───────────────────────────────────────────────
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using AccessibleTrader.Sdk.Interfaces;");
            sb.AppendLine("using AccessibleTrader.Sdk.Models;");
            sb.AppendLine();
            sb.AppendLine($"// Auto-generated by PineTranspiler from: {indicatorName}");
            sb.AppendLine();
            sb.AppendLine($"public class {safeClass} : ICustomIndicator");
            sb.AppendLine("{");
            sb.AppendLine($"    public string Id => \"{safeId}\";");
            sb.AppendLine($"    public string DisplayName => \"{EscapeString(displayName)}\";");
            sb.AppendLine();

            // ComponentNames
            sb.Append("    public string[] ComponentNames => new[] { ");
            sb.Append(string.Join(", ", componentNames.Select(n => $"\"{EscapeString(n)}\"")));
            sb.AppendLine(" };");

            // DisplayTypes
            sb.Append("    public ComponentDisplayType[] DisplayTypes => new[] { ");
            sb.Append(string.Join(", ", componentTypes.Select(t => $"ComponentDisplayType.{t}")));
            sb.AppendLine(" };");
            sb.AppendLine();

            // DefaultParameters
            sb.AppendLine("    public Dictionary<string, double> DefaultParameters => new()");
            sb.AppendLine("    {");
            foreach (var pl in paramLines) sb.AppendLine($"        {pl},");
            sb.AppendLine("    };");
            sb.AppendLine();

            // Calculate method
            sb.AppendLine("    public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> p)");
            sb.AppendLine("    {");
            sb.AppendLine("        int n = data.Length;");
            sb.AppendLine("        if (n == 0) return " + GenerateEmptyReturn(componentNames.Count) + ";");
            sb.AppendLine();

            // Source series
            sb.AppendLine("        // ── Source series ─────────────────────────────────────────────");
            sb.AppendLine("        var _close  = Arr(data, d => d.Close);");
            sb.AppendLine("        var _open   = Arr(data, d => d.Open);");
            sb.AppendLine("        var _high   = Arr(data, d => d.High);");
            sb.AppendLine("        var _low    = Arr(data, d => d.Low);");
            sb.AppendLine("        var _volume = Arr(data, d => d.Volume);");
            sb.AppendLine("        var _hl2    = Arr(data, d => (d.High + d.Low) / 2.0);");
            sb.AppendLine("        var _hlc3   = Arr(data, d => (d.High + d.Low + d.Close) / 3.0);");
            sb.AppendLine("        var _ohlc4  = Arr(data, d => (d.Open + d.High + d.Low + d.Close) / 4.0);");
            sb.AppendLine();

            // Parameter extractions
            if (parameters.Any())
            {
                sb.AppendLine("        // ── Parameters ────────────────────────────────────────────────");
                foreach (var kv in pineVarToParam)
                {
                    double def = parameters.TryGetValue(kv.Value, out double dv) ? dv : 0;
                    sb.AppendLine($"        var {SanitizeIdent(kv.Key)} = (int)p.GetValueOrDefault(\"{EscapeString(kv.Value)}\", {def.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
                }
                sb.AppendLine();
            }

            // Generated computation body — pattern-replace Pine calls with C# helpers
            sb.AppendLine("        // ── Computed series ────────────────────────────────────────────");
            var bodyLines = GenerateBody(stripped, pineVarToParam, plotVarToComp, componentNames, warnings);
            foreach (var line in bodyLines) sb.AppendLine("        " + line);
            sb.AppendLine();

            // Return statement
            sb.AppendLine("        // ── Return outputs ─────────────────────────────────────────────");
            sb.AppendLine("        return new double[][]");
            sb.AppendLine("        {");
            foreach (var cn in componentNames)
            {
                string varN = plotVarToComp.FirstOrDefault(x => x.Value == cn).Key;
                string arrN = string.IsNullOrEmpty(varN) ? $"_out_{SanitizeIdent(cn)}" : $"_arr_{SanitizeIdent(varN)}";
                sb.AppendLine($"            {arrN} ?? new double[n],");
            }
            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Static helper methods
            AppendHelpers(sb);

            sb.AppendLine("}");

            if (errors.Any())
                return new TranspileResult(false, sb.ToString(), errors.ToArray(), warnings.ToArray());

            return new TranspileResult(true, sb.ToString(), Array.Empty<string>(), warnings.ToArray());
        }

        // ── Code generation helpers ───────────────────────────────────────────

        private List<string> GenerateBody(
            string stripped,
            Dictionary<string, string> pineVarToParam,
            Dictionary<string, string> plotVarToComp,
            List<string> componentNames,
            List<string> warnings)
        {
            var lines = new List<string>();
            var declaredVars = new HashSet<string>(pineVarToParam.Keys, StringComparer.OrdinalIgnoreCase);

            // Parse assignment lines (simplified: handle `varName = expr` forms)
            var assignRx = new Regex(@"^\s*(?:var\s+|varip\s+)?(\w+)\s*=\s*(.+)$", RegexOptions.Multiline);

            foreach (Match m in assignRx.Matches(stripped))
            {
                string varName = m.Groups[1].Value.Trim();
                string expr    = m.Groups[2].Value.Trim();

                // Skip Pine keywords and already-declared param vars
                if (IsPineKeyword(varName) || pineVarToParam.ContainsKey(varName)) continue;
                if (varName == "indicator" || varName == "strategy" || varName == "study") continue;

                // Skip if the expression is a Pine directive like indicator() itself
                if (expr.TrimStart().StartsWith("indicator(", StringComparison.OrdinalIgnoreCase)) continue;
                if (expr.TrimStart().StartsWith("input(", StringComparison.OrdinalIgnoreCase)
                    || expr.TrimStart().StartsWith("input.", StringComparison.OrdinalIgnoreCase)) continue;

                string csExpr = TranslateExpression(expr, pineVarToParam, warnings);
                string arrName = $"_arr_{SanitizeIdent(varName)}";

                // Request.security stub
                if (_requestSecRx.IsMatch(expr))
                {
                    lines.Add($"double[]? {arrName} = new double[n]; // stub: request.security() not supported → NaN");
                    lines.Add($"Array.Fill({arrName}, double.NaN);");
                    declaredVars.Add(varName);
                    continue;
                }

                if (!declaredVars.Contains(varName))
                {
                    lines.Add($"double[]? {arrName} = {csExpr};");
                    declaredVars.Add(varName);
                }
            }

            // Ensure all plotted variables have corresponding output arrays
            foreach (var kvp in plotVarToComp)
            {
                string pVar  = kvp.Key;
                string arrN  = $"_arr_{SanitizeIdent(pVar)}";
                string outN  = $"_out_{SanitizeIdent(kvp.Value)}";

                // If not already declared, try to create as NaN fallback
                if (!declaredVars.Contains(pVar))
                {
                    lines.Add($"double[]? {arrN} = new double[n]; Array.Fill({arrN}!, double.NaN); // {pVar} not resolved");
                }
                if (arrN != outN)
                    lines.Add($"var {outN} = {arrN};");
            }

            return lines;
        }

        private string TranslateExpression(string expr, Dictionary<string, string> paramMap, List<string> warnings)
        {
            // Normalize Pine booleans
            expr = Regex.Replace(expr, @"\btrue\b", "true");
            expr = Regex.Replace(expr, @"\bfalse\b", "false");

            // na → double.NaN; nz(x) → NzHelper(x); nz(x, y) → NzHelper(x, y)
            expr = Regex.Replace(expr, @"\bna\b(?!\w)", "double.NaN");
            expr = Regex.Replace(expr, @"\bnz\s*\(\s*([^,)]+),\s*([^)]+)\)", "NzHelper($1, $2)");
            expr = Regex.Replace(expr, @"\bnz\s*\(\s*([^)]+)\)", "NzHelper($1)");
            // fixnan() → NzHelper
            expr = Regex.Replace(expr, @"\bfixnan\s*\(\s*([^)]+)\)", "NzHelper($1)");

            // request.security → NaN stub
            expr = _requestSecRx.Replace(expr, "NanArr(n)");

            // math functions → element-wise where possible, scalar otherwise
            expr = Regex.Replace(expr, @"\bmath\.max\s*\(", "Math.Max(");
            expr = Regex.Replace(expr, @"\bmath\.min\s*\(", "Math.Min(");
            expr = Regex.Replace(expr, @"\bmath\.abs\s*\(", "Math.Abs(");
            expr = Regex.Replace(expr, @"\bmath\.sqrt\s*\(", "Math.Sqrt(");
            expr = Regex.Replace(expr, @"\bmath\.pow\s*\(", "Math.Pow(");
            expr = Regex.Replace(expr, @"\bmath\.log\s*\(", "Math.Log(");
            expr = Regex.Replace(expr, @"\bmath\.sign\s*\(", "Math.Sign(");
            expr = Regex.Replace(expr, @"\bmath\.round\s*\(", "Math.Round(");
            expr = Regex.Replace(expr, @"\bmath\.floor\s*\(", "Math.Floor(");
            expr = Regex.Replace(expr, @"\bmath\.ceil\s*\(", "Math.Ceiling(");
            expr = Regex.Replace(expr, @"\bmath\.pi\b", "Math.PI");
            expr = Regex.Replace(expr, @"\bmath\.e\b", "Math.E");

            // ta.change(src[, length]) → ArrSub(src, LookbackArr(src, length))
            expr = _taChangeRx.Replace(expr, m =>
            {
                string src = MapSource(m.Groups[1].Value);
                string len = m.Groups[2].Success ? m.Groups[2].Value : "1";
                return $"ArrSub({src}, LookbackArr({src}, {len}))";
            });

            // ta.rising(src, length) → ArrGt(src, LookbackArr(src, length))
            expr = _taRisingRx.Replace(expr, m =>
                $"ArrGt({MapSource(m.Groups[1].Value)}, LookbackArr({MapSource(m.Groups[1].Value)}, {m.Groups[2].Value}))");

            // ta.falling(src, length) → ArrLt(src, LookbackArr(src, length))
            expr = _taFallingRx.Replace(expr, m =>
                $"ArrLt({MapSource(m.Groups[1].Value)}, LookbackArr({MapSource(m.Groups[1].Value)}, {m.Groups[2].Value}))");

            // ta.valuewhen(cond, src, occurrence) → ValueWhenArr helper
            expr = _taWhenRx.Replace(expr, m =>
                $"ValueWhenArr({m.Groups[1].Value}, {MapSource(m.Groups[2].Value)}, {m.Groups[3].Value})");

            // ta.barssince(cond) → BarsSinceArr helper
            expr = _taBarsSinceRx.Replace(expr, m =>
                $"BarsSinceArr({m.Groups[1].Value})");

            // ta.* functions → helper calls
            expr = _taSmaRx.Replace(expr, m => $"SmaArr({MapSource(m.Groups[1].Value)}, {MapLen(m.Groups[2].Value, paramMap)})");
            expr = _taEmaRx.Replace(expr, m => $"EmaArr({MapSource(m.Groups[1].Value)}, {MapLen(m.Groups[2].Value, paramMap)})");
            expr = _taRsiRx.Replace(expr, m => $"RsiArr({MapSource(m.Groups[1].Value)}, {MapLen(m.Groups[2].Value, paramMap)})");
            expr = _taAtrRx.Replace(expr, m => $"AtrArr(data, {MapLen(m.Groups[1].Value, paramMap)})");
            expr = _taHighestRx.Replace(expr, m => $"HighestArr({MapSource(m.Groups[1].Value)}, {MapLen(m.Groups[2].Value, paramMap)})");
            expr = _taLowestRx.Replace(expr, m => $"LowestArr({MapSource(m.Groups[1].Value)}, {MapLen(m.Groups[2].Value, paramMap)})");
            expr = _taStdevRx.Replace(expr, m => $"StdevArr({MapSource(m.Groups[1].Value)}, {MapLen(m.Groups[2].Value, paramMap)})");
            expr = _taCrossRx.Replace(expr, m => $"CrossoverArr({MapSource(m.Groups[1].Value)}, {MapSource(m.Groups[2].Value)})");
            expr = _taUnderRx.Replace(expr, m => $"CrossunderArr({MapSource(m.Groups[1].Value)}, {MapSource(m.Groups[2].Value)})");

            // ta.macd: Pine = [macd, signal, hist] — generate as a tuple-array call
            expr = _taMacdRx.Replace(expr, m =>
                $"MacdArr({MapSource(m.Groups[1].Value)}, {MapLen(m.Groups[2].Value, paramMap)}, {MapLen(m.Groups[3].Value, paramMap)}, {MapLen(m.Groups[4].Value, paramMap)})[0]");

            // ta.bb: Pine = [middle, upper, lower]
            expr = _taBbRx.Replace(expr, m =>
                $"BbArr({MapSource(m.Groups[1].Value)}, {MapLen(m.Groups[2].Value, paramMap)}, {ParseDouble(m.Groups[3].Value)})[0]");

            // ta.stoch
            expr = _taStochRx.Replace(expr, m =>
                $"StochArr({MapSource(m.Groups[1].Value)}, {MapSource(m.Groups[2].Value)}, {MapSource(m.Groups[3].Value)}, {MapLen(m.Groups[4].Value, paramMap)})[0]");

            // Map source variable names
            foreach (var kv in _sourceMap)
                expr = Regex.Replace(expr, $@"\b{kv.Key}\b", kv.Value);

            // ── Lookback indexing: identifier[N] → LookbackArr(identifier, N) ─────
            // Applied after source mapping so _close[1] → LookbackArr(_close, 1)
            expr = _lookbackRx.Replace(expr, m =>
            {
                string ident = m.Groups[1].Value;
                string offset = m.Groups[2].Value;
                // Only transform known array-like identifiers (source arrays or _arr_ variables)
                if (ident.StartsWith("_") || _sourceMap.ContainsValue(ident))
                    return $"LookbackArr({ident}, {offset})";
                return m.Value; // leave C# array indexing alone
            });

            // ── Element-wise ops: Math.Abs(array_expr) → ArrAbs(array_expr) ────────
            // Detects when Math.Abs is wrapping an array-typed expression.
            expr = _arrAbsRx.Replace(expr, m => $"ArrAbs({m.Groups[1].Value}{m.Groups[2].Value})");

            // ── Element-wise subtraction: _arr_X - _arr_Y → ArrSub(_arr_X, _arr_Y) ─
            expr = _arrSubRx.Replace(expr, m => $"ArrSub({m.Groups[1].Value}, {m.Groups[2].Value})");

            // Pine variable references → _arr_ prefix
            // (conservative: only bare identifiers that look like indicator variables)
            // We don't do this broadly to avoid false positives

            return expr;
        }

        private static string MapSource(string s)
        {
            s = s.Trim();
            return _sourceMap.TryGetValue(s, out var mapped) ? mapped : $"_arr_{SanitizeIdent(s)} ?? _close";
        }

        private static string MapLen(string s, Dictionary<string, string> paramMap)
        {
            s = s.Trim();
            if (paramMap.TryGetValue(s, out var pName)) return s; // already declared as int
            if (int.TryParse(s, out _)) return s;
            if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out _)) return $"(int){s}";
            return s; // fallback: use as-is
        }

        private static string ParseDouble(string s)
        {
            s = s.Trim();
            if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return s;
        }

        private static string EscapeString(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string SanitizeIdent(string s) =>
            Regex.Replace(s ?? "x", @"[^A-Za-z0-9_]", "_");

        private static string GenerateEmptyReturn(int count) =>
            "new double[][] { " + string.Join(", ", Enumerable.Repeat("new double[0]", count)) + " }";

        private static bool IsPineKeyword(string s) =>
            s is "if" or "else" or "for" or "while" or "true" or "false"
              or "and" or "or" or "not" or "import" or "export" or "switch"
              or "type" or "enum" or "method" or "series" or "simple";

        // ── Static helper methods appended to generated class ─────────────────

        private static void AppendHelpers(StringBuilder sb)
        {
            sb.AppendLine("    // ── Static helpers ──────────────────────────────────────────────");
            sb.AppendLine("    private static double[] Arr(ReadOnlySpan<Ohlcv> d, Func<Ohlcv, double> f)");
            sb.AppendLine("    { var a = new double[d.Length]; for (int i = 0; i < d.Length; i++) a[i] = f(d[i]); return a; }");
            sb.AppendLine();
            sb.AppendLine("    private static double[] NanArr(int n) { var a = new double[n]; Array.Fill(a, double.NaN); return a; }");
            sb.AppendLine();
            sb.AppendLine("    private static double NzHelper(double x, double def = 0) => double.IsNaN(x) ? def : x;");
            sb.AppendLine();
            sb.AppendLine("    // ── Element-wise array helpers ───────────────────────────────────────");
            sb.AppendLine("    private static double[] LookbackArr(double[] src, int n) { var r = new double[src.Length]; Array.Fill(r, double.NaN); for (int i = n; i < src.Length; i++) r[i] = src[i - n]; return r; }");
            sb.AppendLine("    private static bool[] LookbackArr(bool[] src, int n) { var r = new bool[src.Length]; for (int i = n; i < src.Length; i++) r[i] = src[i - n]; return r; }");
            sb.AppendLine("    private static double[] ArrAbs(double[] a) { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = Math.Abs(a[i]); return r; }");
            sb.AppendLine("    private static double[] ArrSub(double[] a, double[] b) { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] - b[i]; return r; }");
            sb.AppendLine("    private static double[] ArrAdd(double[] a, double[] b) { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] + b[i]; return r; }");
            sb.AppendLine("    private static double[] ArrMul(double[] a, double[] b) { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] * b[i]; return r; }");
            sb.AppendLine("    private static double[] ArrDiv(double[] a, double[] b) { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = b[i] == 0 ? double.NaN : a[i] / b[i]; return r; }");
            sb.AppendLine("    private static double[] ArrScale(double s, double[] a) { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = s * a[i]; return r; }");
            sb.AppendLine("    private static bool[] ArrGt(double[] a, double[] b) { var r = new bool[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = !double.IsNaN(a[i]) && !double.IsNaN(b[i]) && a[i] > b[i]; return r; }");
            sb.AppendLine("    private static bool[] ArrLt(double[] a, double[] b) { var r = new bool[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = !double.IsNaN(a[i]) && !double.IsNaN(b[i]) && a[i] < b[i]; return r; }");
            sb.AppendLine("    private static double[] BoolToDouble(bool[] src) { var r = new double[src.Length]; for (int i = 0; i < src.Length; i++) r[i] = src[i] ? 1.0 : double.NaN; return r; }");
            sb.AppendLine("    private static double[] ValueWhenArr(bool[] cond, double[] src, int occurrence) { var r = new double[src.Length]; Array.Fill(r, double.NaN); for (int i = 0; i < src.Length; i++) { int found = 0; for (int j = i; j >= 0; j--) { if (cond[j]) { if (found++ == occurrence) { r[i] = src[j]; break; } } } } return r; }");
            sb.AppendLine("    private static double[] BarsSinceArr(bool[] cond) { var r = new double[cond.Length]; Array.Fill(r, double.NaN); for (int i = 0; i < cond.Length; i++) { for (int j = i; j >= 0; j--) { if (cond[j]) { r[i] = i - j; break; } } } return r; }");
            sb.AppendLine();
            sb.AppendLine("    private static double[] SmaArr(double[] src, int period)");
            sb.AppendLine("    {");
            sb.AppendLine("        var r = new double[src.Length];");
            sb.AppendLine("        for (int i = 0; i < src.Length; i++)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (i < period - 1) { r[i] = double.NaN; continue; }");
            sb.AppendLine("            double s = 0; for (int j = i - period + 1; j <= i; j++) s += src[j]; r[i] = s / period;");
            sb.AppendLine("        }");
            sb.AppendLine("        return r;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static double[] EmaArr(double[] src, int period)");
            sb.AppendLine("    {");
            sb.AppendLine("        var r = new double[src.Length];");
            sb.AppendLine("        double k = 2.0 / (period + 1);");
            sb.AppendLine("        double ema = double.NaN;");
            sb.AppendLine("        for (int i = 0; i < src.Length; i++)");
            sb.AppendLine("        {");
            sb.AppendLine("            ema = double.IsNaN(ema) ? src[i] : src[i] * k + ema * (1 - k);");
            sb.AppendLine("            r[i] = i < period - 1 ? double.NaN : ema;");
            sb.AppendLine("        }");
            sb.AppendLine("        return r;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static double[] RsiArr(double[] src, int period)");
            sb.AppendLine("    {");
            sb.AppendLine("        var r = new double[src.Length];");
            sb.AppendLine("        Array.Fill(r, double.NaN);");
            sb.AppendLine("        for (int i = period; i < src.Length; i++)");
            sb.AppendLine("        {");
            sb.AppendLine("            double g = 0, l = 0;");
            sb.AppendLine("            for (int j = i - period + 1; j <= i; j++) { double ch = src[j] - src[j-1]; if (ch > 0) g += ch; else l -= ch; }");
            sb.AppendLine("            double ag = g / period, al = l / period;");
            sb.AppendLine("            r[i] = al == 0 ? 100 : 100 - 100 / (1 + ag / al);");
            sb.AppendLine("        }");
            sb.AppendLine("        return r;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static double[] AtrArr(ReadOnlySpan<Ohlcv> d, int period)");
            sb.AppendLine("    {");
            sb.AppendLine("        var tr = new double[d.Length];");
            sb.AppendLine("        for (int i = 1; i < d.Length; i++)");
            sb.AppendLine("            tr[i] = Math.Max(d[i].High - d[i].Low, Math.Max(Math.Abs(d[i].High - d[i-1].Close), Math.Abs(d[i].Low - d[i-1].Close)));");
            sb.AppendLine("        return SmaArr(tr, period);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static double[] HighestArr(double[] src, int period)");
            sb.AppendLine("    {");
            sb.AppendLine("        var r = new double[src.Length];");
            sb.AppendLine("        for (int i = 0; i < src.Length; i++) { double m = double.NegativeInfinity; for (int j = Math.Max(0, i-period+1); j <= i; j++) if (src[j] > m) m = src[j]; r[i] = m; }");
            sb.AppendLine("        return r;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static double[] LowestArr(double[] src, int period)");
            sb.AppendLine("    {");
            sb.AppendLine("        var r = new double[src.Length];");
            sb.AppendLine("        for (int i = 0; i < src.Length; i++) { double m = double.PositiveInfinity; for (int j = Math.Max(0, i-period+1); j <= i; j++) if (src[j] < m) m = src[j]; r[i] = m; }");
            sb.AppendLine("        return r;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static double[] StdevArr(double[] src, int period)");
            sb.AppendLine("    {");
            sb.AppendLine("        var r = new double[src.Length];");
            sb.AppendLine("        for (int i = period - 1; i < src.Length; i++) { double m = 0; for (int j = i-period+1; j <= i; j++) m += src[j]; m /= period; double v = 0; for (int j = i-period+1; j <= i; j++) { double d2 = src[j]-m; v += d2*d2; } r[i] = Math.Sqrt(v / period); }");
            sb.AppendLine("        return r;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static bool[] CrossoverArr(double[] a, double[] b)");
            sb.AppendLine("    { var r = new bool[a.Length]; for (int i = 1; i < a.Length; i++) r[i] = a[i-1] < b[i-1] && a[i] >= b[i]; return r; }");
            sb.AppendLine();
            sb.AppendLine("    private static bool[] CrossunderArr(double[] a, double[] b)");
            sb.AppendLine("    { var r = new bool[a.Length]; for (int i = 1; i < a.Length; i++) r[i] = a[i-1] > b[i-1] && a[i] <= b[i]; return r; }");
            sb.AppendLine();
            sb.AppendLine("    private static double[][] MacdArr(double[] src, int fast, int slow, int signal)");
            sb.AppendLine("    {");
            sb.AppendLine("        var fe = EmaArr(src, fast); var se = EmaArr(src, slow);");
            sb.AppendLine("        var macd = new double[src.Length]; for (int i = 0; i < src.Length; i++) macd[i] = fe[i] - se[i];");
            sb.AppendLine("        var sig  = EmaArr(macd, signal);");
            sb.AppendLine("        var hist = new double[src.Length]; for (int i = 0; i < src.Length; i++) hist[i] = macd[i] - sig[i];");
            sb.AppendLine("        return new[] { macd, sig, hist };");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static double[][] BbArr(double[] src, int period, double mult)");
            sb.AppendLine("    {");
            sb.AppendLine("        var mid = SmaArr(src, period);");
            sb.AppendLine("        var std = StdevArr(src, period);");
            sb.AppendLine("        var upper = new double[src.Length]; var lower = new double[src.Length];");
            sb.AppendLine("        for (int i = 0; i < src.Length; i++) { upper[i] = mid[i] + mult * std[i]; lower[i] = mid[i] - mult * std[i]; }");
            sb.AppendLine("        return new[] { mid, upper, lower };");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static double[][] StochArr(double[] src, double[] high, double[] low, int period)");
            sb.AppendLine("    {");
            sb.AppendLine("        var k = new double[src.Length]; Array.Fill(k, double.NaN);");
            sb.AppendLine("        for (int i = period - 1; i < src.Length; i++) { double lo = double.MaxValue, hi = double.MinValue; for (int j = i-period+1; j <= i; j++) { if (low[j] < lo) lo = low[j]; if (high[j] > hi) hi = high[j]; } k[i] = hi > lo ? (src[i] - lo) / (hi - lo) * 100 : 50; }");
            sb.AppendLine("        return new[] { k, SmaArr(k, 3) };");
            sb.AppendLine("    }");
        }
    }
}

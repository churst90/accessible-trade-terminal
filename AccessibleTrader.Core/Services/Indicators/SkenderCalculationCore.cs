using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Skender.Stock.Indicators;

namespace AccessibleTrader.Core.Services.Indicators
{
    internal static class SkenderCalculationCore
    {
        internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<object[], object>> _delegateCache
            = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, MethodInfo> _methodCache
            = new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Concurrent.ConcurrentStack<Quote> _quotePool = new();

        internal static int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int maxPeriod = 200;
            int foundPeriod = 14;
            foreach (var p in parameters)
            {
                if (p.Key.Contains("Period", StringComparison.OrdinalIgnoreCase) ||
                    p.Key.Contains("Lookback", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(p.Value?.ToString(), out int period))
                        foundPeriod = Math.Max(foundPeriod, period);
                }
            }
            return Math.Min(maxPeriod, (int)(foundPeriod * 2.5));
        }

        internal static void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            ResolveDelegate(code, out var compiledDelegate, out var methodInfo);
            if (compiledDelegate == null || methodInfo == null) return;

            var quotes = System.Buffers.ArrayPool<Quote>.Shared.Rent(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                if (!_quotePool.TryPop(out var quote)) quote = new Quote();
                var x = data[i];
                quote.Date   = x.Date;
                quote.Open   = (decimal)x.Open;
                quote.High   = (decimal)x.High;
                quote.Low    = (decimal)x.Low;
                quote.Close  = (decimal)x.Close;
                quote.Volume = (decimal)x.Volume;
                quotes[i] = quote;
            }

            try
            {
                var methodParams = methodInfo.GetParameters();
                object[] args = new object[methodParams.Length];
                args[0] = new ArraySegment<Quote>(quotes, 0, data.Length);

                for (int i = 1; i < methodParams.Length; i++)
                {
                    var p = methodParams[i];
                    parameters.TryGetValue(p.Name ?? "", out var val);
                    if (val != null)
                    {
                        // Skender's OPTIONAL parameters are Nullable<T> — smaPeriods, signalPeriods
                        // and friends. Convert.ChangeType THROWS on a Nullable target, so every one
                        // of them landed in the catch below and fell back to its null default: the
                        // series it controls was computed as all-null and the line the user had
                        // added rendered empty, with the parameter sitting in the UI doing nothing.
                        // Unwrap to the underlying type before converting.
                        var targetType = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
                        try
                        {
                            if (targetType.IsEnum && val is string sv)
                                args[i] = Enum.Parse(targetType, sv, true);
                            else if (targetType.IsEnum)
                                args[i] = Enum.ToObject(targetType, val);
                            else
                                args[i] = Convert.ChangeType(val, targetType);
                        }
                        catch
                        {
                            args[i] = p.HasDefaultValue
                                ? p.DefaultValue!
                                : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)! : null!);
                        }
                    }
                    else
                    {
                        args[i] = p.HasDefaultValue
                            ? p.DefaultValue!
                            : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)! : null!);
                    }
                }

                var results = (IEnumerable)compiledDelegate(args);

                int idx = 0;
                PropertyInfo[]? props = null;
                foreach (var item in results)
                {
                    if (props == null)
                    {
                        props = item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.Name != "Date" && p.Name != "Open" && p.Name != "High"
                                     && p.Name != "Low"  && p.Name != "Close" && p.Name != "Volume")
                            .ToArray();
                    }
                    foreach (var prop in props)
                    {
                        var span = buffer.GetComponentSpan(prop.Name);
                        if (idx < span.Length)
                        {
                            var v = prop.GetValue(item);
                            span[idx] = v == null ? double.NaN : Convert.ToDouble(v);
                        }
                    }
                    idx++;
                }

                string c = code.ToUpperInvariant();
                if (c is "BB" or "BOLLINGERBANDS")
                {
                    var upper   = buffer.GetComponentSpan("UpperBand");
                    var lower   = buffer.GetComponentSpan("LowerBand");
                    var squeeze = buffer.GetComponentSpan("__SQUEEZE");
                    if (upper.Length > 0 && lower.Length > 0 && squeeze.Length > 0)
                    {
                        for (int i = 0; i < upper.Length; i++)
                        {
                            if (i < 20) { squeeze[i] = 0; continue; }
                            double width = upper[i] - lower[i];
                            double sumW = 0; int cnt = 0;
                            for (int j = i - 20; j < i; j++)
                            {
                                if (!double.IsNaN(upper[j]) && !double.IsNaN(lower[j]))
                                { sumW += upper[j] - lower[j]; cnt++; }
                            }
                            if (cnt > 0)
                            {
                                double avg = sumW / cnt;
                                if      (width < avg * 0.6) squeeze[i] = 1;
                                else if (width > avg * 1.5) squeeze[i] = 2;
                            }
                        }
                    }
                }
                else if (c == "MACD")
                {
                    var macd      = buffer.GetComponentSpan("Macd");
                    var signal    = buffer.GetComponentSpan("Signal");
                    var crossover = buffer.GetComponentSpan("__CROSSOVER");
                    if (macd.Length > 0 && signal.Length > 0 && crossover.Length > 0)
                    {
                        for (int i = 1; i < macd.Length; i++)
                        {
                            if (double.IsNaN(macd[i - 1]) || double.IsNaN(signal[i - 1])) continue;
                            if (macd[i - 1] <= signal[i - 1] && macd[i] > signal[i]) crossover[i] = 1;
                            else if (macd[i - 1] >= signal[i - 1] && macd[i] < signal[i]) crossover[i] = 2;
                        }
                    }
                }
            }
            finally
            {
                for (int i = 0; i < data.Length; i++) _quotePool.Push(quotes[i]);
                System.Buffers.ArrayPool<Quote>.Shared.Return(quotes, clearArray: true);
            }
        }

        internal static void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int windowSize = GetStabilityWindow(code, parameters);
            var slicedData = data.Length > windowSize ? data.Slice(data.Length - windowSize) : data;
            using var tempBuffer = new InternalResultBuffer(windowSize);
            Calculate(code, slicedData, parameters, tempBuffer);
            foreach (var componentName in tempBuffer.ComponentNames)
            {
                var val = tempBuffer.GetComponentSpan(componentName)[^1];
                buffer.SetValue(componentName, data.Length - 1, val);
            }
        }

        private static void ResolveDelegate(string code, out Func<object[], object>? compiledDelegate, out MethodInfo? methodInfo)
        {
            if (_delegateCache.TryGetValue(code, out compiledDelegate) &&
                _methodCache.TryGetValue(code, out methodInfo))
                return;

            if (!_methodCache.TryGetValue(code, out methodInfo) || methodInfo.ContainsGenericParameters)
            {
                var assembly = typeof(Quote).Assembly;
                string methodName = "Get" + SkenderMethodName(code);
                var potentialMethods = assembly.GetExportedTypes()
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    .Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var genericMethod = potentialMethods.FirstOrDefault(m => m.IsGenericMethodDefinition);
                methodInfo = genericMethod != null
                    ? genericMethod.MakeGenericMethod(typeof(Quote))
                    : potentialMethods.FirstOrDefault();

                if (methodInfo == null) { compiledDelegate = null; return; }
                _methodCache[code] = methodInfo;
            }

            compiledDelegate = CreateDelegate(methodInfo);
            _delegateCache[code] = compiledDelegate;
        }

        /// <summary>
        /// Whether Skender exposes a calculation for this code at all. Used by
        /// <c>IndicatorService.GetAvailableIndicators</c> so an indicator that could only ever draw
        /// an empty line is never offered in the first place.
        /// </summary>
        internal static bool CanResolve(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            string methodName = "Get" + SkenderMethodName(code);
            return typeof(Quote).Assembly.GetExportedTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Any(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Our indicator <c>Code</c> to the name Skender actually publishes.
        ///
        /// <para>
        /// The lookup is <c>"Get" + code</c>, and where the two disagree the reflection finds
        /// nothing, the delegate is null, and the indicator draws an empty line — no exception, no
        /// log, nothing for the user to report beyond "it does not work". Bollinger Bands was one
        /// of these, and it is one of the seven indicators the public demo offers by name.
        /// </para>
        ///
        /// <para>
        /// Only aliases that are the SAME indicator under another name belong here. An indicator
        /// Skender does not implement must not be registered at all — a menu entry that can never
        /// produce a value is worse than an absent one, because the user spends their time on it.
        /// </para>
        /// </summary>
        internal static string SkenderMethodName(string code) => (code ?? "").ToUpperInvariant() switch
        {
            "BB"             => "BollingerBands",
            "KC"             => "Keltner",
            "CHANDELIEREXIT" => "Chandelier",
            "ULTOSC"         => "Ultimate",
            // Momentum is the first column of Skender's Rate-of-Change result.
            "MOM"            => "Roc",
            _                => code ?? "",
        };

        internal static Func<object[], object> CreateDelegate(MethodInfo method)
        {
            var paramsArray  = Expression.Parameter(typeof(object[]), "params");
            var methodParams = method.GetParameters();
            var castParams   = new Expression[methodParams.Length];
            for (int i = 0; i < methodParams.Length; i++)
            {
                var index    = Expression.Constant(i);
                var accessor = Expression.ArrayIndex(paramsArray, index);
                castParams[i] = Expression.Convert(accessor, methodParams[i].ParameterType);
            }
            var call   = Expression.Call(method, castParams);
            var lambda = Expression.Lambda<Func<object[], object>>(Expression.Convert(call, typeof(object)), paramsArray);
            return lambda.Compile();
        }

        internal static Type? GetEnumerableItemType(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];
            return type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];
        }

        private sealed class InternalResultBuffer : IIndicatorResultBuffer, IDisposable
        {
            private readonly Dictionary<string, double[]> _data = new();
            private readonly int _length;
            internal IEnumerable<string> ComponentNames => _data.Keys;

            internal InternalResultBuffer(int length) { _length = length; }

            public Span<double> GetComponentSpan(string name)
            {
                if (!_data.TryGetValue(name, out var arr))
                {
                    arr = System.Buffers.ArrayPool<double>.Shared.Rent(_length);
                    _data[name] = arr;
                }
                return arr.AsSpan(0, _length);
            }

            public void SetValue(string name, int index, double value) => GetComponentSpan(name)[index] = value;

            public void WriteZoneBands(string indicatorCode, List<ZoneBandConfig> zoneBands) { }
            public IReadOnlyList<ZoneBandConfig> ReadZoneBands(string indicatorCode) => Array.Empty<ZoneBandConfig>();

            public void Dispose()
            {
                foreach (var arr in _data.Values)
                    System.Buffers.ArrayPool<double>.Shared.Return(arr);
            }
        }
    }
}

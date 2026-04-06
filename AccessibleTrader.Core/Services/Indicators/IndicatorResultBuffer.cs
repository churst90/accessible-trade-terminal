using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// A reusable, high-performance buffer for indicator calculation results.
    /// Wraps a dictionary of arrays to fulfill the IIndicatorResultBuffer contract.
    /// Also carries dynamic zone bands written by providers via WriteZoneBands().
    ///
    /// After a full <c>Calculate()</c> pass the caller may call <see cref="Seal"/> to lock
    /// the set of registered component names.  Any subsequent <see cref="GetComponentSpan"/>
    /// or <see cref="SetValue"/> call with an unrecognised name then throws
    /// <see cref="ArgumentException"/>, turning silent NaN arrays (caused by typos) into
    /// loud, immediate errors.
    /// </summary>
    public class IndicatorResultBuffer : IIndicatorResultBuffer
    {
        private readonly Dictionary<string, double[]> _data;
        private readonly int _length;

        /// <summary>
        /// Dynamic zone bands written by providers during Calculate().
        /// Keyed by indicator code (upper-cased). Read by IndicatorOrchestrator after Calculate() returns.
        /// </summary>
        private Dictionary<string, List<ZoneBandConfig>>? _zoneBands;

        /// <summary>True after <see cref="Seal"/> is called; new component names are rejected.</summary>
        private bool _sealed;

        public IndicatorResultBuffer(Dictionary<string, double[]> existingData, int length)
        {
            _data = existingData ?? new Dictionary<string, double[]>();
            _length = length;
        }

        /// <summary>
        /// Locks the set of registered component names.  After this call,
        /// <see cref="GetComponentSpan"/> and <see cref="SetValue"/> will throw
        /// <see cref="ArgumentException"/> for any name that was not registered during
        /// the preceding <c>Calculate()</c> pass.
        /// </summary>
        public void Seal() => _sealed = true;

        private void ThrowIfUnknown(string componentName)
        {
            if (_sealed && !_data.ContainsKey(componentName))
            {
                var knownNames = string.Join(", ", _data.Keys);
                throw new ArgumentException(
                    $"Component name '{componentName}' was never registered in this buffer. " +
                    $"Valid registered names: [{knownNames}]. " +
                    "Check for typos in the provider's component name constant or metadata Name field.",
                    nameof(componentName));
            }
        }

        public Span<double> GetComponentSpan(string componentName)
        {
            ThrowIfUnknown(componentName);
            if (!_data.TryGetValue(componentName, out var array) || array.Length != _length)
            {
                array = new double[_length];
                _data[componentName] = array;
            }
            return array;
        }

        public void SetValue(string componentName, int index, double value)
        {
            ThrowIfUnknown(componentName);
            if (!_data.TryGetValue(componentName, out var array) || array.Length != _length)
            {
                array = new double[_length];
                _data[componentName] = array;
            }
            if (index >= 0 && index < array.Length)
            {
                array[index] = value;
            }
        }

        /// <inheritdoc/>
        public void WriteZoneBands(string indicatorCode, List<ZoneBandConfig> zoneBands)
        {
            _zoneBands ??= new Dictionary<string, List<ZoneBandConfig>>(StringComparer.OrdinalIgnoreCase);
            _zoneBands[indicatorCode] = zoneBands ?? new List<ZoneBandConfig>();
        }

        /// <inheritdoc/>
        public IReadOnlyList<ZoneBandConfig> ReadZoneBands(string indicatorCode)
        {
            if (_zoneBands != null && _zoneBands.TryGetValue(indicatorCode, out var bands))
                return bands;
            return System.Array.Empty<ZoneBandConfig>();
        }

        public Dictionary<string, double[]> GetResults() => _data;
    }
}

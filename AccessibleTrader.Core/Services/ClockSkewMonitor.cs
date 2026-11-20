using System.Net.Http;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Compares this computer's clock with the <c>Date</c> header on every venue's answer and says
    /// once, when they disagree, that the clock is off (code review, 2026-11-20).
    ///
    /// <para>Venues sign requests with a timestamp and refuse one whose time is too far from theirs
    /// (Binance's -1021, Coinbase's token lifetime, Kalshi's signed timestamp). Binance re-syncs
    /// itself; the others just refuse, with words that do not say "your clock". A wrong desktop clock
    /// also puts the newest bar in the future or the past. The answer's <c>Date</c> header is the
    /// venue-agnostic reference: every venue's web server sends one.</para>
    ///
    /// <para>False alarms are worse than none, so a warning needs <see cref="StreakNeeded"/> answers
    /// in a row all more than <see cref="WarnSeconds"/> off in the same direction; an answer that
    /// carries an <c>Age</c> header (a cached copy, whose Date is old) is not counted. The header is
    /// whole seconds and the round trip is measured, so the reading is good to about a second; the
    /// threshold is far above that. The figure said is the smallest of the streak.</para>
    ///
    /// <para>Desktop: spoken once a run through the error coordinator. Hosted: the server's own
    /// clock, so only logged, and reported by <c>/healthz/deep</c>.</para>
    /// </summary>
    public sealed class ClockSkewMonitor
    {
        public const double WarnSeconds = 15;
        public const int StreakNeeded = 3;

        private readonly IGlobalErrorCoordinator? _errors;
        private readonly ILogger<ClockSkewMonitor>? _logger;
        private readonly object _gate = new();
        private int _streak;
        private int _streakSign;
        private double _streakSmallest;
        private bool _warned;

        public ClockSkewMonitor(IGlobalErrorCoordinator? errors = null, ILogger<ClockSkewMonitor>? logger = null)
        {
            _errors = errors;
            _logger = logger;
        }

        /// <summary>The newest reading: how far this clock is ahead of the venue's (negative = behind).</summary>
        public ClockReading? Last { get; private set; }

        /// <summary>True once the warning has been given this run.</summary>
        public bool Warned { get { lock (_gate) return _warned; } }

        /// <summary>
        /// One answer: <paramref name="serverDate"/> from its Date header, sent and received on this
        /// computer's UTC clock. Seconds this clock is AHEAD of the venue's; null when not counted.
        /// </summary>
        public double? Observe(string source, DateTimeOffset? serverDate, bool cached, DateTime sentUtc, DateTime receivedUtc)
        {
            if (serverDate is null || cached || receivedUtc < sentUtc) return null;

            // The header is truncated to the second: the venue's true time is half a second later
            // on average. Compare with the middle of the round trip.
            var localMid = sentUtc + TimeSpan.FromTicks((receivedUtc - sentUtc).Ticks / 2);
            double ahead = (localMid - serverDate.Value.UtcDateTime).TotalSeconds - 0.5;

            string? say = null;
            lock (_gate)
            {
                Last = new ClockReading(source, ahead, receivedUtc);
                int sign = Math.Abs(ahead) > WarnSeconds ? Math.Sign(ahead) : 0;
                if (sign == 0) { _streak = 0; _streakSign = 0; return ahead; }
                if (sign != _streakSign) { _streak = 0; _streakSign = sign; _streakSmallest = double.MaxValue; }
                _streak++;
                _streakSmallest = Math.Min(_streakSmallest, Math.Abs(ahead));
                if (_streak >= StreakNeeded && !_warned)
                {
                    _warned = true;
                    say = Sentence(source, sign * _streakSmallest);
                }
            }

            if (say != null)
            {
                _logger?.LogWarning("Clock skew: {Message}", say);
                _errors?.ReportError(say, ErrorSeverity.High);
            }
            return ahead;
        }

        internal static string Sentence(string source, double ahead)
        {
            double secs = Math.Abs(ahead);
            string amount = secs >= 120 ? $"{Math.Round(secs / 60):0} minutes" : $"{Math.Round(secs):0} seconds";
            string way = ahead > 0 ? "fast" : "slow";
            return $"This computer's clock is about {amount} {way} next to {source}'s. "
                 + "Venues refuse signed orders when the clock is off, and the newest bar's time will be wrong. "
                 + "Set the clock to sync automatically.";
        }
    }

    public sealed record ClockReading(string Source, double SecondsAhead, DateTime ObservedUtc);

    /// <summary>
    /// Sits under a plugin's web client and hands every answer's Date header to the
    /// <see cref="ClockSkewMonitor"/>. Never changes, delays or fails a request.
    /// </summary>
    public sealed class ClockSkewHandler : DelegatingHandler
    {
        private readonly ClockSkewMonitor _monitor;
        private readonly string _source;

        public ClockSkewHandler(ClockSkewMonitor monitor, string source, HttpMessageHandler inner) : base(inner)
        {
            _monitor = monitor;
            _source = source;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = DateTime.UtcNow;
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            try
            {
                _monitor.Observe(_source, response.Headers.Date, response.Headers.Age is not null, sent, DateTime.UtcNow);
            }
            catch
            {
                // A clock reading must never cost a request.
            }
            return response;
        }
    }
}

using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IEventBus
    {
        IDisposable Subscribe<T>(Action<T> handler);
        void Publish<T>(T eventData);

        /// <summary>
        /// Returns an IObservable for the event type, allowing for reactive patterns like Throttle/Sample.
        /// </summary>
        IObservable<T> AsObservable<T>();

        /// <summary>
        /// Subscribe with Rx <c>Throttle</c> debouncing — only emits <paramref name="handler"/>
        /// after <paramref name="quietWindow"/> of silence on the stream. Useful for coalescing
        /// burst-fire events (<c>RedrawEvent</c>, <c>IndicatorUpdatedEvent</c>) where ten
        /// near-simultaneous publications collapse to a single actual re-render. Do NOT use
        /// for accessibility events (<c>FeedbackRequestEvent</c>, <c>AnnouncementEvent</c>) —
        /// a 50 ms debounce becomes a silent no-op in a key-repeat loop.
        /// </summary>
        IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan quietWindow);

        /// <summary>
        /// Subscribe with Rx <c>Sample</c> rate-limiting — emits the latest value per
        /// <paramref name="window"/> regardless of quiet periods. Useful for continuous
        /// high-frequency streams (mouse-move, scroll) where you need steady-state throttle
        /// and don't care about the tail.
        /// </summary>
        IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window);
    }

    public class EventBus : IEventBus, IDisposable
    {
        private readonly ConcurrentDictionary<Type, object> _subjects = new();
        private readonly ILogger<EventBus>? _logger;

        public EventBus() { }

        /// <summary>DI supplies the logger; the parameterless constructor keeps every existing
        /// <c>new EventBus()</c> in tests and hosts working.</summary>
        public EventBus(ILogger<EventBus> logger) => _logger = logger;

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            var subject = GetSubject<T>();
            return subject.AsObservable().Subscribe(Isolate(handler));
        }

        public void Publish<T>(T eventData)
        {
            if (eventData == null) return;
            GetSubject<T>().OnNext(eventData);
        }

        public IObservable<T> AsObservable<T>()
        {
            return GetSubject<T>().AsObservable();
        }

        public IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan quietWindow)
            => GetSubject<T>().AsObservable().Throttle(quietWindow).Subscribe(Isolate(handler));

        public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window)
            => GetSubject<T>().AsObservable().Sample(window).Subscribe(Isolate(handler));

        /// <summary>
        /// Wraps a handler so its exceptions cannot escape into the publisher or into the
        /// subscribers queued behind it.
        ///
        /// <para>
        /// <c>Subject&lt;T&gt;.OnNext</c> walks its observers on the publishing thread and stops
        /// at the first one that throws — so before this, a single broken handler did two things
        /// at once: it threw the exception back out of whatever called <c>Publish</c>, and it
        /// silently denied the event to every subscriber registered after it. Measured: with two
        /// subscribers and the first throwing, <c>Publish</c> threw and the second received
        /// nothing. On this bus that is a fill announcement, an earcon and a journal entry going
        /// missing together, from a fault in something unrelated.
        /// </para>
        ///
        /// <para>
        /// Rx also treats a throwing observer as a terminated subscription, which is why the
        /// second publish in that measurement reached the healthy subscriber — the broken one had
        /// been dropped. "It heals after losing one event" is not a design, it is the shape of an
        /// accident.
        /// </para>
        ///
        /// <para>
        /// The catch is not a swallow: in this app an unreported error is inaudible, so it goes to
        /// the log with the event type and the handler's target named. What it must not do is
        /// publish — an error event raised from inside event delivery is how a bus gets a loop.
        /// </para>
        /// </summary>
        private Action<T> Isolate<T>(Action<T> handler) => e =>
        {
            try
            {
                handler(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "An EventBus subscriber threw while handling {EventType}. Handler: {Handler}. "
                    + "Delivery to the other subscribers continued.",
                    typeof(T).Name,
                    handler.Method.DeclaringType?.FullName + "." + handler.Method.Name);
            }
        };

        public void Dispose()
        {
            foreach (var kvp in _subjects)
            {
                if (kvp.Value is IDisposable disposable)
                    disposable.Dispose();
            }
            _subjects.Clear();
        }

        private Subject<T> GetSubject<T>()
        {
            var type = typeof(T);
            return (Subject<T>)_subjects.GetOrAdd(type, _ => new Subject<T>());
        }
    }
}

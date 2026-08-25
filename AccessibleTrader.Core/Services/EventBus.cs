using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

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

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            var subject = GetSubject<T>();
            return subject.AsObservable().Subscribe(handler);
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
            => GetSubject<T>().AsObservable().Throttle(quietWindow).Subscribe(handler);

        public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window)
            => GetSubject<T>().AsObservable().Sample(window).Subscribe(handler);

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

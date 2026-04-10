using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AccessibleTrader.Sdk.Models;

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

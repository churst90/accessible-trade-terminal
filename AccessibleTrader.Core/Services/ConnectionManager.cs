using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Models;

namespace AccessibleTrader.Core.Services
{
    public interface IConnectionManager
    {
        void TrackProvider(IMarketDataProvider provider);
        void UntrackProvider(IMarketDataProvider provider);
        ConnectionState GetState(string providerName);
    }

    /// <summary>
    /// Tracks the connection state of various market data providers.
    /// Delegates all feedback to the EventBus via ConnectionStatusEvent.
    /// </summary>
    public class ConnectionManager : IConnectionManager, IDisposable
    {
        private readonly Dictionary<string, ConnectionState> _states = new();
        private readonly Dictionary<string, IDisposable> _subscriptions = new();
        private readonly IEventBus _eventBus;

        public ConnectionManager(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void TrackProvider(IMarketDataProvider provider)
        {
            if (_subscriptions.ContainsKey(provider.Name)) return;

            _states[provider.Name] = ConnectionState.Disconnected;
            _subscriptions[provider.Name] = provider.ConnectionStateStream.Subscribe(state => 
            {
                var oldState = _states.ContainsKey(provider.Name) ? _states[provider.Name] : ConnectionState.Disconnected;
                if (oldState != state)
                {
                    _states[provider.Name] = state;
                    NotifyStateChange(provider.Name, oldState, state);
                }
            });
        }

        public void UntrackProvider(IMarketDataProvider provider)
        {
            if (_subscriptions.TryGetValue(provider.Name, out var sub))
            {
                sub.Dispose();
                _subscriptions.Remove(provider.Name);
            }
            _states.Remove(provider.Name);
        }

        public ConnectionState GetState(string providerName)
        {
            return _states.TryGetValue(providerName, out var state) ? state : ConnectionState.Disconnected;
        }

        private void NotifyStateChange(string providerName, ConnectionState oldState, ConnectionState newState)
        {
            string message = newState switch
            {
                ConnectionState.Connected => $"{providerName} link established.",
                ConnectionState.Disconnected => $"{providerName} link disconnected.",
                ConnectionState.Connecting => $"Establishing link to {providerName}.",
                ConnectionState.Error => $"Link error with {providerName}.",
                _ => ""
            };

            if (!string.IsNullOrEmpty(message))
            {
                // Only announce "Connected" if it's a first-time connection or recovery.
                bool shouldAnnounce = true;
                if (newState == ConnectionState.Connected && (oldState != ConnectionState.Error && oldState != ConnectionState.Disconnected && oldState != ConnectionState.Connecting))
                {
                    shouldAnnounce = false;
                }

                if (shouldAnnounce)
                {
                    _eventBus.Publish(new ConnectionStatusEvent(providerName, newState, message));
                }
            }
        }

        public void Dispose()
        {
            foreach (var sub in _subscriptions.Values) sub.Dispose();
            _subscriptions.Clear();
        }
    }
}
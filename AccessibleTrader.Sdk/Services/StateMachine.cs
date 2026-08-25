using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace AccessibleTrader.Sdk.Services
{
    /// <summary>
    /// A lightweight, high-performance state machine for deterministic logic transitions.
    /// Designed to work with 'readonly record struct' states to minimize allocations.
    /// </summary>
    /// <typeparam name="TState">The type of the state (must be immutable).</typeparam>
    /// <typeparam name="TTrigger">The type of the trigger/command.</typeparam>
    public abstract class StateMachine<TState, TTrigger> : IDisposable
        where TState : notnull
        where TTrigger : notnull
    {
        private TState _currentState;
        private TState _previousState;
        private readonly BehaviorSubject<TState> _stateSubject;

        public TState CurrentState => _currentState;
        public TState PreviousState => _previousState;
        public IObservable<TState> StateChanged => _stateSubject.AsObservable();

        protected StateMachine(TState initialState)
        {
            _currentState = initialState;
            _previousState = initialState;
            _stateSubject = new BehaviorSubject<TState>(initialState);
        }

        /// <summary>
        /// Fires a trigger to attempt a state transition.
        /// </summary>
        /// <param name="trigger">The trigger to apply.</param>
        public void Fire(TTrigger trigger)
        {
            var newState = Transition(_currentState, trigger);
            
            if (!newState.Equals(_currentState))
            {
                _previousState = _currentState;
                _currentState = newState;
                _stateSubject.OnNext(newState);
                OnTransitioned(newState);
            }
        }

        /// <summary>
        /// Defines the transition logic. Use C# 'switch' expressions for deterministic mapping.
        /// </summary>
        protected abstract TState Transition(TState currentState, TTrigger trigger);

        /// <summary>
        /// Optional hook for side-effects after a successful transition.
        /// </summary>
        protected virtual void OnTransitioned(TState newState) { }

        public virtual void Dispose()
        {
            _stateSubject.OnCompleted();
            _stateSubject.Dispose();
        }
    }
}

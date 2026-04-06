using System;

namespace AccessibleTrader.Core.Services
{
    public interface IMainThreadService
    {
        void InvokeOnMainThread(Action action);
    }
}

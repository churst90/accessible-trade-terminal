using Microsoft.Maui.ApplicationModel;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.BlazorClient.Services
{
    public class MauiMainThreadService : IMainThreadService
    {
        public void InvokeOnMainThread(Action action)
        {
            if (MainThread.IsMainThread)
            {
                action();
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(action);
            }
        }
    }
}

using AccessibleTrader.Core.Services;

namespace AccessibleTrader.BlazorClient.Services
{
    public class BlazorInputService : IInputService
    {
        public event Action<string, bool, bool, bool>? KeyPressed;
        public event Action<double, double, string, double, double>? MouseEvent;

        public void ProcessKey(string key, bool shift, bool ctrl, bool alt)
        {
            KeyPressed?.Invoke(key, shift, ctrl, alt);
        }

        public void ProcessMouse(double x, double y, string type, double width, double height)
        {
            MouseEvent?.Invoke(x, y, type, width, height);
        }
    }
}

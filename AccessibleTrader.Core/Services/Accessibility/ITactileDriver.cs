using System.Threading.Tasks;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface ITactileDriver
    {
        bool IsConnected { get; }
        string DeviceName { get; }
        int DisplayWidth { get; }
        int DisplayHeight { get; }

        Task ConnectAsync();
        Task DisconnectAsync();
        
        /// <summary>
        /// Renders a window of a larger virtual canvas onto the physical device.
        /// </summary>
        Task RenderViewportAsync(bool[,] virtualCanvas, int startX, int startY);
    }
}
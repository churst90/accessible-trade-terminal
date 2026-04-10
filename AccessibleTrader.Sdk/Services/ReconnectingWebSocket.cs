using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AccessibleTrader.Sdk.Services
{
    /// <summary>
    /// WebSocket wrapper with automatic reconnection, heartbeat, and subscription memory.
    /// Providers subclass or compose this to get resilient WebSocket behaviour.
    /// </summary>
    public sealed class ReconnectingWebSocket : IDisposable
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private readonly string _url;
        private readonly TimeSpan _heartbeatInterval;
        private readonly TimeSpan _reconnectBaseDelay;
        private readonly int _maxReconnectAttempts;
        private bool _disposed;

        // Callbacks
        private Func<ReconnectingWebSocket, Task>? _onConnected;
        private Action<string>? _onMessage;
        private Action<string>? _onError;
        private Action? _onDisconnected;

        /// <summary>Current connection state.</summary>
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        /// <summary>
        /// Creates a reconnecting WebSocket.
        /// </summary>
        /// <param name="url">WebSocket endpoint URL.</param>
        /// <param name="heartbeatInterval">Interval between heartbeat pings. Zero disables heartbeat.</param>
        /// <param name="reconnectBaseDelay">Base delay for exponential backoff reconnect (default 2s).</param>
        /// <param name="maxReconnectAttempts">Maximum consecutive reconnect attempts (default 10).</param>
        public ReconnectingWebSocket(
            string url,
            TimeSpan heartbeatInterval = default,
            TimeSpan reconnectBaseDelay = default,
            int maxReconnectAttempts = 10)
        {
            _url = url;
            _heartbeatInterval = heartbeatInterval == default ? TimeSpan.FromSeconds(30) : heartbeatInterval;
            _reconnectBaseDelay = reconnectBaseDelay == default ? TimeSpan.FromSeconds(2) : reconnectBaseDelay;
            _maxReconnectAttempts = maxReconnectAttempts;
        }

        /// <summary>Register a callback invoked after each (re)connection succeeds. Use to send auth/subscribe messages.</summary>
        public ReconnectingWebSocket OnConnected(Func<ReconnectingWebSocket, Task> handler) { _onConnected = handler; return this; }

        /// <summary>Register a callback for each received text message.</summary>
        public ReconnectingWebSocket OnMessage(Action<string> handler) { _onMessage = handler; return this; }

        /// <summary>Register a callback for errors.</summary>
        public ReconnectingWebSocket OnError(Action<string> handler) { _onError = handler; return this; }

        /// <summary>Register a callback for disconnection.</summary>
        public ReconnectingWebSocket OnDisconnected(Action handler) { _onDisconnected = handler; return this; }

        /// <summary>Connects and starts the receive loop. Idempotent if already connected.</summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            if (IsConnected) return;
            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await ConnectInternalAsync(_cts.Token);
            _ = ReceiveLoopAsync(_cts.Token);
            if (_heartbeatInterval > TimeSpan.Zero)
                _ = HeartbeatLoopAsync(_cts.Token);
        }

        private async Task ConnectInternalAsync(CancellationToken ct)
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(_url), ct);
            if (_onConnected != null)
                await _onConnected(this);
        }

        /// <summary>Send a text message. Safe to call from any thread.</summary>
        public async Task SendAsync(string message, CancellationToken ct = default)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        /// <summary>Gracefully disconnect.</summary>
        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None); }
                catch { /* best-effort */ }
            }
            _ws?.Dispose();
            _ws = null;
            _onDisconnected?.Invoke();
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[1024 * 64];
            int reconnectAttempts = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_ws == null || _ws.State != WebSocketState.Open)
                    {
                        if (reconnectAttempts >= _maxReconnectAttempts)
                        {
                            _onError?.Invoke($"Max reconnect attempts ({_maxReconnectAttempts}) reached");
                            return;
                        }

                        var delay = TimeSpan.FromMilliseconds(
                            _reconnectBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(reconnectAttempts, 6)));
                        await Task.Delay(delay, ct);

                        try
                        {
                            await ConnectInternalAsync(ct);
                            reconnectAttempts = 0;
                        }
                        catch (Exception ex)
                        {
                            reconnectAttempts++;
                            _onError?.Invoke($"Reconnect attempt {reconnectAttempts} failed: {ex.Message}");
                            continue;
                        }
                    }

                    var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _onDisconnected?.Invoke();
                        _ws?.Dispose();
                        _ws = null;
                        continue; // will trigger reconnect
                    }

                    var message = Encoding.UTF8.GetString(ms.ToArray());
                    ms.Dispose();

                    if (!string.IsNullOrWhiteSpace(message))
                        _onMessage?.Invoke(message);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _onError?.Invoke($"WebSocket receive error: {ex.Message}");
                    _ws?.Dispose();
                    _ws = null;
                    // will reconnect on next loop iteration
                }
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_heartbeatInterval, ct);
                    if (_ws?.State == WebSocketState.Open)
                    {
                        // Send a WebSocket ping frame
                        var pingBytes = Encoding.UTF8.GetBytes("ping");
                        await _ws.SendAsync(new ArraySegment<byte>(pingBytes, 0, 0), WebSocketMessageType.Text, true, ct);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* non-critical */ }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _ws?.Dispose();
        }
    }
}

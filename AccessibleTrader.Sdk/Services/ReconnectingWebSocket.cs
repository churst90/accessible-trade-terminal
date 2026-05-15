using System;
using System.Collections.Generic;
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
    public sealed class ReconnectingWebSocket : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Maximum bytes we will accumulate for a single WebSocket message before
        /// closing the connection. Protects against a compromised or hostile endpoint
        /// OOM-ing the app with an unbounded frame. 16 MB is well above any legitimate
        /// exchange payload (Binance depth snapshots are ~1 MB, most messages < 64 KB).
        /// </summary>
        public const int MaxMessageBytes = 16 * 1024 * 1024;

        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private readonly string _url;
        private readonly TimeSpan _heartbeatInterval;
        private readonly TimeSpan _reconnectBaseDelay;
        private readonly int _maxReconnectAttempts;
        private volatile bool _disposed;

        // Loop task handles. Captured at ConnectAsync so DisposeAsync can await
        // their completion before returning, eliminating the race where the
        // receive/heartbeat callback hits a disposed socket after Dispose has
        // already nulled the underlying ClientWebSocket.
        private Task? _receiveLoopTask;
        private Task? _heartbeatLoopTask;
        // Hard upper bound on how long sync Dispose() will block waiting for
        // the loops to drain. Async DisposeAsync waits without a ceiling.
        private static readonly TimeSpan SyncDisposeWait = TimeSpan.FromMilliseconds(500);

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

        /// <summary>
        /// Hard cap on a single ConnectAsync attempt. Without this, a hung DNS
        /// resolution or a silently-black-holed TLS handshake can wedge the
        /// whole subscription path indefinitely because most callers pass
        /// <see cref="CancellationToken.None"/>.
        /// </summary>
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        /// <summary>Connects and starts the receive loop. Idempotent if already connected.</summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            if (IsConnected) return;
            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // Local CTS bounds the handshake only; the long-lived _cts still governs the
            // receive + heartbeat loops once connected.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectCts.CancelAfter(ConnectTimeout);
            await ConnectInternalAsync(connectCts.Token).ConfigureAwait(false);
            _receiveLoopTask = ReceiveLoopAsync(_cts.Token);
            _heartbeatLoopTask = _heartbeatInterval > TimeSpan.Zero
                ? HeartbeatLoopAsync(_cts.Token)
                : null;
        }

        private async Task ConnectInternalAsync(CancellationToken ct)
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(_url), ct).ConfigureAwait(false);
            if (_onConnected != null)
                await _onConnected(this).ConfigureAwait(false);
        }

        /// <summary>Send a text message. Safe to call from any thread.</summary>
        public async Task SendAsync(string message, CancellationToken ct = default)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        /// <summary>Gracefully disconnect.</summary>
        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false); }
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
                        await Task.Delay(delay, ct).ConfigureAwait(false);

                        try
                        {
                            await ConnectInternalAsync(ct).ConfigureAwait(false);
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
                    bool oversize = false;
                    do
                    {
                        result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                        if (ms.Length + result.Count > MaxMessageBytes)
                        {
                            oversize = true;
                            break;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (oversize)
                    {
                        _onError?.Invoke($"WebSocket message exceeded {MaxMessageBytes} bytes — closing.");
                        try
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None).ConfigureAwait(false);
                        }
                        catch { /* best-effort */ }
                        _ws?.Dispose();
                        _ws = null;
                        ms.Dispose();
                        continue; // triggers reconnect
                    }

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
                catch (ObjectDisposedException) when (_disposed) { break; }
                catch (Exception ex)
                {
                    if (_disposed) break;  // suppress noise during teardown
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
                    await Task.Delay(_heartbeatInterval, ct).ConfigureAwait(false);
                    if (_ws?.State == WebSocketState.Open)
                    {
                        // Send a WebSocket ping frame with a real payload. Using count=0 here
                        // (an earlier bug) produced an empty frame that some exchanges treat
                        // as a no-op; idle sockets would then time out and force a reconnect.
                        var pingBytes = Encoding.UTF8.GetBytes("ping");
                        await _ws.SendAsync(new ArraySegment<byte>(pingBytes, 0, pingBytes.Length), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) when (_disposed) { break; }
                catch (Exception ex)
                {
                    if (_disposed) break;
                    // Heartbeat failures are non-fatal (the reconnect loop handles socket
                    // death), but swallowing silently hid the count=0 bug for months.
                    System.Diagnostics.Debug.WriteLine($"[ReconnectingWebSocket] heartbeat error: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts?.Cancel(); } catch { /* already disposed */ }
            // Best-effort drain on the synchronous path. Bounded so a wedged
            // ReceiveAsync doesn't block the calling thread (e.g. the MAUI
            // shutdown handler) for more than SyncDisposeWait. The receive
            // loop's ObjectDisposedException handler exits cleanly when
            // _disposed is set, so the wait usually completes in well under
            // the timeout. Async callers should prefer DisposeAsync().
            try
            {
                var loops = new List<Task>(2);
                if (_receiveLoopTask != null) loops.Add(_receiveLoopTask);
                if (_heartbeatLoopTask != null) loops.Add(_heartbeatLoopTask);
                if (loops.Count > 0)
                    Task.WhenAll(loops).Wait(SyncDisposeWait);
            }
            catch { /* loops always exit cleanly via the canceled token */ }
            _ws?.Dispose();
            _ws = null;
            _cts?.Dispose();
            _cts = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts?.Cancel(); } catch { /* already disposed */ }
            try
            {
                if (_receiveLoopTask != null) await _receiveLoopTask.ConfigureAwait(false);
                if (_heartbeatLoopTask != null) await _heartbeatLoopTask.ConfigureAwait(false);
            }
            catch { /* loops always exit cleanly via the canceled token */ }
            _ws?.Dispose();
            _ws = null;
            _cts?.Dispose();
            _cts = null;
        }
    }
}

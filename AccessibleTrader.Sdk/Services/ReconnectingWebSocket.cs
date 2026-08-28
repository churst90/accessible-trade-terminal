using System.Net.WebSockets;
using System.Text;

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
        /// exchange payload (Binance depth snapshots are ~1 MB, most messages &lt; 64 KB).
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

        /// <summary>
        /// Serialises every write to the socket.
        ///
        /// <para>
        /// <see cref="ClientWebSocket.SendAsync(ArraySegment{byte}, WebSocketMessageType, bool, CancellationToken)"/>
        /// does not permit overlapping calls — the second throws
        /// <c>InvalidOperationException("There is already one outstanding 'SendAsync' call")</c>.
        /// This class has two independent writers: <see cref="HeartbeatLoopAsync"/> on its own
        /// timer, and whatever caller is sending a subscribe. A symbol switch that lands on the
        /// heartbeat tick therefore lost the subscribe — and the exception surfaced on the
        /// caller's path, not the socket's, so the symptom was a chart that simply never
        /// updated. The class doc used to claim <see cref="SendAsync"/> was "safe to call from
        /// any thread", which made the bug harder to find than no comment at all.
        /// </para>
        /// </summary>
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        // Hard upper bound on how long sync Dispose() will block waiting for
        // the loops to drain. Async DisposeAsync waits without a ceiling.
        private static readonly TimeSpan SyncDisposeWait = TimeSpan.FromMilliseconds(500);

        // Callbacks
        private Func<ReconnectingWebSocket, Task>? _onConnected;
        private Action<string>? _onMessage;
        private Action<byte[]>? _onBinary;
        private Action<string>? _onError;
        private Action? _onDisconnected;
        // Heartbeat payload. Defaults to "ping"; exchanges with a specific keepalive
        // format (MEXC spot: {"method":"PING"}) override it so idle sockets aren't
        // dropped for sending an unrecognised frame.
        private string _heartbeatMessage = "ping";

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

        /// <summary>Register a callback for each received BINARY message (e.g. MEXC's
        /// Protobuf spot push frames). Text frames still route to <see cref="OnMessage"/>.</summary>
        public ReconnectingWebSocket OnBinary(Action<byte[]> handler) { _onBinary = handler; return this; }

        /// <summary>Overrides the heartbeat keepalive payload (default "ping").</summary>
        public ReconnectingWebSocket WithHeartbeatMessage(string message) { _heartbeatMessage = message; return this; }

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
            // Cancel the previous generation and dispose it ONCE ITS LOOPS HAVE DRAINED.
            // Cancelling alone leaked the source's registration list — and its timer handle
            // where one was armed — on every reconnect and every symbol switch, which a long
            // session does hundreds of times. Disposing it here instead would race the loops
            // that still hold its token, so the drain is what makes the dispose safe.
            RetirePreviousGeneration();
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

        /// <summary>
        /// Cancels the current token source and schedules its disposal for after the loops it
        /// governs have exited. The loops are the only holders of its token, so once both tasks
        /// complete nothing can register on it again.
        /// </summary>
        private void RetirePreviousGeneration()
        {
            var retired = _cts;
            if (retired == null) return;
            var loops = new List<Task>(2);
            if (_receiveLoopTask != null) loops.Add(_receiveLoopTask);
            if (_heartbeatLoopTask != null) loops.Add(_heartbeatLoopTask);
            _cts = null;
            _receiveLoopTask = null;
            _heartbeatLoopTask = null;

            try { retired.Cancel(); } catch (ObjectDisposedException) { return; }

            if (loops.Count == 0)
            {
                retired.Dispose();
                return;
            }
            _ = Task.WhenAll(loops).ContinueWith(
                _ => { try { retired.Dispose(); } catch { /* already disposed */ } },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task ConnectInternalAsync(CancellationToken ct)
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(_url), ct).ConfigureAwait(false);
            if (_onConnected != null)
                await _onConnected(this).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a text message. Safe to call from any thread — writes are serialised through
        /// <see cref="_sendLock"/>, without which an overlapping heartbeat would make this
        /// throw and the message would silently never go. See that field for the full story.
        /// </summary>
        public async Task SendAsync(string message, CancellationToken ct = default)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(message);
            await SendFrameAsync(bytes, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The single write path to the socket. Every caller — public sends and the heartbeat
        /// alike — goes through here so no two writes are ever in flight at once. The socket is
        /// re-read INSIDE the lock because a reconnect may have replaced or nulled it while
        /// this caller was queued.
        /// </summary>
        private async Task SendFrameAsync(byte[] bytes, CancellationToken ct)
        {
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var ws = _ws;
                if (ws == null || ws.State != WebSocketState.Open) return;
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
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
                            // GIVING UP IS A DISCONNECT AND MUST BE REPORTED AS ONE.
                            //
                            // This used to invoke _onError and return, terminating the loop for
                            // good. _onDisconnected was never called on that path, so
                            // ConnectionStateStream stayed at Connected over a permanently dead
                            // feed: the UI said the provider was fine and the chart simply
                            // stopped, which for a user who cannot see the last bar's timestamp
                            // is indistinguishable from a quiet market.
                            _onError?.Invoke(
                                $"Connection lost — gave up after {_maxReconnectAttempts} reconnect attempts. "
                                + "The feed is dead until you reconnect.");
                            _onDisconnected?.Invoke();
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

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        var payload = ms.ToArray();
                        ms.Dispose();
                        if (payload.Length > 0) _onBinary?.Invoke(payload);
                    }
                    else
                    {
                        var message = Encoding.UTF8.GetString(ms.ToArray());
                        ms.Dispose();
                        if (!string.IsNullOrWhiteSpace(message))
                            _onMessage?.Invoke(message);
                    }
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

        /// <summary>
        /// Consecutive heartbeat send failures tolerated before the socket is declared
        /// dead. A single failure can be a transient buffer issue; three in a row
        /// (default 90s of silence at the 30s interval) means the connection is gone
        /// even if the OS hasn't noticed — e.g. a half-open TCP connection after a
        /// NAT timeout. On user-data streams this is the difference between hearing
        /// about your fills and silently missing them.
        /// </summary>
        public const int MaxConsecutiveHeartbeatFailures = 3;

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            int consecutiveFailures = 0;
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
                        var pingBytes = Encoding.UTF8.GetBytes(_heartbeatMessage);
                        await SendFrameAsync(pingBytes, ct).ConfigureAwait(false);
                        consecutiveFailures = 0;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) when (_disposed) { break; }
                catch (Exception ex)
                {
                    if (_disposed) break;
                    consecutiveFailures++;
                    // Single failures stay quiet (the reconnect loop handles outright socket
                    // death), but repeated failures mean a half-open connection the receive
                    // loop can't see — surface it and kill the socket so reconnect kicks in.
                    System.Diagnostics.Debug.WriteLine($"[ReconnectingWebSocket] heartbeat error ({consecutiveFailures}/{MaxConsecutiveHeartbeatFailures}): {ex.Message}");
                    if (consecutiveFailures >= MaxConsecutiveHeartbeatFailures)
                    {
                        consecutiveFailures = 0;
                        _onError?.Invoke($"Heartbeat failed {MaxConsecutiveHeartbeatFailures} times in a row — connection presumed dead, reconnecting: {ex.Message}");
                        try { _ws?.Dispose(); } catch { /* best-effort */ }
                        _ws = null; // receive loop sees a dead socket and reconnects
                    }
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
            _sendLock.Dispose();
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
            _sendLock.Dispose();
        }
    }
}

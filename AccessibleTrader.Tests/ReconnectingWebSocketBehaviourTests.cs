using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <see cref="ReconnectingWebSocket"/> driven against a REAL WebSocket server on loopback.
    ///
    /// <para>
    /// Every live price feed and every user-data stream (fills, order updates) in the terminal
    /// rides on this class, and before A2p it was guarded only by source scans: text searches
    /// that pinned a spelling ("_onDisconnected?.Invoke appears in the give-up branch") rather
    /// than the behaviour. The A2p mutation campaign found four single-line bugs here — a
    /// reconnect budget that never refills, a heartbeat that ignores its configured payload,
    /// an oversize guard that measures a frame instead of a message, a reconnect that leaves
    /// the previous generation's receive loop running — that no test noticed, and a fifth
    /// (the give-up path no longer reporting a disconnect) that only a source scan noticed.
    /// These tests state each behaviour as the user meets it.
    /// </para>
    ///
    /// <para>
    /// There is deliberately no test here for overlapping sends. One was written (eight 2 MB
    /// sends in flight against a server that holds off reading) and it stayed GREEN with the
    /// public <c>SendAsync</c> writing to the socket directly, bypassing <c>_sendLock</c>: on
    /// .NET 10 (Linux, where it was run) the managed WebSocket serialises concurrent sends itself
    /// rather than throwing. A guard never seen red is not a guard, so it was removed; the
    /// source scan in <c>ReconnectingWebSocketContractTests</c> still pins the single write path.
    /// </para>
    ///
    /// <para>
    /// The server is <see cref="HttpListener"/> on 127.0.0.1 with a per-connection script, so a
    /// test can refuse a handshake (503), abort a connection mid-session, read what the client
    /// sent, or send a message of any size. Every wait is on a positive signal with a generous
    /// ceiling — none of these asserts that something did NOT happen within a short window.
    /// </para>
    /// </summary>
    public class ReconnectingWebSocketBehaviourTests
    {
        private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(20);

        // ── Giving up ────────────────────────────────────────────────────────

        [Fact]
        public async Task A_feed_that_gives_up_reconnecting_reports_itself_disconnected()
        {
            // Connection 1 is accepted and then dropped abruptly (no close frame, so the
            // receive-error path runs, not the close-frame path); every reconnect is refused.
            // After the budget is spent the socket stops for good — and a dead feed that still
            // reads "Connected" is indistinguishable from a quiet market to someone who cannot
            // see the last bar's time.
            await using var server = new LoopbackWsServer(async (n, ctx, stop) =>
            {
                if (n > 1) { Refuse(ctx); return; }
                var ws = await Accept(ctx);
                await Task.Delay(100);
                Drop(ws, ctx);
            });

            var gaveUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var disconnectedAfterGivingUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var client = new ReconnectingWebSocket(server.Url,
                heartbeatInterval: TimeSpan.FromHours(1),
                reconnectBaseDelay: TimeSpan.FromMilliseconds(10),
                maxReconnectAttempts: 2);
            client.OnError(e => { if (e.Contains("gave up", StringComparison.Ordinal)) gaveUp.TrySetResult(); });
            client.OnDisconnected(() => { if (gaveUp.Task.IsCompleted) disconnectedAfterGivingUp.TrySetResult(); });

            await client.ConnectAsync();
            await gaveUp.Task.WaitAsync(Ceiling);

            var reported = await Task.WhenAny(disconnectedAfterGivingUp.Task, Task.Delay(Ceiling));
            Assert.True(reported == disconnectedAfterGivingUp.Task,
                "The socket gave up reconnecting but never reported a disconnect: the UI keeps saying Connected over a dead feed.");
        }

        [Fact]
        public async Task A_reconnect_that_succeeds_refills_the_reconnect_budget()
        {
            // Budget of two consecutive failures. The session drops twice, and each time ONE
            // reconnect attempt fails before the next succeeds — never two in a row. A budget
            // that counted failures across the whole session instead of consecutively would be
            // spent by the second drop and the feed would die, even though the network was only
            // ever briefly unavailable.
            await using var server = new LoopbackWsServer(async (n, ctx, stop) =>
            {
                switch (n)
                {
                    case 1:
                    case 3:
                        var drop = await Accept(ctx);
                        await Task.Delay(100);
                        Drop(drop, ctx);
                        break;
                    case 2:
                    case 4:
                        Refuse(ctx);
                        break;
                    default:
                        var ws = await Accept(ctx);
                        await ws.SendAsync(Encoding.UTF8.GetBytes($"session {n}"), WebSocketMessageType.Text, true, CancellationToken.None);
                        await Task.Delay(Timeout.Infinite, stop);
                        break;
                }
            });

            var outcome = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var client = new ReconnectingWebSocket(server.Url,
                heartbeatInterval: TimeSpan.FromHours(1),
                reconnectBaseDelay: TimeSpan.FromMilliseconds(10),
                maxReconnectAttempts: 2);
            client.OnMessage(m => outcome.TrySetResult(m));
            client.OnError(e => { if (e.Contains("gave up", StringComparison.Ordinal)) outcome.TrySetResult("GAVE UP: " + e); });

            await client.ConnectAsync();
            string result = await outcome.Task.WaitAsync(Ceiling);

            Assert.Equal("session 5", result);
        }

        // ── Heartbeat ────────────────────────────────────────────────────────

        [Fact]
        public async Task The_heartbeat_sends_the_configured_keepalive_payload()
        {
            // MEXC spot drops an idle socket that does not send {"method":"PING"}; a plain
            // "ping" is an unrecognised frame to it. The override exists for exactly that.
            const string keepalive = "{\"method\":\"PING\"}";
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var server = new LoopbackWsServer(async (n, ctx, stop) =>
            {
                var ws = await Accept(ctx);
                received.TrySetResult(await ReceiveText(ws, stop));
                await Task.Delay(Timeout.Infinite, stop);
            });

            await using var client = new ReconnectingWebSocket(server.Url,
                heartbeatInterval: TimeSpan.FromMilliseconds(50));
            client.WithHeartbeatMessage(keepalive);
            await client.ConnectAsync();

            Assert.Equal(keepalive, await received.Task.WaitAsync(Ceiling));
        }

        // ── Reads ────────────────────────────────────────────────────────────

        [Fact]
        public async Task A_message_larger_than_the_cap_is_refused_even_when_it_arrives_in_pieces()
        {
            // The cap protects against an endpoint streaming one unbounded message: every
            // receive delivers at most one 64 KB buffer, so the check has to be on the MESSAGE
            // accumulated so far, not on the piece just read — per piece, nothing ever trips.
            int oversize = ReconnectingWebSocket.MaxMessageBytes + 1;
            await using var server = new LoopbackWsServer(async (n, ctx, stop) =>
            {
                var ws = await Accept(ctx);
                if (n == 1)
                {
                    var payload = new byte[oversize];
                    Array.Fill(payload, (byte)'x');
                    try
                    {
                        await ws.SendAsync(payload, WebSocketMessageType.Text, true, stop);
                        // Answer the client's MessageTooBig close so its CloseAsync can finish.
                        var buf = new byte[1024];
                        var r = await ws.ReceiveAsync(buf, stop);
                        if (r.MessageType == WebSocketMessageType.Close)
                            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", stop);
                    }
                    catch (Exception) { /* the client closes on us — that is the point */ }
                }
                await Task.Delay(Timeout.Infinite, stop);
            });

            var outcome = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var client = new ReconnectingWebSocket(server.Url,
                heartbeatInterval: TimeSpan.FromHours(1),
                reconnectBaseDelay: TimeSpan.FromMilliseconds(10));
            client.OnMessage(m => outcome.TrySetResult($"DELIVERED {m.Length} characters"));
            client.OnError(e => { if (e.Contains("exceeded", StringComparison.Ordinal)) outcome.TrySetResult("refused"); });

            await client.ConnectAsync();

            Assert.Equal("refused", await outcome.Task.WaitAsync(Ceiling));
        }

        // ── Reconnect generations ────────────────────────────────────────────

        [Fact]
        public async Task Connecting_again_while_the_old_loop_is_backing_off_leaves_one_loop_on_one_socket()
        {
            // The socket drops and the receive loop starts its back-off. Before it wakes, the
            // caller reconnects (a symbol switch calls ConnectAsync). The old generation must be
            // cancelled: if its loop survives it wakes up, finds the NEW socket open, and reads
            // from it alongside the new loop — two concurrent receives, which ClientWebSocket
            // refuses — or opens a connection of its own over the top of the caller's.
            // Either way the server sees a third connection, which is what this counts.
            var backoff = TimeSpan.FromMilliseconds(400);
            var connections = 0;
            var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var server = new LoopbackWsServer(async (n, ctx, stop) =>
            {
                Interlocked.Increment(ref connections);
                var ws = await Accept(ctx);
                if (n == 1)
                {
                    await Task.Delay(100);
                    Drop(ws, ctx);
                    return;
                }
                if (n == 2) second.TrySetResult();
                // Talk steadily so any second reader on this socket collides with the first.
                for (int i = 0; !stop.IsCancellationRequested; i++)
                {
                    try { await ws.SendAsync(Encoding.UTF8.GetBytes($"tick {i}"), WebSocketMessageType.Text, true, stop); }
                    catch (Exception) { return; }
                    await Task.Delay(20);
                }
            });

            var errors = new ConcurrentQueue<string>();
            var dropped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int ticksAfterWake = 0;
            var ticksSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var wakeAt = DateTime.MaxValue;
            await using var client = new ReconnectingWebSocket(server.Url,
                heartbeatInterval: TimeSpan.FromHours(1),
                reconnectBaseDelay: backoff);
            client.OnError(e => { errors.Enqueue(e); dropped.TrySetResult(); });
            client.OnMessage(_ =>
            {
                if (DateTime.UtcNow > wakeAt && Interlocked.Increment(ref ticksAfterWake) >= 20)
                    ticksSeen.TrySetResult();
            });

            await client.ConnectAsync();
            await dropped.Task.WaitAsync(Ceiling);       // old loop is now in its back-off
            while (!errors.IsEmpty) errors.TryDequeue(out _);

            await client.ConnectAsync();                 // the caller reconnects first
            await second.Task.WaitAsync(Ceiling);

            // Wait until well past the moment the old loop's back-off would have ended, then
            // for a run of messages on the live socket.
            wakeAt = DateTime.UtcNow + backoff + backoff;
            await ticksSeen.Task.WaitAsync(Ceiling);

            Assert.Equal(2, Volatile.Read(ref connections));
            Assert.True(errors.IsEmpty, "Errors after reconnecting: " + string.Join(" | ", errors));
        }

        // ── Loopback server ──────────────────────────────────────────────────

        private static void Refuse(HttpListenerContext ctx)
        {
            ctx.Response.StatusCode = 503;
            ctx.Response.Close();
        }

        /// <summary>Drops the connection abruptly — no close frame — by aborting the socket AND
        /// the underlying HTTP connection (aborting the WebSocket alone leaves the TCP stream open
        /// under HttpListener, and the client never notices).</summary>
        private static void Drop(WebSocket ws, HttpListenerContext ctx)
        {
            ws.Abort();
            ctx.Response.Abort();
        }

        private static async Task<WebSocket> Accept(HttpListenerContext ctx) =>
            (await ctx.AcceptWebSocketAsync(subProtocol: null)).WebSocket;

        private static async Task<string> ReceiveText(WebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buffer, ct);
                ms.Write(buffer, 0, r.Count);
            } while (!r.EndOfMessage);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        /// <summary>
        /// A WebSocket server on 127.0.0.1 that hands each incoming connection, numbered from 1,
        /// to a script. Disposal cancels <see cref="Stopping"/> and stops the listener.
        /// </summary>
        private sealed class LoopbackWsServer : IAsyncDisposable
        {
            private readonly HttpListener _listener = new();
            private readonly CancellationTokenSource _stop = new();
            private readonly Task _acceptLoop;
            private int _count;

            public string Url { get; }
            public CancellationToken Stopping => _stop.Token;

            public LoopbackWsServer(Func<int, HttpListenerContext, CancellationToken, Task> script)
            {
                int port = FreePort();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                Url = $"ws://127.0.0.1:{port}/";
                _acceptLoop = Task.Run(async () =>
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        HttpListenerContext ctx;
                        try { ctx = await _listener.GetContextAsync(); }
                        catch (Exception) { return; }
                        int n = Interlocked.Increment(ref _count);
                        _ = Task.Run(async () =>
                        {
                            try { await script(n, ctx, _stop.Token); }
                            catch (Exception) { /* a script ends when the test tears down */ }
                        });
                    }
                });
            }

            private static int FreePort()
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                int port = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                return port;
            }

            public async ValueTask DisposeAsync()
            {
                _stop.Cancel();
                try { _listener.Stop(); _listener.Close(); } catch (Exception) { }
                try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
                _stop.Dispose();
            }
        }
    }
}

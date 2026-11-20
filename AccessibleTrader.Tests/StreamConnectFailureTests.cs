using System.Net;
using System.Net.Sockets;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// A stream whose first handshake fails must say so. <see cref="ReconnectingWebSocket.ConnectAsync"/>
/// throws once and does not retry, and three providers started streams with a discarded task,
/// so the throw went nowhere: Kraken's and MEXC's order books and Tradier's account stream
/// could die without a word (code review, 2026-11-20). The address used here is a loopback
/// port that was just released, so the connect is refused at once and no network is touched.
/// </summary>
public class StreamConnectFailureTests
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(20);

    private static string RefusingUrl()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return $"ws://127.0.0.1:{port}/";
    }

    [Fact]
    public async Task A_bare_connect_to_a_refusing_port_throws()
    {
        // Vacuity control: without this the tests below could pass against a socket that
        // never fails at all.
        using var ws = new ReconnectingWebSocket(RefusingUrl());
        await Assert.ThrowsAnyAsync<Exception>(() => ws.ConnectAsync());
    }

    [Fact]
    public async Task ConnectOrReport_hands_the_failure_over_and_returns_false()
    {
        using var ws = new ReconnectingWebSocket(RefusingUrl());
        Exception? reported = null;

        bool ok = await ws.ConnectOrReportAsync(ex => reported = ex);

        Assert.False(ok);
        Assert.NotNull(reported);
    }

    [Fact]
    public async Task Kraken_order_book_that_cannot_connect_says_so_and_tries_again_on_the_next_open()
    {
        var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider { WsUrl = RefusingUrl() };
        var errors = new List<string>();
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        using var sub = p.ErrorStream.Subscribe(e =>
        {
            lock (errors)
            {
                if (!e.Contains("order book", StringComparison.OrdinalIgnoreCase)) return;
                errors.Add(e);
                if (errors.Count == 1) first.TrySetResult();
                if (errors.Count == 2) second.TrySetResult();
            }
        });

        p.SubscribeOrderBook("BTC/USD");
        await first.Task.WaitAsync(Ceiling);
        // The failed symbol is forgotten, so opening the book again makes a fresh attempt
        // rather than handing back the dead subscription.
        p.SubscribeOrderBook("BTC/USD");
        await second.Task.WaitAsync(Ceiling);

        Assert.All(errors, e => Assert.Contains("could not subscribe", e));
    }

}

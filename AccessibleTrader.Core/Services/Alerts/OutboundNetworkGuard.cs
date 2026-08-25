using System.Net;
using System.Net.Sockets;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// Decides whether an outbound connection target belongs to the public internet.
    ///
    /// <para>
    /// The alert channels connect to wherever the USER typed — a webhook URL, an SMTP
    /// host and port. On the hosted server that is an SSRF primitive: any registered
    /// user can point delivery at the cloud metadata endpoint, loopback services or
    /// the private network the server sits on, and the spoken delivery result is a
    /// boolean oracle. This guard is the deny-list of everything that is not the
    /// public internet; the provider allow-list in <c>PluginHostServices</c> cannot
    /// help here because the whole point of these channels is a user-chosen host.
    /// </para>
    /// </summary>
    public static class OutboundNetworkGuard
    {
        /// <summary>
        /// True only for globally routable unicast addresses. Loopback, RFC1918,
        /// link-local (incl. 169.254.169.254, every cloud's metadata service),
        /// CGNAT, unique-local, multicast, unspecified, benchmarking and
        /// documentation ranges are all rejected. IPv4-mapped IPv6 is unmapped
        /// first so <c>::ffff:10.0.0.1</c> cannot smuggle a private IPv4 through.
        /// </summary>
        public static bool IsPublic(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

            if (IPAddress.IsLoopback(ip)) return false;
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                return !(b[0] == 0                                  // 0.0.0.0/8 "this network"
                    || b[0] == 10                                   // RFC1918
                    || b[0] == 127                                  // loopback
                    || (b[0] == 100 && (b[1] & 0xC0) == 64)         // 100.64/10 CGNAT
                    || (b[0] == 169 && b[1] == 254)                 // link-local + cloud metadata
                    || (b[0] == 172 && (b[1] & 0xF0) == 16)         // RFC1918
                    || (b[0] == 192 && b[1] == 168)                 // RFC1918
                    || (b[0] == 192 && b[1] == 0 && b[2] == 0)      // 192.0.0/24 protocol assignments
                    || (b[0] == 192 && b[1] == 0 && b[2] == 2)      // TEST-NET-1
                    || (b[0] == 198 && (b[1] & 0xFE) == 18)         // 198.18/15 benchmarking
                    || (b[0] == 198 && b[1] == 51 && b[2] == 100)   // TEST-NET-2
                    || (b[0] == 203 && b[1] == 0 && b[2] == 113)    // TEST-NET-3
                    || b[0] >= 224);                                // multicast + 240/4 reserved + broadcast
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var b = ip.GetAddressBytes();
                return !(ip.IsIPv6LinkLocal
                    || ip.IsIPv6Multicast
                    || ip.IsIPv6SiteLocal
                    || (b[0] & 0xFE) == 0xFC);                      // fc00::/7 unique-local
            }

            return false; // unknown family — fail closed
        }

        /// <summary>
        /// Resolves <paramref name="host"/> and throws unless EVERY resulting address
        /// is public — the attacker controls the DNS record, so one private A record
        /// among public ones is still a probe. IP literals skip DNS. Returns the
        /// public addresses so a caller that connects itself can pin them (no
        /// resolve-then-reconnect TOCTOU).
        /// </summary>
        public static async Task<IReadOnlyList<IPAddress>> ResolvePublicOrThrowAsync(
            string host, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new HttpRequestException("Delivery target has no host.");

            if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
            {
                if (!IsPublic(literal)) throw NotPublic(host);
                return new[] { literal };
            }

            IPAddress[] resolved;
            try
            {
                resolved = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                throw new HttpRequestException($"Delivery target '{host}' did not resolve.", ex);
            }

            if (resolved.Length == 0 || resolved.Any(a => !IsPublic(a)))
                throw NotPublic(host);
            return resolved;
        }

        private static HttpRequestException NotPublic(string host) => new(
            $"Delivery target '{host}' is not on the public internet. On this host, alert " +
            "channels may only reach public addresses — loopback, private and link-local " +
            "targets are refused (see DemoPolicy.BlockPrivateNetworkTargets).");
    }

    /// <summary>
    /// The one place alert-channel <see cref="HttpClient"/>s come from — previously two
    /// byte-identical private <c>BuildAlertChannelHttpClient</c> copies in the heads,
    /// which is exactly how the channels came to bypass every outbound restriction the
    /// providers are held to.
    ///
    /// <para>
    /// Redirects are never followed: the target is user-supplied, a webhook is a POST
    /// to a fixed endpoint, and an open redirect on an approved host must not be able
    /// to re-aim the request (the allow-list handler chain has the same blind spot —
    /// a redirect is followed by the inner handler without re-entering the check).
    /// A 30x therefore surfaces as a delivery failure the user can hear.
    /// </para>
    ///
    /// <para>
    /// With <paramref name="blockPrivateNetworks"/> the guard runs inside
    /// <see cref="SocketsHttpHandler.ConnectCallback"/>: the socket connects to an
    /// address this code resolved and validated itself, so a DNS record that flips
    /// between validation and connect (rebinding) has nothing to rebind.
    /// </para>
    /// </summary>
    public static class AlertChannelHttpClient
    {
        public static HttpClient Create(bool blockPrivateNetworks)
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            };

            if (blockPrivateNetworks)
            {
                handler.ConnectCallback = async (ctx, ct) =>
                {
                    var addresses = await OutboundNetworkGuard
                        .ResolvePublicOrThrowAsync(ctx.DnsEndPoint.Host, ct).ConfigureAwait(false);
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(addresses.ToArray(), ctx.DnsEndPoint.Port, ct)
                            .ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                };
            }

            // Same envelope the old per-head copies used: alert payloads and
            // channel responses are small JSON; a hung endpoint must not pin
            // the delivery thread.
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
                MaxResponseContentBufferSize = 1 * 1024 * 1024,
            };
        }
    }
}

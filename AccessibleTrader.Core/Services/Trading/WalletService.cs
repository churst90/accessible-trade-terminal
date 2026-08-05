using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>An address that has been fetched and checked, ready to put in front of a user.</summary>
    public sealed record CheckedDepositAddress(DepositAddress Address, AddressValidation Validation);

    /// <summary>
    /// The wallet path: which venues have one, what networks an asset takes, and an
    /// address that has been validated before anyone sees it.
    ///
    /// <para>
    /// Everything here goes through <see cref="ProviderResult{T}"/>, because the
    /// ways this can come back empty are all different and all actionable: the venue
    /// has no wallet API, the asset cannot be deposited, the key lacks the scope,
    /// verification is incomplete, or the call failed. "No address" would collapse
    /// five answers into one, on the screen where being wrong costs money.
    /// </para>
    /// </summary>
    public sealed class WalletService
    {
        private readonly IDataService _data;
        private readonly ILogger<WalletService> _logger;

        public WalletService(IDataService data, ILogger<WalletService> logger)
        {
            _data = data;
            _logger = logger;
        }

        /// <summary>
        /// Whether this provider has a wallet at all. A compiler-enforced fact, not
        /// a declared flag — which is why the Wallet button cannot appear over
        /// nothing.
        /// </summary>
        public async Task<bool> HasWalletAsync(string provider)
        {
            var p = await _data.GetProviderAsync(provider).ConfigureAwait(false);
            return p is IWalletProvider || p?.GetCapability<IWalletProvider>() is not null;
        }

        private async Task<IWalletProvider?> WalletAsync(string provider)
        {
            var p = await _data.GetProviderAsync(provider).ConfigureAwait(false);
            return p as IWalletProvider ?? p?.GetCapability<IWalletProvider>();
        }

        public async Task<ProviderResult<IReadOnlyList<string>>> GetNetworksAsync(
            string provider, string asset, CancellationToken ct = default)
        {
            var w = await WalletAsync(provider).ConfigureAwait(false);
            if (w is null)
                return ProviderResult<IReadOnlyList<string>>.NotSupported(
                    $"{provider} does not expose deposit addresses");

            try
            {
                var nets = await w.GetDepositNetworksAsync(asset, ct).ConfigureAwait(false);

                // An empty list is a real answer here — the venue lists the asset
                // but will not take deposits of it — and must not read as a failure.
                return nets.Count == 0
                    ? ProviderResult<IReadOnlyList<string>>.NotSupported(
                        $"{provider} does not accept {asset} deposits")
                    : ProviderResult<IReadOnlyList<string>>.Ok(nets);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deposit networks failed for {Asset} on {Provider}", asset, provider);
                return ProviderResult.FromException<IReadOnlyList<string>>(ex, $"Reading {asset} deposit networks");
            }
        }

        /// <summary>
        /// Fetch an address and check it before returning it.
        ///
        /// <para>
        /// A **malformed** address is refused outright rather than shown with a
        /// warning. There is no legitimate reason for a venue's own API to return
        /// one, and the whole point of a warning is that it can be ignored — which
        /// on this screen means funds gone. Anything merely unverifiable is returned
        /// WITH what could not be checked, so the caller can say so.
        /// </para>
        /// </summary>
        public async Task<ProviderResult<CheckedDepositAddress>> GetAddressAsync(
            string provider, string asset, string network, CancellationToken ct = default)
        {
            var w = await WalletAsync(provider).ConfigureAwait(false);
            if (w is null)
                return ProviderResult<CheckedDepositAddress>.NotSupported(
                    $"{provider} does not expose deposit addresses");

            try
            {
                var addr = await w.GetDepositAddressAsync(asset, network, ct).ConfigureAwait(false);
                var check = CryptoAddressValidator.Validate(addr.Address, addr.Network);

                if (!check.IsDisplayable)
                {
                    // The venue returning a corrupt address is the alarm this check
                    // exists to raise. Log loudly; show nothing.
                    _logger.LogError("Refusing to display a malformed {Asset} address from {Provider} on {Network}: {Detail}",
                        asset, provider, network, check.Detail);
                    return ProviderResult<CheckedDepositAddress>.Failed(
                        $"{provider} returned an address that failed its own format check ({check.Detail}). "
                      + "It has not been shown. Do not send funds — check the venue directly.");
                }

                return ProviderResult<CheckedDepositAddress>.Ok(new CheckedDepositAddress(addr, check));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deposit address failed for {Asset}/{Network} on {Provider}",
                    asset, network, provider);
                return ProviderResult.FromException<CheckedDepositAddress>(ex, $"Reading the {asset} deposit address");
            }
        }

        public async Task<ProviderResult<IReadOnlyList<DepositRecord>>> GetDepositsAsync(
            string provider, string? asset = null, int limit = 50, CancellationToken ct = default)
        {
            var w = await WalletAsync(provider).ConfigureAwait(false);
            if (w is null)
                return ProviderResult<IReadOnlyList<DepositRecord>>.NotSupported(
                    $"{provider} does not expose deposit history");

            try
            {
                return ProviderResult<IReadOnlyList<DepositRecord>>.Ok(
                    await w.GetDepositsAsync(asset, limit, ct).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deposit history failed on {Provider}", provider);
                return ProviderResult.FromException<IReadOnlyList<DepositRecord>>(ex, "Reading deposit history");
            }
        }

        // ── Speech ───────────────────────────────────────────────────────────

        /// <summary>
        /// What is said when an address is opened. Network first, memo second,
        /// address last — in that order because that is the order in which getting
        /// it wrong costs you everything.
        ///
        /// <para>
        /// The address itself is NOT spelled out here. Chunked speech was considered
        /// and rejected: addresses get written down and compared by hand, so the
        /// useful thing is a stable string the review cursor and braille display can
        /// walk, not a recital nobody can follow.
        /// </para>
        /// </summary>
        public static string Announce(CheckedDepositAddress a)
        {
            var parts = new List<string>
            {
                $"{a.Address.Asset} deposit address on the {a.Address.Network} network.",
            };

            if (!string.IsNullOrWhiteSpace(a.Address.Memo))
                parts.Add($"This deposit REQUIRES a {a.Address.MemoLabel ?? "memo"} — "
                        + "without it the funds are lost.");

            if (a.Address.MinimumDeposit is > 0)
                parts.Add($"Minimum {a.Address.MinimumDeposit.Value.ToString("0.########",
                    System.Globalization.CultureInfo.InvariantCulture)} {a.Address.Asset}.");

            if (a.Address.Confirmations is > 0)
                parts.Add($"Credited after {a.Address.Confirmations} confirmations.");

            parts.Add(a.Validation.Result switch
            {
                AddressCheck.Verified      => "The address checksum was verified.",
                AddressCheck.StructureOnly => "Format checked, but this address type carries no checksum — "
                                            + "compare it character by character, including capitals.",
                _                          => "This address format could not be checked here — "
                                            + "verify it against the venue.",
            });

            return string.Join(" ", parts);
        }
    }
}

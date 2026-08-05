using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Sdk.Plugins
{
    /// <summary>
    /// A deposit address, with everything needed to use it without losing the funds.
    /// </summary>
    /// <param name="Network">
    /// The chain the address is on — "Bitcoin", "ERC20", "Solana". **The single
    /// biggest way to lose a deposit**: sending USDC to an Ethereum address over
    /// Solana destroys it, and that is far more common than any attack. The UI
    /// chooses this before it shows an address, and speaks it first.
    /// </param>
    /// <param name="Memo">
    /// Destination tag / memo, where the chain needs one — XRP, XLM, ATOM, HBAR and
    /// others. Omitting it loses the funds just as completely as a wrong address, so
    /// the UI gives it equal prominence rather than a footnote.
    /// </param>
    /// <param name="MemoLabel">
    /// What the venue calls it — "memo", "destination tag", "note". Use the venue's
    /// own word so the user can match it against the sending side.
    /// </param>
    /// <param name="MinimumDeposit">
    /// Below this the venue may not credit the deposit at all. Shown up front.
    /// </param>
    public sealed record DepositAddress(
        string  Asset,
        string  Network,
        string  Address,
        string? Memo = null,
        string? MemoLabel = null,
        double? MinimumDeposit = null,
        int?    Confirmations = null);

    /// <summary>A deposit that has been seen on the account — the "did my money arrive?" loop.</summary>
    public sealed record DepositRecord(
        string   Asset,
        string   Network,
        double   Amount,
        string   Status,
        DateTime SeenAtUtc,
        string?  TxId = null,
        int?     Confirmations = null);

    /// <summary>
    /// Reading deposit addresses and deposit history from a custodial venue.
    ///
    /// <para>
    /// **Deliberately an interface rather than a <c>ProviderCapabilities</c> flag.**
    /// Capability flags in this codebase have been wrong in both directions inside
    /// one week — declared and unimplemented, and implemented but undeclared, the
    /// latter hiding working UI. A flag is a claim someone writes by hand;
    /// <c>provider is IWalletProvider</c> is a fact the compiler enforces. For a
    /// feature whose failure mode is showing someone the wrong address to send money
    /// to, use the one that cannot be mistyped.
    /// </para>
    ///
    /// <para>
    /// **Implementations must not cache or persist an address.** Every call goes to
    /// the authenticated API. Address substitution needs a stored copy to
    /// substitute, and this way there is not one. Several venues also hand out a
    /// fresh address per request, so a cached one is wrong as well as unsafe.
    /// </para>
    ///
    /// <para>
    /// Withdrawals are deliberately NOT on this interface. They need a separate,
    /// withdrawal-enabled credential so that the terminal's ordinary trading keys
    /// can never move funds — see <c>docs/WALLET_AND_PORTFOLIO_DESIGN.md</c> §3.
    /// </para>
    /// </summary>
    public interface IWalletProvider : IProviderPlugin
    {
        /// <summary>
        /// Chains this asset can be deposited over, as the venue names them. An
        /// empty list means the venue does not accept deposits of this asset —
        /// which is a real answer and must not be shown as "no address found".
        /// </summary>
        Task<IReadOnlyList<string>> GetDepositNetworksAsync(string asset, CancellationToken ct = default);

        /// <summary>
        /// A deposit address for this asset on this network, read fresh from the
        /// authenticated API. Never cached, never written to disk.
        /// </summary>
        Task<DepositAddress> GetDepositAddressAsync(string asset, string network, CancellationToken ct = default);

        /// <summary>
        /// Recent deposits, newest first. Optional: default returns empty so a venue
        /// with addresses but no history endpoint need not pretend to have one.
        /// </summary>
        Task<IReadOnlyList<DepositRecord>> GetDepositsAsync(
            string? asset = null, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DepositRecord>>(Array.Empty<DepositRecord>());
    }
}

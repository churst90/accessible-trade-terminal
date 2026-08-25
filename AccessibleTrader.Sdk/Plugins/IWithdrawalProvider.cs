using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Sdk.Plugins
{
    /// <summary>
    /// A withdrawal destination the user has ALREADY approved at the venue.
    /// </summary>
    /// <param name="Key">
    /// The venue's own identifier for this saved destination — Kraken calls it the
    /// withdrawal key, other venues call it a label or an address id. This is what
    /// gets sent when withdrawing, and it is the whole point: see
    /// <see cref="IWithdrawalProvider"/>.
    /// </param>
    /// <param name="Address">
    /// Shown so the user can confirm which destination they mean, and never sent
    /// anywhere. Some venues return it truncated; that is fine, because nothing
    /// here depends on it being complete.
    /// </param>
    public sealed record WithdrawalDestination(
        string  Key,
        string  Asset,
        string? Address = null,
        string? Network = null,
        bool    Verified = true);

    /// <summary>What a withdrawal would actually cost and deliver.</summary>
    /// <param name="NetAmount">
    /// What ARRIVES. Quoted separately from the amount sent because the fee is
    /// deducted from it on most venues, and a user who reads only the amount will
    /// expect the wrong number at the other end.
    /// </param>
    public sealed record WithdrawalQuote(
        string  Asset,
        double  Amount,
        double  Fee,
        double  NetAmount,
        double? MinimumAmount = null,
        double? RemainingDailyLimit = null);

    public sealed record WithdrawalResult(string ReferenceId, string Status);

    /// <summary>
    /// Moving funds OFF a venue.
    ///
    /// <para>
    /// **Separate from <see cref="IWalletProvider"/> on purpose.** Reading a
    /// deposit address and moving money out are different powers, and a provider
    /// that can do the first must not thereby be assumed able to do the second.
    /// The capability audit caught exactly that assumption the day Kraken's
    /// deposit support landed.
    /// </para>
    ///
    /// <para>
    /// **No method here accepts a raw address, and that is the design.**
    /// Withdrawals go to destinations the user has already whitelisted at the
    /// venue, identified by the venue's own key. The consequence is worth stating
    /// plainly: even if this terminal were completely compromised, it could not
    /// send funds anywhere the user had not personally pre-approved on the venue's
    /// own site, behind the venue's own 2FA. Accepting an address and passing it
    /// through would throw that away, and it is the single most valuable property
    /// this interface has.
    /// </para>
    ///
    /// <para>
    /// **It also requires a separate credential.** Withdrawal permission is the
    /// most dangerous scope an API key can carry, and a trading key that can also
    /// move funds means one compromise empties the account. The host will only
    /// call this with a key explicitly marked as withdrawal-enabled, which the
    /// ordinary trading profile is not.
    /// </para>
    /// </summary>
    public interface IWithdrawalProvider : IProviderPlugin
    {
        /// <summary>
        /// Destinations already whitelisted at the venue for this asset. An empty
        /// list means the user has not set any up — which is a real answer, and the
        /// fix is on the venue's site, not here.
        /// </summary>
        Task<IReadOnlyList<WithdrawalDestination>> GetWithdrawalDestinationsAsync(
            string asset, CancellationToken ct = default);

        /// <summary>
        /// Fee, net amount and limits, WITHOUT committing to anything. Always
        /// obtained and read back before a withdrawal is offered for confirmation:
        /// the fee is the number people are surprised by afterwards.
        /// </summary>
        Task<WithdrawalQuote> GetWithdrawalQuoteAsync(
            string asset, string destinationKey, double amount, CancellationToken ct = default);

        /// <summary>
        /// Send funds to an already-whitelisted destination. The only method in the
        /// SDK that moves money out, and the only one that should ever be reached
        /// through a typed confirmation rather than a keypress.
        /// </summary>
        Task<WithdrawalResult> WithdrawAsync(
            string asset, string destinationKey, double amount, CancellationToken ct = default);
    }
}

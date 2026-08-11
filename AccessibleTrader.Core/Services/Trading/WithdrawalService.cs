using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// Moving funds off a venue — the only path in the terminal that does, and the
    /// one place where being wrong loses money directly rather than through a trade.
    ///
    /// <para>
    /// Three things hold it together, and none of them is a warning message:
    /// </para>
    ///
    /// <list type="number">
    /// <item>**A separate credential.** The withdrawal key is fetched only from a
    /// profile explicitly marked withdrawal-enabled, and there is no fallback to
    /// the trading key. No withdrawal profile means no withdrawal.</item>
    /// <item>**No raw addresses, ever.** Destinations come from the venue's own
    /// whitelist and are named by the venue's key. A compromised terminal cannot
    /// invent a destination.</item>
    /// <item>**A quote before a commitment.** The fee and the NET amount are read
    /// back before anything is confirmed, because the fee is what surprises
    /// people afterwards.</item>
    /// </list>
    /// </summary>
    public sealed class WithdrawalService
    {
        /// <summary>
        /// Whether the withdrawal path is offered to users at all. **False for
        /// 2.3.0, deliberately.**
        ///
        /// <para>
        /// Everything below this line is built, tested and believed correct — but
        /// no human has ever driven it against a live venue with a real
        /// withdrawal-enabled key. Every other unverified thing in this release
        /// costs a user a wasted click. This one moves money off an exchange, and
        /// a first run that discovers something is the wrong way to learn it.
        /// </para>
        ///
        /// <para>
        /// So the code ships dark rather than being reverted: reverting would
        /// throw away work that is probably right and would have to be rebuilt
        /// from a diff, while a flag keeps the tests running against real markup
        /// every build. Flip this to <c>true</c> for 2.3.1 once Cody has run one
        /// real withdrawal end to end — that is the ONLY thing standing between
        /// here and released.
        /// </para>
        ///
        /// <para>
        /// Not a <c>const</c> on purpose: a const would fold at compile time and
        /// make the gated branches unreachable code, which reads as dead rather
        /// than as a decision, and would silence the compiler on the day it is
        /// flipped back.
        /// </para>
        /// </summary>
        public static readonly bool Released = false;

        private readonly IDataService _data;
        private readonly IApiKeyService _keys;
        private readonly ILogger<WithdrawalService> _logger;

        private readonly bool _released;

        public WithdrawalService(IDataService data, IApiKeyService keys, ILogger<WithdrawalService> logger)
            : this(data, keys, logger, Released) { }

        /// <summary>
        /// Takes the release gate explicitly so the tests can exercise the built
        /// behaviour — the code 2.3.1 will turn on — while the DI container keeps
        /// getting the dark default. An <c>internal</c> constructor rather than a
        /// mutable static: xUnit runs test classes in parallel, and a global flag
        /// one class flipped would make another class's "it stays dark" assertion
        /// fail at random.
        ///
        /// <para>
        /// Deliberately NOT an optional parameter on the public constructor:
        /// Microsoft's DI container does not honour default parameter values, so
        /// that would resolve to a runtime failure at registration instead.
        /// </para>
        /// </summary>
        internal WithdrawalService(IDataService data, IApiKeyService keys,
                                   ILogger<WithdrawalService> logger, bool released)
        {
            _data = data;
            _keys = keys;
            _logger = logger;
            _released = released;
        }

        /// <summary>
        /// Whether withdrawals are possible at all right now: the provider must
        /// implement the interface AND a withdrawal-enabled credential must exist.
        /// Both are required, and the UI shows nothing unless both hold.
        /// </summary>
        public async Task<bool> CanWithdrawAsync(string provider)
        {
            // The release gate comes first: while it is closed there is no
            // provider and no key that can make this true, so the toolbar button
            // never appears and no other surface has to know why.
            if (!_released) return false;
            if (await ProviderAsync(provider).ConfigureAwait(false) is null) return false;
            return await _keys.GetWithdrawalKeyAsync(provider).ConfigureAwait(false) is not null;
        }

        private async Task<IWithdrawalProvider?> ProviderAsync(string provider)
        {
            var p = await _data.GetProviderAsync(provider).ConfigureAwait(false);
            return p as IWithdrawalProvider ?? p?.GetCapability<IWithdrawalProvider>();
        }

        /// <summary>
        /// Resolves the provider and its dedicated credential together, so no path
        /// below can reach the venue without both.
        /// </summary>
        private async Task<(IWithdrawalProvider? P, string? Refusal)> ReadyAsync(string provider)
        {
            // Belt and braces. CanWithdrawAsync already keeps every surface dark,
            // but this is the single choke point every venue-reaching call passes
            // through, and a UI gate someone forgets is exactly how a dark feature
            // stops being dark. A refusal here means no request leaves the machine.
            if (!_released)
                return (null, "withdrawals are not enabled in this build");

            var p = await ProviderAsync(provider).ConfigureAwait(false);
            if (p is null)
                return (null, $"{provider} does not support withdrawals through its API");

            var key = await _keys.GetWithdrawalKeyAsync(provider).ConfigureAwait(false);
            if (key is null || string.IsNullOrEmpty(key.ApiKey))
                return (null,
                    $"no withdrawal-enabled API key is configured for {provider}. Withdrawals deliberately "
                  + "require their OWN key profile so that a trading key can never move funds — create one "
                  + "in API Keys, with withdrawal permission granted at the venue.");

            // The credential is handed to the plugin for THIS call only, exactly as
            // the trading path does at each sign site.
            if (await _data.GetProviderAsync(provider).ConfigureAwait(false) is { } raw)
                raw.Configure(new Dictionary<string, string>
                {
                    ["ApiKey"] = key.ApiKey,
                    ["ApiSecret"] = key.ApiSecret ?? "",
                    ["Passphrase"] = key.Passphrase ?? "",
                });

            return (p, null);
        }

        public async Task<ProviderResult<IReadOnlyList<WithdrawalDestination>>> GetDestinationsAsync(
            string provider, string asset, CancellationToken ct = default)
        {
            var (p, refusal) = await ReadyAsync(provider).ConfigureAwait(false);
            if (p is null) return ProviderResult<IReadOnlyList<WithdrawalDestination>>.NotPermitted(refusal!);

            try
            {
                var dests = await p.GetWithdrawalDestinationsAsync(asset, ct).ConfigureAwait(false);

                // No whitelist is a real answer with a clear fix, and the fix is not
                // here: addresses are added on the venue's own site, behind its own
                // 2FA. That is the property being protected.
                return dests.Count == 0
                    ? ProviderResult<IReadOnlyList<WithdrawalDestination>>.NotSupported(
                        $"no withdrawal addresses are saved for {asset} on {provider}. Add one on the venue's "
                      + "site first — this terminal cannot add destinations, by design.")
                    : ProviderResult<IReadOnlyList<WithdrawalDestination>>.Ok(dests);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Withdrawal destinations failed for {Asset} on {Provider}", asset, provider);
                return ProviderResult.FromException<IReadOnlyList<WithdrawalDestination>>(
                    ex, $"Reading {asset} withdrawal addresses");
            }
        }

        public async Task<ProviderResult<WithdrawalQuote>> GetQuoteAsync(
            string provider, string asset, string destinationKey, double amount, CancellationToken ct = default)
        {
            if (amount <= 0)
                return ProviderResult<WithdrawalQuote>.Failed("the amount must be greater than zero");

            var (p, refusal) = await ReadyAsync(provider).ConfigureAwait(false);
            if (p is null) return ProviderResult<WithdrawalQuote>.NotPermitted(refusal!);

            try
            {
                var q = await p.GetWithdrawalQuoteAsync(asset, destinationKey, amount, ct).ConfigureAwait(false);

                // A quote whose net is zero or negative means the fee eats the whole
                // withdrawal. Sending it would destroy the funds for nothing.
                if (q.NetAmount <= 0)
                    return ProviderResult<WithdrawalQuote>.Failed(
                        $"the fee of {Fmt(q.Fee)} {asset} is not less than the {Fmt(amount)} {asset} being sent, "
                      + "so nothing would arrive");

                if (q.MinimumAmount is > 0 && amount < q.MinimumAmount)
                    return ProviderResult<WithdrawalQuote>.Failed(
                        $"{provider}'s minimum {asset} withdrawal is {Fmt(q.MinimumAmount.Value)}");

                return ProviderResult<WithdrawalQuote>.Ok(q);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Withdrawal quote failed for {Asset} on {Provider}", asset, provider);
                return ProviderResult.FromException<WithdrawalQuote>(ex, "Quoting the withdrawal");
            }
        }

        /// <summary>
        /// Send the funds.
        /// </summary>
        /// <param name="typedConfirmation">
        /// What the user typed to confirm, which must match <see cref="ConfirmationPhrase"/>.
        /// Checked HERE rather than only in the UI, so the guard cannot be bypassed
        /// by any other caller — a confirmation enforced only by the screen that
        /// draws it is a convention, not a control.
        /// </param>
        public async Task<ProviderResult<WithdrawalResult>> WithdrawAsync(
            string provider, string asset, string destinationKey, double amount,
            string typedConfirmation, CancellationToken ct = default)
        {
            if (!string.Equals(typedConfirmation?.Trim(), ConfirmationPhrase, StringComparison.Ordinal))
                return ProviderResult<WithdrawalResult>.Failed(
                    $"the withdrawal was not confirmed — type {ConfirmationPhrase} exactly to proceed");

            var (p, refusal) = await ReadyAsync(provider).ConfigureAwait(false);
            if (p is null) return ProviderResult<WithdrawalResult>.NotPermitted(refusal!);

            try
            {
                _logger.LogWarning("Withdrawing {Amount} {Asset} from {Provider} to saved destination '{Key}'",
                    amount, asset, provider, destinationKey);

                var r = await p.WithdrawAsync(asset, destinationKey, amount, ct).ConfigureAwait(false);
                return ProviderResult<WithdrawalResult>.Ok(r);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Withdrawal FAILED for {Amount} {Asset} on {Provider}", amount, asset, provider);
                return ProviderResult.FromException<WithdrawalResult>(ex, "The withdrawal");
            }
        }

        /// <summary>
        /// Typed in full to confirm. A word rather than a button because a button is
        /// one keystroke from anywhere, and this is the one action in the terminal
        /// that cannot be undone.
        /// </summary>
        public const string ConfirmationPhrase = "WITHDRAW";

        /// <summary>
        /// The full readback, spoken before confirmation is accepted. Leads with the
        /// amount that ARRIVES rather than the amount sent, because that is the
        /// number the user is actually deciding about and the fee is what they
        /// forget.
        /// </summary>
        public static string Confirmation(WithdrawalQuote q, WithdrawalDestination to)
        {
            var parts = new List<string>
            {
                $"Withdraw {Fmt(q.Amount)} {q.Asset}.",
                $"Fee {Fmt(q.Fee)}. {Fmt(q.NetAmount)} {q.Asset} will ARRIVE.",
                $"Destination: {to.Key}" + (to.Network is null ? "." : $", on the {to.Network} network."),
            };

            if (!string.IsNullOrWhiteSpace(to.Address))
                parts.Add($"Address {to.Address}.");

            parts.Add("This cannot be undone. "
                    + $"Type {ConfirmationPhrase} to send, or leave the field empty to cancel.");
            return string.Join(" ", parts);
        }

        private static string Fmt(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);
    }
}

using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Account;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Binds ONE browser tab to its user's shared paper account, and unbinds when the tab goes.
    ///
    /// <para>
    /// Scoped, so the Blazor circuit scope owns it: constructed when the circuit opens, disposed
    /// when Blazor disposes the circuit. Disposal detaches only THIS tab's chart subscription — the
    /// account itself belongs to <see cref="PaperAccountHub"/> and survives, because the user's
    /// other tabs are still trading on it.
    /// </para>
    ///
    /// <para>
    /// Same pattern as <c>InSessionAlertRecorder</c>: the circuit handler force-creates it so the
    /// attach happens even if nothing on the page has asked for the broker yet. Without that, a
    /// resting order placed in tab A would stop being evaluated the moment tab B took focus.
    /// </para>
    /// </summary>
    public sealed class PaperAccountAttachment : IDisposable
    {
        private readonly IDisposable _attachment;

        public PaperTradingProvider Account { get; }

        public PaperAccountAttachment(
            PaperAccountHub hub,
            IWorkspaceStore store,
            IPlatformPathService paths,
            ILogger<PaperTradingProvider> accountLogger,
            IEventBus eventBus,
            IDataService dataService,
            ICurrentUser? currentUser = null,
            AccessibleTrader.Core.Services.DemoPolicy? demo = null)
        {
            // PaperTradingProvider reads IPlatformPathService.AppDataDirectory in its
            // constructor, and UserScopedPathService's contract is "computed on access,
            // AFTER the circuit handler has set ICurrentUser". On the hosted head that
            // ordering is what keeps accounts apart: resolve this from a pre-circuit
            // scope (or re-enable prerendering in App.razor) and every user's paper
            // account would silently become users/anon/paper_account.json — one shared
            // account for the whole site. Money state must fail loudly, so refuse.
            if (demo?.IsHosted == true && currentUser?.IsAuthenticated != true)
                throw new InvalidOperationException(
                    "PaperAccountAttachment resolved before the circuit user was known. On the " +
                    "hosted head the paper account is per-user; creating it from a pre-circuit " +
                    "scope (or during prerendering) would bind every user to the shared 'anon' " +
                    "account. Resolve IPaperTradingProvider only from inside an authenticated " +
                    "circuit — see UserScopedPathService's computed-on-access contract.");

            string key = currentUser?.DataKey ?? "anon";

            Account = hub.ForUser(key, () =>
                new PaperTradingProvider(store, paths, accountLogger, eventBus, dataService));

            // The account may pre-date this tab, so attach unconditionally — ForUser only runs the
            // factory (which attaches once itself) for a user who had none.
            //
            // The creating tab CLAIMS the constructor's attachment rather than being handed a
            // no-op. A no-op meant the first tab never detached: its store kept a live
            // subscription after the circuit was gone, and the account went on resolving
            // identities and prices against a chart nobody was looking at. TakePrimaryAttachment
            // hands the token over exactly once, so a second circuit that happens to be
            // constructed with the same store still attaches normally.
            _attachment = ReferenceEquals(Account.PrimaryStore, store)
                ? Account.TakePrimaryAttachment()
                : Account.Attach(store, eventBus);
        }

        public void Dispose() => _attachment.Dispose();
    }
}

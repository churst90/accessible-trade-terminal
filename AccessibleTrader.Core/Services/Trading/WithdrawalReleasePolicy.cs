namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// The withdrawal release gate as a VALUE a component is handed, rather than a static it
    /// reads.
    ///
    /// <para>
    /// <see cref="WithdrawalService"/> already takes the gate as a constructor argument for
    /// exactly this reason — so its tests can drive the behaviour 2.3.1 will turn on while DI
    /// keeps getting the dark default. The two markup surfaces had no such seam, so the tests
    /// covering them were reduced to grepping the .razor source for the string
    /// "WithdrawalService.Released" — which passes just as happily on
    /// <c>@if (WithdrawalService.Released || _debug)</c>. This is the same seam for the markup.
    /// </para>
    ///
    /// <para>
    /// Deliberately NOT a mutable static: xUnit runs test classes in parallel, and a global flag
    /// one class flipped would make another class's "it stays dark" assertion fail at random.
    /// That is the same reasoning recorded on <see cref="WithdrawalService"/>'s internal
    /// constructor, and it still holds.
    /// </para>
    ///
    /// <para>
    /// <see cref="From"/> falls back to <see cref="Shipped"/> when nothing is registered, so a
    /// host that forgets the registration behaves exactly as it did before this type existed —
    /// closed. A missing registration must not be able to open the gate, and must not be able to
    /// crash the API Keys dialog either.
    /// </para>
    /// </summary>
    public sealed class WithdrawalReleasePolicy
    {
        /// <summary>What the app actually ships with — <see cref="WithdrawalService.Released"/>.</summary>
        public static readonly WithdrawalReleasePolicy Shipped = new(WithdrawalService.Released);

        public WithdrawalReleasePolicy(bool released) => Released = released;

        public bool Released { get; }

        /// <summary>
        /// Resolve the registered policy, or the shipped default. Uses
        /// <see cref="IServiceProvider.GetService(Type)"/> rather than the DI extension so this
        /// type stays free of a Microsoft.Extensions dependency, and so an unregistered host
        /// gets the closed default instead of an exception mid-render.
        /// </summary>
        public static WithdrawalReleasePolicy From(IServiceProvider? services) =>
            services?.GetService(typeof(WithdrawalReleasePolicy)) as WithdrawalReleasePolicy ?? Shipped;
    }
}

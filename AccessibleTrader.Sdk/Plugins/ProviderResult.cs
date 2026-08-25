namespace AccessibleTrader.Sdk.Plugins
{
    /// <summary>
    /// Why a provider read returned what it did.
    ///
    /// <para>
    /// An empty list used to mean all of these at once. That is the dominant
    /// failure mode in this codebase's provider layer, and it is not cosmetic:
    /// broker reconciliation compared a FAILED positions fetch against its saved
    /// snapshot, announced every position as "closed while you were away", and
    /// then overwrote the snapshot with the empty result — so a transient network
    /// hiccup reported the account flat and destroyed the record that would have
    /// corrected it.
    /// </para>
    /// </summary>
    public enum ResultKind
    {
        /// <summary>Answered. <c>Value</c> is real, and may legitimately be empty.</summary>
        Ok,

        /// <summary>
        /// The provider has no such concept — a spot venue asked for positions,
        /// where what you hold IS your balance. Not an error, and the UI should
        /// say so rather than showing an empty table.
        /// </summary>
        NotSupported,

        /// <summary>
        /// The venue understood and refused: key lacks the scope, KYC incomplete,
        /// account not enabled for this product. The fix is at the venue, so the
        /// user needs telling rather than a silent blank.
        /// </summary>
        NotPermitted,

        /// <summary>
        /// Nothing to ask — no provider configured, not connected, no credentials.
        /// Distinct from a failure, because nothing went wrong.
        /// </summary>
        Unavailable,

        /// <summary>
        /// The call was made and did not come back cleanly. **Never treat the
        /// value as data.** Anything that writes state from a read must refuse on
        /// this, or it records an outage as a fact.
        /// </summary>
        Failed,
    }

    /// <summary>
    /// A provider read, with the reason it turned out the way it did.
    ///
    /// <para>
    /// Deliberately at the SERVICE boundary rather than on <c>ITradingProvider</c>:
    /// the service already knows everything needed to classify — whether a
    /// provider exists, whether it is connected, what it declares, and whether the
    /// call threw — so ten plugin contracts did not have to change to get this.
    /// </para>
    /// </summary>
    public sealed record ProviderResult<T>(ResultKind Kind, T? Value, string? Reason)
    {
        public bool IsOk => Kind == ResultKind.Ok;

        /// <summary>
        /// The data when the read succeeded, otherwise <paramref name="fallback"/>.
        /// Use ONLY where an empty display is acceptable — never where the result
        /// is written back to disk or compared against saved state.
        /// </summary>
        public T OrElse(T fallback) => Kind == ResultKind.Ok && Value is not null ? Value : fallback;

        public static ProviderResult<T> Ok(T value) => new(ResultKind.Ok, value, null);
        public static ProviderResult<T> NotSupported(string reason) => new(ResultKind.NotSupported, default, reason);
        public static ProviderResult<T> NotPermitted(string reason) => new(ResultKind.NotPermitted, default, reason);
        public static ProviderResult<T> Unavailable(string reason) => new(ResultKind.Unavailable, default, reason);
        public static ProviderResult<T> Failed(string reason) => new(ResultKind.Failed, default, reason);
    }

    /// <summary>Classifies provider exceptions into a <see cref="ResultKind"/>.</summary>
    public static class ProviderResult
    {
        /// <summary>
        /// Whether an exception is the venue refusing rather than breaking. A 401
        /// or 403 is actionable by the user — regenerate the key, add the scope,
        /// finish verification — while a timeout is not, so the two must not read
        /// the same.
        /// </summary>
        public static bool IsPermissionError(System.Exception ex)
        {
            if (ex is System.UnauthorizedAccessException) return true;

            if (ex is System.Net.Http.HttpRequestException http && http.StatusCode is { } code)
                return code == System.Net.HttpStatusCode.Unauthorized
                    || code == System.Net.HttpStatusCode.Forbidden;

            // Several plugins surface the venue's own wording without a typed status.
            string m = ex.Message;
            return m.Contains("401") || m.Contains("403")
                || m.Contains("Unauthorized", System.StringComparison.OrdinalIgnoreCase)
                || m.Contains("Forbidden", System.StringComparison.OrdinalIgnoreCase)
                || m.Contains("permission", System.StringComparison.OrdinalIgnoreCase)
                || m.Contains("invalid api key", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Turns a caught exception into the right kind of failure.</summary>
        public static ProviderResult<T> FromException<T>(System.Exception ex, string what) =>
            IsPermissionError(ex)
                ? ProviderResult<T>.NotPermitted($"{what} was refused — check the API key's permissions on the venue")
                : ProviderResult<T>.Failed($"{what} failed: {ex.Message}");
    }
}

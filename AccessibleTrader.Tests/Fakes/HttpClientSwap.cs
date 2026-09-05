using System.Net.Http;
using System.Reflection;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Replaces a provider's <see cref="HttpClient"/> with one wired to a fake handler — EVERY
    /// client it holds, never "the first one".
    ///
    /// <para>
    /// <b>Why this exists.</b> Seventeen test files did the swap by hand as
    /// <c>GetFields(...).First(f =&gt; f.FieldType == typeof(HttpClient))</c>. Three providers
    /// carry two clients (<c>TradierProvider</c> and <c>OandaProvider</c> — <c>_httpClient</c> plus
    /// <c>_streamClient</c>; MEXC across two classes), so which one <c>First</c> returned was CLR
    /// field-declaration order: not guaranteed, and one reordering away from faking the stream
    /// client while <c>PlaceOrderAsync</c> went to the REAL venue — invisible to the fake's strict
    /// mode, because the client that made the call was never wired to the fake at all. Raised
    /// from the hosted deployment (notes §5a) after <c>BrokerParityTests.Swap</c> was caught doing
    /// it; that one site was fixed by name, and this is the repo-wide half.
    /// </para>
    ///
    /// <para>
    /// The rule is "replace them ALL", not "find the right one by name": a test that fakes every
    /// client an object can reach cannot make a real call through any of them, whatever the
    /// fields are called, and a provider that grows a second client is covered on the day it
    /// does. <see cref="HttpClientSwapScanTests"/> fails any test file that goes back to picking
    /// one by position.
    /// </para>
    /// </summary>
    public static class HttpClientSwap
    {
        /// <summary>
        /// Points every <see cref="HttpClient"/>-typed instance field of <paramref name="target"/>
        /// (base types included) at a fresh client over <paramref name="handler"/>. Throws when
        /// the object holds none — a swap that swapped nothing is a test talking to the network.
        /// </summary>
        public static void ReplaceAll(object target, HttpMessageHandler handler)
        {
            var fields = ClientFields(target).ToList();
            if (fields.Count == 0)
                throw new InvalidOperationException(
                    $"{target.GetType().Name} has no HttpClient-typed instance field to replace.");
            foreach (var f in fields)
                f.SetValue(target, new HttpClient(handler));
        }

        /// <summary>
        /// The one <see cref="HttpClient"/> the object holds, for tests that inspect it (disposal,
        /// default headers). Throws when there is not exactly one — a reader that silently took
        /// the first of two is the defect this class exists to prevent.
        /// </summary>
        public static HttpClient Single(object target)
        {
            var fields = ClientFields(target).ToList();
            if (fields.Count != 1)
                throw new InvalidOperationException(
                    $"{target.GetType().Name} holds {fields.Count} HttpClient fields "
                    + $"({string.Join(", ", fields.Select(f => f.Name))}); Single() needs exactly one.");
            return (HttpClient)fields[0].GetValue(target)!;
        }

        private static IEnumerable<FieldInfo> ClientFields(object target)
        {
            for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public
                                             | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (f.FieldType == typeof(HttpClient)) yield return f;
        }
    }
}

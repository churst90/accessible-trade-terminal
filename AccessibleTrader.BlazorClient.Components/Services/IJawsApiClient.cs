using System.Runtime.Versioning;

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// JAWS, through the COM automation object it registers when it installs.
    ///
    /// <para>
    /// <b>Why this exists at all, and why it is not Prism.</b> On the desktop head the chart is a
    /// native SkiaSharp canvas sitting on top of the <c>BlazorWebView</c>, so a screen reader
    /// following focus onto the chart is not reading the web view's DOM and the ARIA live region
    /// announces to nobody. NVDA got a direct path out of that on 2026-09-21; JAWS had none, so a
    /// JAWS user met exactly the silence that day began with — permanently, and with no
    /// workaround. That is a whole category of user who could not use the application.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing ships for this.</b> <c>FreedomSci.JawsApi</c> is registered by the JAWS
    /// installer, so it is late-bound by ProgID and simply resolves to nothing on a machine
    /// without JAWS. No vendored binary, no per-RID native asset, no build staging — which
    /// matters, because every staging mechanism in this repository has been found broken at
    /// least once, and the cheapest payload is the one that does not exist.
    /// </para>
    /// </summary>
    public interface IJawsApiClient
    {
        /// <summary>Whether JAWS is installed AND running right now. Both, and neither is
        /// permanent: the COM object can be created only while JAWS is up.</summary>
        bool IsReaderRunning();

        /// <summary>Speak, optionally flushing whatever JAWS is currently saying.</summary>
        void Speak(string text, bool interrupt);

        /// <summary>Stop the current utterance.</summary>
        void StopSpeech();
    }

    /// <summary>
    /// The real thing, via late-bound COM. Reflection rather than a typed interop assembly
    /// because a typed reference would have to be present at build time on a machine that has
    /// JAWS, and this repository builds on Linux.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class JawsApiClient : IJawsApiClient
    {
        private const string ProgId = "FreedomSci.JawsApi";

        private object? _api;

        /// <summary>
        /// The COM object is cached once created, but its ABSENCE never is: JAWS may be started
        /// after the terminal, or restarted after a crash, and a latched "not available" is the
        /// precise bug that kept the NVDA path dead for a whole session before it was fixed.
        /// </summary>
        private object? Api
        {
            get
            {
                if (_api != null) return _api;
                try
                {
                    var t = Type.GetTypeFromProgID(ProgId, throwOnError: false);
                    if (t == null) return null;                 // JAWS not installed
                    _api = Activator.CreateInstance(t);         // throws when JAWS is not running
                }
                catch
                {
                    _api = null;
                }
                return _api;
            }
        }

        public bool IsReaderRunning() => Api != null;

        public void Speak(string text, bool interrupt)
        {
            var api = Api;
            if (api == null) return;
            try
            {
                // SayString(string text, bool flush) — flush true interrupts.
                api.GetType().InvokeMember("SayString",
                    System.Reflection.BindingFlags.InvokeMethod, null, api,
                    new object[] { text, interrupt });
            }
            catch
            {
                // JAWS went away mid-call. Drop the cached object so the next attempt re-creates
                // it rather than calling into a dead apartment for the rest of the session.
                _api = null;
                throw;
            }
        }

        public void StopSpeech()
        {
            var api = Api;
            if (api == null) return;
            try
            {
                api.GetType().InvokeMember("StopSpeech",
                    System.Reflection.BindingFlags.InvokeMethod, null, api, null);
            }
            catch
            {
                _api = null;
            }
        }
    }

    /// <summary>Every head that is not Windows. Reports JAWS absent, which is the truth, and
    /// keeps a COM call off a server path.</summary>
    public sealed class NullJawsApiClient : IJawsApiClient
    {
        public bool IsReaderRunning() => false;
        public void Speak(string text, bool interrupt) { }
        public void StopSpeech() { }
    }
}

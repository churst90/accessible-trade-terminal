using System.Runtime.InteropServices;

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// The NVDA Controller Client, as a PARAMETER rather than as three <c>DllImport</c>s reached
    /// straight from the speech manager.
    ///
    /// <para>
    /// The reason is the 2026-09-21 Windows report and what it exposed: the speech manager
    /// decided ONCE, in its constructor, whether NVDA was reachable, latched the answer, and had
    /// no way to say that it had latched it to "no". None of that logic could be exercised
    /// without a Windows box with NVDA on it, so none of it ever was. Behind this interface the
    /// decision is ordinary code with ordinary tests, and the P/Invoke is the only part that
    /// still needs a real machine.
    /// </para>
    /// </summary>
    public interface INvdaControllerClient
    {
        /// <summary>
        /// Whether the client LIBRARY can be loaded at all — a fact about the INSTALL, not about
        /// the user. False means <c>nvdaControllerClient64.dll</c> is not next to the binary, and
        /// no amount of waiting will change it: the build did not stage it.
        /// </summary>
        bool IsClientLibraryAvailable { get; }

        /// <summary>
        /// Whether NVDA is running RIGHT NOW — a fact about the user, and a changing one. They
        /// may start NVDA after the terminal, restart it, or switch readers mid-session.
        /// </summary>
        bool IsReaderRunning();

        void Speak(string text);
        void CancelSpeech();
    }

    /// <summary>The real thing: <c>nvdaControllerClient64.dll</c>, staged next to the host binary
    /// by <c>CopyNvdaControllerWindows</c> in the BlazorClient csproj.</summary>
    public sealed class NvdaControllerClient : INvdaControllerClient
    {
        private bool? _libraryAvailable;

        public bool IsClientLibraryAvailable => _libraryAvailable ??= Probe();

        private static bool Probe()
        {
            // A missing DLL throws DllNotFoundException on the FIRST call into it, not at load,
            // so probing is the only way to ask. Any answer at all — including "NVDA is not
            // running" — proves the library resolved.
            try { Native.TestIfRunning(); return true; }
            catch { return false; }
        }

        public bool IsReaderRunning()
        {
            if (!IsClientLibraryAvailable) return false;
            try { return Native.TestIfRunning() == 0; }
            catch { return false; }
        }

        public void Speak(string text) => Native.SpeakText(text);
        public void CancelSpeech() => Native.CancelSpeech();

        private static class Native
        {
            private const string DllName = "nvdaControllerClient64.dll";

            [DllImport(DllName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_testIfRunning")]
            public static extern int TestIfRunning();

            [DllImport(DllName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_speakText")]
            public static extern int SpeakText(string text);

            [DllImport(DllName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, EntryPoint = "nvdaController_cancelSpeech")]
            public static extern int CancelSpeech();
        }
    }

    /// <summary>
    /// Every head that is not Windows-with-NVDA. Reports the library as unavailable, which is the
    /// truth and which routes the speech manager to its live-region path without a P/Invoke
    /// attempt per utterance.
    /// </summary>
    public sealed class NullNvdaControllerClient : INvdaControllerClient
    {
        public bool IsClientLibraryAvailable => false;
        public bool IsReaderRunning() => false;
        public void Speak(string text) { }
        public void CancelSpeech() { }
    }

    /// <summary>
    /// Why the terminal has no way to speak. Carried on
    /// <see cref="BlazorSpeechManager.OutputStatus"/> so the condition is a value anyone can ASK
    /// for, rather than an absence the user has to infer from silence.
    /// </summary>
    public enum SpeechOutputStatus
    {
        /// <summary>Speaking through the NVDA Controller Client.</summary>
        NvdaDirect,
        /// <summary>Speaking into the ARIA live region for whatever reader is watching the DOM.</summary>
        LiveRegion,
        /// <summary>
        /// The client library is absent AND no live region is attached — the terminal is MUTE.
        /// On the desktop head this is reachable on an ordinary launch, because the chart is a
        /// native canvas over the web view and the live region is only attached while a Blazor
        /// layout is rendered.
        /// </summary>
        Mute,
    }
}

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
            /// <summary>
            /// <b>NV Access renamed this file, and the old name is the one every instruction
            /// ever written for this app tells a user to look for.</b>
            ///
            /// <para>
            /// Current <c>controllerClient</c> downloads ship <c>x64/nvdaControllerClient.dll</c>.
            /// Releases from some years back shipped <c>nvdaControllerClient64.dll</c>, and that
            /// is the name this code has always P/Invoked — including the copy that was
            /// hand-dropped into a bin folder in March, which is why that one machine worked.
            /// On 2026-09-21 Cody downloaded the current client, put it beside the executable
            /// exactly as told, and the terminal stayed silent: the file was there and nothing
            /// was looking for that name.
            /// </para>
            ///
            /// <para>
            /// A resolver rather than picking one name, because a user who already has the old
            /// file must not be broken by the fix, and a user following NV Access's current
            /// download must not have to rename anything. The bare name is tried first so the
            /// normal .NET probing (which includes the application directory) still applies.
            /// </para>
            /// </summary>
            private static readonly string[] CandidateNames =
            {
                "nvdaControllerClient.dll",      // current NV Access naming
                "nvdaControllerClient64.dll",    // historical, and what this code used to demand
            };

            private const string DllName = "nvdaControllerClient.dll";

            static Native()
            {
                try
                {
                    NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, asm, path) =>
                    {
                        if (!string.Equals(name, DllName, StringComparison.OrdinalIgnoreCase))
                            return IntPtr.Zero;

                        foreach (var candidate in CandidateNames)
                            if (NativeLibrary.TryLoad(candidate, asm, path, out var handle))
                                return handle;

                        return IntPtr.Zero;
                    });
                }
                catch (InvalidOperationException)
                {
                    // A resolver is already set for this assembly. Nothing to do: the first one
                    // wins and setting it twice is the only error this call can raise.
                }
            }

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

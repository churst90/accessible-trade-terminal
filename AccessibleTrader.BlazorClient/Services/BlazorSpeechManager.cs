using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.BlazorClient.Services
{
    public class BlazorSpeechManager : ISpeechManager, IDisposable
    {
        private readonly ILogger<BlazorSpeechManager> _logger;
        private bool? _isNvdaAvailable;
        private string _queuedText = string.Empty;

        public bool IsActive => (_isNvdaAvailable == true) || OnSpeak != null;
        public string SpeechMode => (_isNvdaAvailable == true) ? "NVDA Direct" : (OnSpeak != null ? "ARIA Live" : "None");
        public bool IsSpeechEnabled { get; set; } = true;
        
        private Action<string>? _onSpeak;
        public Action<string>? OnSpeak 
        { 
            get => _onSpeak; 
            set 
            {
                _onSpeak = value;
                if (_onSpeak != null && !string.IsNullOrEmpty(_queuedText))
                {
                    _onSpeak(_queuedText);
                    _queuedText = string.Empty;
                }
            } 
        }

        public BlazorSpeechManager(ILogger<BlazorSpeechManager> logger)
        {
            _logger = logger;
            // Immediate check, then background monitor
            _isNvdaAvailable = CheckNvdaNative();
        }

        private bool CheckNvdaNative()
        {
            try
            {
                // Test if DLL is reachable and NVDA is running
                return NvdaNative.TestIfRunning() == 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"NVDA DLL not found or incompatible: {ex.Message}");
                return false;
            }
        }

        public void Speak(string text, bool interrupt = false)
        {
            if (string.IsNullOrWhiteSpace(text) || !IsSpeechEnabled) return;

            // Always try NVDA first
            if (_isNvdaAvailable == true)
            {
                try
                {
                    if (interrupt) NvdaNative.CancelSpeech();
                    NvdaNative.SpeakText(text);
                    return; // Exit if NVDA handled it
                }
                catch
                {
                    _isNvdaAvailable = false; // Fallback to ARIA
                }
            }

            // Fallback to ARIA Live
            if (OnSpeak != null)
            {
                OnSpeak(text);
            }
            else
            {
                _queuedText = text;
            }
        }
public void Silence()
{
    if (_isNvdaAvailable == true)
    {
        try { NvdaNative.CancelSpeech(); } catch { }
    }

    OnSpeak?.Invoke("");
    _queuedText = "";
}
        public void Dispose() { }

        private static class NvdaNative
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
}

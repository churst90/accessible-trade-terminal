namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// Minimal, dependency-free RIFF/WAVE parser for user imports (wavetables and
    /// one-shot samples). Supports PCM 8/16/24/32-bit and IEEE float32, any channel
    /// count (channels are averaged to mono — sonification voices are positioned by
    /// the engine's own panner, so imported stereo is flattened).
    ///
    /// Deliberately strict where safety matters (bounds-checked chunk walking,
    /// length caps) and lenient where files vary (unknown chunks are skipped;
    /// trailing bytes ignored).
    /// </summary>
    public static class WavFileReader
    {
        /// <summary>Hard cap on decoded frames (~60 s @ 48 kHz) — an import is an
        /// earcon or a single cycle, never an album track.</summary>
        public const int MaxFrames = 2_880_000;

        public static bool TryParse(byte[] bytes, out float[] mono, out int sampleRate, out string error)
        {
            mono = Array.Empty<float>();
            sampleRate = 0;
            error = "";
            try
            {
                if (bytes == null || bytes.Length < 44) { error = "File too short to be a WAV."; return false; }
                if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F'
                    || bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
                { error = "Not a RIFF/WAVE file."; return false; }

                int pos = 12;
                int format = 0, channels = 0, bitsPerSample = 0;
                int dataStart = -1, dataLen = 0;

                while (pos + 8 <= bytes.Length)
                {
                    uint chunkId = ReadU32(bytes, pos);
                    int chunkLen = (int)ReadU32(bytes, pos + 4);
                    int body = pos + 8;
                    if (chunkLen < 0 || body + chunkLen > bytes.Length)
                        chunkLen = bytes.Length - body; // tolerate truncated final chunk

                    if (chunkId == 0x20746d66) // "fmt "
                    {
                        if (chunkLen < 16) { error = "Malformed fmt chunk."; return false; }
                        format        = ReadU16(bytes, body);
                        channels      = ReadU16(bytes, body + 2);
                        sampleRate    = (int)ReadU32(bytes, body + 4);
                        bitsPerSample = ReadU16(bytes, body + 14);
                        // WAVE_FORMAT_EXTENSIBLE: real format is in the extension GUID's first word.
                        if (format == 0xFFFE && chunkLen >= 26)
                            format = ReadU16(bytes, body + 24);
                    }
                    else if (chunkId == 0x61746164) // "data"
                    {
                        dataStart = body;
                        dataLen = chunkLen;
                    }
                    pos = body + chunkLen + (chunkLen & 1); // chunks are word-aligned
                }

                if (dataStart < 0) { error = "No data chunk."; return false; }
                if (channels <= 0 || sampleRate <= 0) { error = "Missing or malformed fmt chunk."; return false; }
                bool isFloat = format == 3;
                bool isPcm = format == 1;
                if (!isFloat && !isPcm) { error = $"Unsupported WAV format {format} (PCM and float32 only)."; return false; }
                if (isFloat && bitsPerSample != 32) { error = "Float WAV must be 32-bit."; return false; }
                if (isPcm && bitsPerSample is not (8 or 16 or 24 or 32)) { error = $"Unsupported PCM bit depth {bitsPerSample}."; return false; }

                int bytesPerSample = bitsPerSample / 8;
                int frameBytes = bytesPerSample * channels;
                int frames = Math.Min(dataLen / frameBytes, MaxFrames);
                if (frames <= 0) { error = "Empty data chunk."; return false; }

                mono = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    double sum = 0;
                    int frameOff = dataStart + f * frameBytes;
                    for (int c = 0; c < channels; c++)
                    {
                        int off = frameOff + c * bytesPerSample;
                        sum += bitsPerSample switch
                        {
                            8  => (bytes[off] - 128) / 128.0,                       // PCM8 is unsigned
                            16 => (short)(bytes[off] | bytes[off + 1] << 8) / 32768.0,
                            24 => (((bytes[off] | bytes[off + 1] << 8 | bytes[off + 2] << 16) << 8) >> 8) / 8388608.0,
                            32 => isFloat
                                ? BitConverter.ToSingle(bytes, off)
                                : BitConverter.ToInt32(bytes, off) / 2147483648.0,
                            _  => 0.0
                        };
                    }
                    mono[f] = (float)Math.Clamp(sum / channels, -1.0, 1.0);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"WAV parse failed: {ex.Message}";
                mono = Array.Empty<float>();
                return false;
            }
        }

        private static uint ReadU32(byte[] b, int off) =>
            (uint)(b[off] | b[off + 1] << 8 | b[off + 2] << 16 | b[off + 3] << 24);
        private static int ReadU16(byte[] b, int off) => b[off] | b[off + 1] << 8;
    }
}

// Browser WebAudio sink for the WebHost. The server's WebHostAudioDriver
// (browser-fallback mode) emits float32 stereo PCM at 44.1 kHz; chunks arrive
// here over the SignalR circuit via accessibleTrader.audioPush(base64).
//
// Buffers are scheduled head-to-tail on the AudioContext clock so they play
// gap-free as long as the producer keeps up. Browsers gate AudioContext until
// a user gesture — we lazy-init on the first push *and* on any document
// keydown/click, and silently log if the context can't resume yet.
(function () {
    window.accessibleTrader = window.accessibleTrader || {};

    const SAMPLE_RATE = 44100;
    const CHANNELS = 2;
    const BYTES_PER_FLOAT = 4;

    let ctx = null;
    let nextStartTime = 0;
    let pendingResume = false;

    function ensureContext() {
        if (ctx) return ctx;
        try {
            ctx = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: SAMPLE_RATE });
            nextStartTime = ctx.currentTime;
        } catch (e) {
            return null;
        }
        // First user gesture flips the context from "suspended" to "running".
        // Listening on the capture phase so we win against page handlers that
        // stopPropagation.
        const wake = function () {
            if (ctx && ctx.state === 'suspended') {
                ctx.resume().catch(function () {});
            }
        };
        document.addEventListener('keydown', wake, true);
        document.addEventListener('click',  wake, true);
        document.addEventListener('pointerdown', wake, true);
        return ctx;
    }

    function base64ToFloat32(b64) {
        // atob → binary string → Uint8Array → Float32Array sharing the same buffer.
        const bin = atob(b64);
        const len = bin.length;
        const u8 = new Uint8Array(len);
        for (let i = 0; i < len; i++) u8[i] = bin.charCodeAt(i);
        // Endianness: server writes little-endian float32 (.NET on x64/ARM64 is LE).
        return new Float32Array(u8.buffer, u8.byteOffset, len / BYTES_PER_FLOAT);
    }

    window.accessibleTrader.audioPush = function (base64Chunk) {
        const c = ensureContext();
        if (!c) return;
        if (c.state === 'suspended') {
            // Try once per push; cheap if already pending.
            if (!pendingResume) {
                pendingResume = true;
                c.resume().finally(function () { pendingResume = false; });
            }
            // Drop this chunk rather than queueing into a context that may never
            // start — would burst-play once it does.
            return;
        }

        const interleaved = base64ToFloat32(base64Chunk);
        const frameCount = interleaved.length / CHANNELS;
        if (frameCount <= 0) return;

        const buf = c.createBuffer(CHANNELS, frameCount, SAMPLE_RATE);
        const left = buf.getChannelData(0);
        const right = buf.getChannelData(1);
        for (let i = 0; i < frameCount; i++) {
            left[i]  = interleaved[i * 2];
            right[i] = interleaved[i * 2 + 1];
        }

        const src = c.createBufferSource();
        src.buffer = buf;
        src.connect(c.destination);

        // Schedule contiguously. Two resyncs keep latency low:
        //  - Underrun: the queue ran dry (chunk arrived late) — restart at
        //    present + a small startup pad so we don't start in the past.
        //  - Overrun: a burst of chunks (SignalR delivers in batches) pushed
        //    the schedule far ahead of the clock. That lead is permanent added
        //    latency, so cap it and snap back near the present. A momentary
        //    overlap is inaudible for navigation blips and worth the
        //    responsiveness.
        const now = c.currentTime;
        const MAX_LEAD = 0.080; // seconds of scheduling lead we tolerate
        if (nextStartTime < now + 0.005 || nextStartTime > now + MAX_LEAD) {
            nextStartTime = now + 0.020;
        }
        src.start(nextStartTime);
        nextStartTime += frameCount / SAMPLE_RATE;
    };

    window.accessibleTrader.audioState = function () {
        return ctx ? ctx.state : 'uninitialized';
    };
})();

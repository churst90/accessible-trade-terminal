namespace AccessibleTrader.ScriptSandbox;

/// <summary>
/// Wire-level opcodes for the host ↔ worker IPC. Values are stable — do not
/// renumber. The high bit (0x80) distinguishes worker → host responses from
/// host → worker commands as a cheap sanity check on the stream.
/// </summary>
public enum Opcode : byte
{
    // ── Host → Worker ──────────────────────────────────────────────────
    /// <summary>
    /// Load a compiled assembly into the worker's collectible ALC. Payload:
    /// u32 length-prefixed byte blob of a .NET PE file. Exactly one
    /// <c>LoadAssembly</c> per worker lifetime — the worker instantiates the
    /// first <c>ICustomIndicator</c> type it finds and replies with a
    /// <see cref="Ready"/> frame carrying the indicator's metadata.
    /// </summary>
    LoadAssembly = 0x01,

    /// <summary>
    /// Request one <c>ICustomIndicator.Calculate</c> pass. Payload is
    /// <see cref="CalculateRequest"/> in its binary form. Response is
    /// <see cref="Result"/> on success or <see cref="Error"/> on failure.
    /// </summary>
    Calculate = 0x02,

    /// <summary>
    /// Graceful shutdown. Worker drains pending frames, disposes the ALC,
    /// and exits. Host gives it a short grace window then sends SIGKILL.
    /// Payload is empty.
    /// </summary>
    Shutdown = 0xFF,

    // ── Worker → Host ──────────────────────────────────────────────────
    /// <summary>
    /// Sent once after successful <see cref="LoadAssembly"/>. Payload is
    /// <see cref="IndicatorMetadataMessage"/>. Any subsequent
    /// <see cref="Ready"/> is a protocol error.
    /// </summary>
    Ready = 0x81,

    /// <summary>
    /// Calculate succeeded. Payload is <see cref="CalculateResponse"/>.
    /// </summary>
    Result = 0x82,

    /// <summary>
    /// A frame failed (bad LoadAssembly, exception in Calculate, decode
    /// error, etc). Payload is a UTF-8 error message. The worker stays
    /// running — it's the host's choice whether to send another frame or
    /// <see cref="Shutdown"/>.
    /// </summary>
    Error = 0x83,

    /// <summary>
    /// Non-fatal diagnostic from the worker. Payload is a UTF-8 log line.
    /// Host should mirror into its journal / logger. Workers SHOULD NOT
    /// spam this — one line per meaningful event (startup, unload, etc).
    /// </summary>
    Diagnostic = 0x84,
}

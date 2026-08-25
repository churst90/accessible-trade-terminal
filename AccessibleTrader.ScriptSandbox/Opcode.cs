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
    /// <c>LoadAssembly</c> per worker lifetime.
    ///
    /// <para>
    /// The worker looks for an <c>ICustomIndicator</c> first and replies with
    /// <see cref="Ready"/>; failing that it looks for an <c>ITradingStrategy</c>
    /// and replies with <see cref="StrategyReady"/>. One opcode with two answers
    /// rather than two opcodes, so the worker — not the host — is what decides
    /// what a compiled assembly actually is.
    /// </para>
    /// </summary>
    LoadAssembly = 0x01,

    /// <summary>
    /// Request one <c>ICustomIndicator.Calculate</c> pass. Payload is
    /// <see cref="CalculateRequest"/> in its binary form. Response is
    /// <see cref="Result"/> on success or <see cref="Error"/> on failure.
    /// </summary>
    Calculate = 0x02,

    // ── Strategy frames ────────────────────────────────────────────────
    // A strategy is the half of the scripting surface that places orders, so
    // it is the half that most needed to leave the host process. The protocol
    // mirrors ITradingStrategy one opcode per method; the only place it does
    // NOT mirror the interface is history, which is sent incrementally (see
    // StrategyCodec.OnBarRequest) because a 10k-bar backtest re-sending the
    // whole buffer 10k times moves gigabytes to say nothing new.

    /// <summary>
    /// <c>ITradingStrategy.Initialize</c>. Payload is
    /// <see cref="InitializeStrategyRequest"/>. Response is <see cref="Ack"/>
    /// or <see cref="Error"/>.
    ///
    /// <para>
    /// Every <c>InitializeStrategy</c> discards the current instance and
    /// constructs a fresh one from the loaded type before calling Initialize.
    /// That is what lets the causality probe — which needs a virgin instance
    /// per run and cannot reach <c>Activator</c> across a process boundary —
    /// drive the proxy exactly as it drives an in-process strategy.
    /// </para>
    /// </summary>
    InitializeStrategy = 0x03,

    /// <summary>
    /// <c>ITradingStrategy.OnBar</c>. Payload is <see cref="OnBarRequest"/>.
    /// Response is <see cref="Signal"/> (which may carry "no order") or
    /// <see cref="Error"/>.
    /// </summary>
    OnBar = 0x04,

    /// <summary>
    /// <c>ITradingStrategy.OnOrderFilled</c>. Payload is an encoded
    /// <c>OrderUpdate</c>. Response is <see cref="Ack"/> or <see cref="Error"/>.
    /// </summary>
    OrderFilled = 0x05,

    /// <summary>
    /// <c>ITradingStrategy.OnStop</c>. Empty payload; response is
    /// <see cref="Ack"/>. Does NOT end the worker — the instance stays loaded
    /// so metrics can still be read and a fresh run can be started with
    /// <see cref="InitializeStrategy"/>. <see cref="Shutdown"/> is what ends
    /// the worker.
    /// </summary>
    StopStrategy = 0x06,

    /// <summary>
    /// <c>ITradingStrategy.GetMetrics</c>. Empty payload; response is
    /// <see cref="Metrics"/> or <see cref="Error"/>.
    /// </summary>
    GetMetrics = 0x07,

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
    /// Sent once after a successful <see cref="LoadAssembly"/> that found an
    /// <c>ITradingStrategy</c> rather than an <c>ICustomIndicator</c>. Payload
    /// is <see cref="StrategyMetadataMessage"/>.
    /// </summary>
    StrategyReady = 0x85,

    /// <summary>
    /// <see cref="OnBar"/> succeeded. Payload is <see cref="SignalResponse"/>,
    /// whose leading presence byte distinguishes "no order this bar" from an
    /// order — a distinction an empty payload could not make, and the one the
    /// causality probe compares on.
    /// </summary>
    Signal = 0x86,

    /// <summary>
    /// A void-returning strategy frame succeeded. Empty payload.
    /// </summary>
    Ack = 0x87,

    /// <summary>
    /// <see cref="GetMetrics"/> succeeded. Payload is an encoded
    /// <c>StrategyMetrics</c>.
    /// </summary>
    Metrics = 0x88,

    /// <summary>
    /// Non-fatal diagnostic from the worker. Payload is a UTF-8 log line.
    /// Host should mirror into its journal / logger. Workers SHOULD NOT
    /// spam this — one line per meaningful event (startup, unload, etc).
    /// </summary>
    Diagnostic = 0x84,
}

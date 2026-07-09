# Out-of-process Roslyn sandbox — design doc

Status: **implemented and in production as of 2026-04-17** (commit `aa0fabf8`). This
document describes the finalized architecture; the live codebase now uses it for every
user-compiled Roslyn indicator and strategy. Per-platform launchers — Windows
AppContainer, macOS `sandbox-exec`, Android `isolatedProcess` bound service — all ship.
iOS remains intentionally deferred (desktop-only sandbox today).

---

## Why out-of-process

The current in-process Roslyn sandbox (`AccessibleTrader.Core/Services/RoslynScriptingService.cs`)
is substantially tighter than the original substring-blocklist it replaced — it runs a
`CSharpSyntaxWalker` over the bound semantic model and refuses to compile code that
references blocked namespaces, types, or members. But `AssemblyLoadContext` is **not a
security boundary**. A novel bypass — a runtime-reflection trick we missed, a compiler
bug, a Roslyn API surface we didn't know to block — would land the attacker's code in
the same OS process as:

- Every API key and OAuth token held by provider plugins
- The `ISecureStorageService` handle to the keychain / DPAPI / KeyStore
- Live WebSocket sessions authenticated as the user
- The BlazorWebView — which can read any cookie or local-storage value the app has

A real-money trading app that compiles arbitrary C# from `.atpkg` files shared on
Discord should run that code in a **separate OS process** with **OS-enforced isolation**
so even a full sandbox escape can't reach the trading host.

---

## High-level architecture

```
┌───────────────────────────────────────────┐           ┌───────────────────────────┐
│  Host process (MAUI BlazorClient)        │           │  Worker process           │
│  ─ trading providers, keychain, WebView  │  stdio /  │  ─ Roslyn compile         │
│  ─ IndicatorOrchestrator                  │◄─pipe───►│  ─ user script executes   │
│  ─ ScriptingService (proxy)              │  frames   │  ─ receives Ohlcv[] in    │
│  ─ launches + supervises worker(s)       │           │  ─ emits double[][] out   │
└───────────────────────────────────────────┘           └───────────────────────────┘
                                                                ▲
                                                                │  OS-enforced sandbox:
                                                                │  - Windows AppContainer
                                                                │  - macOS sandbox-exec
                                                                │  - Android isolatedProcess
                                                                │  - Linux seccomp-bpf
                                                                ▼
                                                        ┌───────────────────────────┐
                                                        │  Kernel denies:           │
                                                        │  - file system            │
                                                        │  - network                │
                                                        │  - loopback except pipe   │
                                                        │  - child process spawn    │
                                                        │  - shared memory          │
                                                        └───────────────────────────┘
```

**Host responsibilities**
- Spawn one worker process per compiled user script (or pool N warm workers).
- Forward OHLCV windows + parameters on the stdin pipe; read `double[][]` result on stdout.
- Enforce per-call timeout (e.g. 5s for `Calculate`, 500ms for incremental updates).
- Kill + respawn worker on crash, timeout, or quota breach.
- Monitor memory/CPU; terminate workers that exceed limits.

**Worker responsibilities**
- Take a compiled-assembly bytestream on startup.
- Load into a collectible `AssemblyLoadContext` (no trust gain — defense in depth only).
- Read frames from stdin, dispatch to `ICustomIndicator.Calculate` or `ITradingStrategy.Evaluate`.
- Write result frames to stdout.
- Never reach outside the pipe.

---

## IPC contract

Length-prefixed framed messages on stdio (binary, not JSON — for speed on tight indicator
loops).

### Frame layout

```
┌────────────┬──────────────┬─────────────────────┐
│ 4 B len    │ 1 B opcode   │ payload (len-1 B)   │
└────────────┴──────────────┴─────────────────────┘
```

### Opcodes (host → worker)

| opcode | name         | payload                                              |
|--------|--------------|------------------------------------------------------|
| `0x01` | LoadAssembly | assembly bytes                                       |
| `0x02` | Calculate    | u32 bar-count, f64×5×N OHLCV rows, u32 param-count, params |
| `0x03` | Evaluate     | strategy-specific (TBD)                              |
| `0xFF` | Shutdown     | (empty)                                              |

### Opcodes (worker → host)

| opcode | name         | payload                                              |
|--------|--------------|------------------------------------------------------|
| `0x81` | Ready        | (empty)                                              |
| `0x82` | Result       | u32 component-count, for each: u32 len, f64×len      |
| `0x83` | Error        | utf-8 error message                                  |
| `0x84` | Diagnostic   | utf-8 log line (verbose; host routes to the journal) |

### Timeouts

| operation      | default timeout |
|----------------|-----------------|
| LoadAssembly   | 2 s             |
| Calculate      | 5 s             |
| Evaluate       | 500 ms (hot path) |
| Shutdown ack   | 1 s before SIGKILL |

If the worker exceeds any of these, the host kills it (`Process.Kill(entireProcessTree: true)`)
and reports a timeout error to the user. No automatic retries — a script that hangs once is
suspect, and silent retries just defer the problem.

---

## Sandboxing per platform

### Missing-primitive policy (all platforms, 2026-07)

When the OS sandbox primitive a launcher needs is unavailable at launch time — `bwrap`
not installed on Linux, `sandbox-exec` masked on macOS, AppContainer creation failing on
Windows — the launcher **refuses to run the worker** and throws
`ScriptSandboxUnavailableException` with a user-readable message naming the missing
piece, the fix, and the override. It does NOT silently fall back to the unsandboxed
`DefaultProcessLauncher` (pre-2026-07 behaviour): an unsandboxed worker could read the
user's files (including API-key storage) and reach the network, and that downgrade must
never be invisible.

Explicit opt-out: `ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=1` restores the fallback
for users who accept the risk on a machine they trust. Every launch under the override
records a `SecurityEventKind.UnsandboxedScriptOverride` event. Central logic:
`SandboxPolicy` (Core/Services/Scripting), enforced by the Linux/macOS/Windows launchers.

### Windows — AppContainer

Launch the worker with `CreateProcess` + an `AppContainer` profile. Capabilities to grant:
- **None.** The stdin/stdout handles inherit through the standard handles mechanism and
  do not require any capability.

Capabilities to explicitly deny (or simply never grant):
- `internetClient`, `internetClientServer`, `privateNetworkClientServer`
- `documentsLibrary`, `removableStorage`, `picturesLibrary`
- `enterpriseAuthentication`

Use `InitializeProcThreadAttributeList` + `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`
to create the process. See `Windows.System.AppContainerProfile` for the managed API —
MAUI Windows apps can call it via WinRT.

Reference: https://learn.microsoft.com/windows/win32/secauthz/appcontainer-isolation

### macOS + Mac Catalyst — `sandbox-exec` / `NSXPCConnection`

Launch the worker via `NSTask` with a `.sb` sandbox profile. Minimal profile:

```scheme
(version 1)
(deny default)
(allow process-fork)
(allow file-read-data (literal "/dev/null"))
(allow file-write-data (literal "/dev/null"))
(allow signal (target self))
(allow mach-lookup (global-name "com.apple.system.logger"))
```

This denies network, file, IPC except self-signal and syslog. stdin/stdout still work
because they're inherited fds, not new opens.

For Mac Catalyst specifically, prefer an `NSExtension`-hosted worker — extensions run
in their own sandboxed process by default and communicate via `NSXPCConnection`. Trade-off:
extensions are a bigger lift to ship in the App Store.

Reference: Apple's Sandbox Design Guide, Archived.

### Android — `isolatedProcess`

Add a `<service>` to the manifest with `android:isolatedProcess="true"`:

```xml
<service
    android:name=".ScriptWorkerService"
    android:isolatedProcess="true"
    android:exported="false" />
```

An isolatedProcess service runs under its own UID, has no access to app data, and can
only communicate with the parent via AIDL or a `Messenger`. That's the IPC channel
instead of stdio — but the same frame/opcode contract still applies.

Reference: https://developer.android.com/reference/android/R.styleable#AndroidManifestService_isolatedProcess

### iOS — deferred

iOS doesn't expose a general sandbox launcher equivalent to the desktop ones. Options:
1. Ship `.atpkg` support as desktop-only.
2. Use `NSExtension` (app extensions) but these require App Store review gymnastics.
3. Run user scripts through a WebAssembly interpreter inside the main app instead —
   trading the Roslyn C# flow for a tighter-but-reduced WASM surface. Interesting
   but a separate design.

Recommendation: gate `.atpkg` import behind platform detection; on iOS, refuse imports
until one of the above ships.

### Linux — seccomp-bpf

Desktop Linux isn't an officially supported MAUI target but people build for it. Use
`prctl(PR_SET_SECCOMP, SECCOMP_MODE_FILTER, &filter)` with a whitelist of syscalls:
`read`, `write`, `brk`, `mmap` (anonymous only), `mprotect`, `munmap`, `rt_sigreturn`,
`exit`, `exit_group`, `sched_yield`, `futex` (for the CLR). Deny everything else including
`socket`, `connect`, `openat`, `clone`, `execve`.

---

## Resource quotas

Even with OS sandboxing, a user script can burn unbounded CPU/memory doing a legitimate-
looking infinite loop. Host enforces:

| resource | limit                      | enforcement                               |
|----------|----------------------------|-------------------------------------------|
| CPU      | 5 s wall-clock per call    | timeout timer in host, `Process.Kill`     |
| Memory   | 256 MB resident            | poll `Process.WorkingSet64`; kill if over |
| File     | none (sandbox denies)      | OS                                        |
| Network  | none (sandbox denies)      | OS                                        |
| Children | none (sandbox denies fork) | OS                                        |

On a resource kill, the host emits a JournalEntry explaining which user script hit which
limit, so the user can iterate.

---

## Threat model delta

What the out-of-process sandbox stops that the in-process one does not:

1. **Roslyn / CLR surface escape.** If a user script finds a way to call
   `Type.GetType("System.IO.File")` through some path we didn't anticipate, it still
   runs in the worker — which has no filesystem.
2. **Credential exfiltration.** Keychain / SecureStorage lives in the host process. The
   worker has no handle to it.
3. **Network exfiltration.** Sandbox denies `socket`/`connect`. A script can't phone home.
4. **Trading host compromise.** Even if the worker fully escapes its managed CLR sandbox,
   the OS sandbox stops it from touching the trading host.

What it does **not** stop:

1. **Malicious signals.** A hostile strategy that returns "BUY at market" signals on bad
   data is still free to do so. That's a logic bug for the user to catch in review, not a
   sandbox issue.
2. **Supply-chain compromise of the worker binary itself.** If the attacker replaces
   the worker executable via a plugin DLL drop in `Plugins/`, they execute code in the
   worker by design. Plugin-trust policy (phase 2) is the mitigation.
3. **Side-channel leaks.** Timing / power / cache side channels in shared OS resources
   aren't covered. Not relevant to this threat model.

---

## Incremental rollout

1. **Worker skeleton** (1 week): new `AccessibleTrader.ScriptWorker` project, stdio
   framing, `LoadAssembly` → `Calculate` roundtrip, no sandbox yet. Proves the IPC path.
2. **Windows AppContainer** (1 week): add the AppContainer launch code, test on
   Windows 11, verify network/filesystem are denied.
3. **macOS sandbox profile** (3 days): add the `.sb` file + launcher.
4. **Android isolatedProcess** (1 week): switch the IPC from stdio to AIDL, add the
   service manifest entry.
5. **Rewire `RoslynScriptingService`** (3 days): `CompileIndicatorAsync` returns a
   handle that dispatches through the worker instead of calling `Activator.CreateInstance`
   in-process. Keep the in-process path behind an opt-in dev flag for debugging.
6. **Resource monitoring + kill-on-timeout** (2 days): wall-clock watchdog,
   `WorkingSet64` poller.
7. **iOS policy**: refuse `.atpkg` imports until further notice.

Total: ~5 weeks of focused engineering. Coordinate with phase 4+ roadmap.

---

## Open questions

- Do we keep a worker pool (faster first-call), or spawn fresh per compile (simpler,
  smaller cold-path delta)?
- Strategy hot-path latency: 500 ms Calculate budget is generous for indicators but
  possibly too slow for tick-level strategies. Revisit after benchmarking.
- How do we version the IPC contract? Worker and host will ship as a matched pair, but
  a user who upgrades only the app (and keeps a cached compiled script) needs a clean
  "recompile required" path.
- Do we ship the worker as a separate signed executable, or as a mode flag on the main
  app? Separate binary is cleaner for trust manifest purposes.

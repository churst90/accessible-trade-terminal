using System;
using System.Diagnostics;
using System.IO;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Adapter that wraps a <see cref="System.Diagnostics.Process"/> in the
/// launcher-agnostic <see cref="IScriptWorkerProcess"/> contract. Used
/// by <see cref="DefaultProcessLauncher"/> and
/// <see cref="MacSandboxExecLauncher"/> — any launcher whose output is
/// a standard <see cref="Process.Start"/>-produced process.
///
/// <para>
/// All properties delegate straight through. Disposal disposes the
/// underlying <see cref="Process"/>, which closes the redirected
/// stdio streams the worker is reading / writing on.
/// </para>
/// </summary>
public sealed class DotNetProcessAdapter : IScriptWorkerProcess
{
    private readonly Process _proc;
    private bool _disposed;

    public DotNetProcessAdapter(Process proc)
    {
        _proc = proc ?? throw new ArgumentNullException(nameof(proc));
    }

    public Stream       StdinWrite   => _proc.StandardInput.BaseStream;
    public Stream       StdoutRead   => _proc.StandardOutput.BaseStream;
    public StreamReader StderrReader => _proc.StandardError;

    public bool HasExited => _proc.HasExited;
    public int  ExitCode  => _proc.ExitCode;

    public bool Kill(bool entireProcessTree)
    {
        try
        {
            if (_proc.HasExited) return true;
            _proc.Kill(entireProcessTree);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool WaitForExit(int milliseconds) => _proc.WaitForExit(milliseconds);

    public void Refresh() => _proc.Refresh();

    public long WorkingSet64 => _proc.WorkingSet64;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _proc.Dispose(); } catch { /* best-effort */ }
    }
}

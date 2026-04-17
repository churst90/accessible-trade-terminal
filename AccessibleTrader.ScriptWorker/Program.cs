using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.ScriptSandbox;

namespace AccessibleTrader.ScriptWorker;

/// <summary>
/// Desktop script-worker entry point. Opens raw stdio, hands the pair
/// to a <see cref="WorkerDispatcher"/> (which is the shared dispatch
/// loop used by both this desktop console host and the Android
/// isolated-process service), waits for the dispatcher to finish.
///
/// This binary is only used on Windows / macOS / Linux desktops — on
/// Android the worker runs inside a bound <c>Service</c> class instead.
/// See <see cref="WorkerDispatcher"/> for the shared protocol-handling
/// code.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Force binary mode on stdio — we are sending raw bytes, not text.
        var stdin  = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();

        var dispatcher = new WorkerDispatcher(stdin, stdout);
        try
        {
            await dispatcher.RunAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch (EndOfStreamException)
        {
            // Host closed stdin without a Shutdown frame — clean exit.
            return 0;
        }
        catch (Exception ex)
        {
            // Last-resort: emit an Error frame if we still can, then exit
            // non-zero so the supervisor knows something went wrong.
            try
            {
                await FrameCodec.WriteFrameAsync(stdout, Opcode.Error,
                    Encoding.UTF8.GetBytes("worker fatal: " + ex)).ConfigureAwait(false);
            }
            catch { /* host pipe is already dead */ }
            Console.Error.WriteLine("[worker] fatal: " + ex);
            return 1;
        }
    }
}

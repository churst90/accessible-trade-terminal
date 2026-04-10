using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Provides a safe wrapper for fire-and-forget background tasks.
    /// Ensures unobserved exceptions are logged instead of crashing the process.
    /// </summary>
    public static class SafeFireAndForget
    {
        /// <summary>
        /// Runs <paramref name="work"/> on the thread pool and logs any unhandled exception.
        /// Use this instead of bare <c>_ = Task.Run(...)</c> to prevent silent crashes.
        /// </summary>
        public static void Run(Func<Task> work, ILogger logger, string operationName)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await work().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown — not an error.
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background task '{OperationName}' failed.", operationName);
                }
            });
        }
    }
}

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The two "medium-value pending" WebHost test groups from the L-series
    /// close-out: the XDG path mapping (SQLite cache, security log, and the
    /// workspace library all assume these directories exist) and the app-logger
    /// dedup window (a reconnect storm must not flood the error surface).
    /// </summary>
    public class WebHostPathAndLoggerTests
    {
        // ── WebHostPathService ───────────────────────────────────────────────

        [Fact]
        public void Default_directories_end_with_the_app_folder_and_exist()
        {
            var svc = new WebHostPathService();

            Assert.EndsWith("AccessibleTrader", svc.AppDataDirectory.TrimEnd(Path.DirectorySeparatorChar));
            Assert.EndsWith("AccessibleTrader", svc.CacheDirectory.TrimEnd(Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(svc.AppDataDirectory));
            Assert.True(Directory.Exists(svc.CacheDirectory));
        }

        [Fact]
        public void Explicit_app_data_root_is_created_on_construction()
        {
            // The hosted (--accounts) build pins app-data to its own root; the
            // directory must exist the moment the service is constructed so the
            // secret store can open without a race.
            string root = TestTemp.NewPath("at-test-");
            try
            {
                var svc = new WebHostPathService(root);
                Assert.Equal(root, svc.AppDataDirectory);
                Assert.True(Directory.Exists(root));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
            }
        }

        /// <summary>
        /// The cache folder is absolute on every head that has one. GetFolderPath(InternetCache)
        /// is empty on Linux whatever exists, so the cache used to be the RELATIVE path
        /// "AccessibleTrader", made in the server's working directory: inside the checkout on
        /// Cody's machine, inside the program folder once the zip's starter runs the server from
        /// app/, and an UnauthorizedAccessException at startup when started from "/".
        /// </summary>
        [Fact]
        public void The_cache_directory_is_absolute_and_not_in_the_working_directory()
        {
            string cwd = Path.GetFullPath(Directory.GetCurrentDirectory());
            foreach (var dir in new[]
            {
                PlatformPaths.LocalCacheRoot(),
                new WebHostPathService().CacheDirectory,
                new WebHostPathService(TestTemp.NewPath("at-test-")).CacheDirectory,
                new UserScopedPathService(NSubstitute.Substitute.For<AccessibleTrader.WebHost.Account.ICurrentUser>(), accountsEnabled: false, dataRoot: null).CacheDirectory,
            })
            {
                Assert.True(Path.IsPathRooted(dir), $"'{dir}' is relative, so it lands in the working directory");
                Assert.False(Path.GetFullPath(dir).StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                    $"'{dir}' is inside the working directory {cwd}");
            }
        }

        [Fact]
        public void Blank_explicit_root_is_rejected()
        {
            Assert.Throws<ArgumentException>(() => new WebHostPathService("  "));
        }

        // ── WebHostAppLogger dedup ───────────────────────────────────────────

        private static (WebHostAppLogger logger, List<AppErrorEvent> events) BuildLogger()
        {
            var bus = new EventBus();
            var events = new List<AppErrorEvent>();
            bus.Subscribe<AppErrorEvent>(events.Add);
            return (new WebHostAppLogger(NullLogger<WebHostAppLogger>.Instance, bus), events);
        }

        [Fact]
        public void Same_source_and_message_inside_the_window_is_flagged_duplicate()
        {
            var (logger, events) = BuildLogger();

            logger.Log(ErrorSeverity.High, ErrorCategory.Systemic, "socket dropped", "Binance");
            logger.Log(ErrorSeverity.High, ErrorCategory.Systemic, "socket dropped", "Binance");

            Assert.Equal(2, events.Count);
            Assert.False(events[0].IsDuplicate);
            Assert.True(events[1].IsDuplicate);
        }

        [Fact]
        public void Different_source_or_message_is_not_deduped()
        {
            var (logger, events) = BuildLogger();

            logger.Log(ErrorSeverity.High, ErrorCategory.Systemic, "socket dropped", "Binance");
            logger.Log(ErrorSeverity.High, ErrorCategory.Systemic, "socket dropped", "Kraken");
            logger.Log(ErrorSeverity.High, ErrorCategory.Systemic, "auth failed", "Binance");

            Assert.All(events, e => Assert.False(e.IsDuplicate));
        }

        [Fact]
        public void Low_severity_never_reaches_the_error_surface()
        {
            var (logger, events) = BuildLogger();

            logger.Log(ErrorSeverity.Low, ErrorCategory.Informational, "tick", "Feed");
            logger.LogDebug("debug detail", "Feed");
            logger.LogInfo("info detail", "Feed");

            Assert.Empty(events);
        }
    }
}

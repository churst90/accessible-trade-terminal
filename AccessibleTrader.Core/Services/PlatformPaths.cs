namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Resolves the OS local-app-data root, with the one guarantee
    /// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> does not give you:
    /// the result is always an ABSOLUTE path.
    ///
    /// <para>
    /// On Unix, <c>GetFolderPath(LocalApplicationData)</c> returns an <b>empty string</b> when the
    /// directory it resolves to does not exist yet — including when <c>XDG_DATA_HOME</c> names a
    /// directory nobody created. Callers then do
    /// <c>Path.Combine(GetFolderPath(...), "AccessibleTrader", "Workspaces")</c> and get the
    /// RELATIVE path <c>AccessibleTrader/Workspaces</c>, which resolves against the process's
    /// current directory.
    /// </para>
    ///
    /// <para>
    /// That is not theoretical. On the hosted server (2026-08-20) it silently wrote every saved
    /// workspace, indicator preference and security-event log into
    /// <c>/opt/accessible-trader-terminal/</c> — the deployment directory, which a redeploy
    /// replaces — while <c>XDG_DATA_HOME</c> pointed at an empty state root that looked correct in
    /// the unit file. Nothing failed; the data just wasn't where anyone thought it was.
    /// </para>
    /// </summary>
    public static class PlatformPaths
    {
        /// <summary>
        /// The local-app-data root, guaranteed absolute and existing. Falls back through the XDG
        /// default and then the temp directory, so there is no input for which this returns a
        /// relative path.
        /// </summary>
        public static string LocalAppDataRoot()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            {
                // GetFolderPath declined (the target does not exist yet). Rebuild the XDG default
                // by hand rather than accepting a relative path.
                var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg))
                {
                    path = xdg;
                }
                else
                {
                    var home = Environment.GetEnvironmentVariable("HOME");
                    path = !string.IsNullOrWhiteSpace(home) && Path.IsPathRooted(home)
                        ? Path.Combine(home, ".local", "share")
                        : Path.Combine(Path.GetTempPath(), "AccessibleTraderData");
                }
            }

            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// The OS cache root, guaranteed absolute. <c>GetFolderPath(InternetCache)</c> is the
        /// Windows and macOS answer (INetCache, ~/Library/Caches); on Linux .NET has no mapping for
        /// it and returns an empty string every time, so the WebHost's market-data cache used to
        /// land in whatever folder the server was started from, and started from <c>/</c> it could
        /// not start at all. Linux gets <c>XDG_CACHE_HOME</c>, then <c>~/.cache</c>; a root that
        /// cannot be created (a service user whose home does not exist) falls to the temp folder.
        /// </summary>
        public static string LocalCacheRoot()
        {
            var candidates = new List<string?>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.InternetCache),
                Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
            };
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home)) candidates.Add(Path.Combine(home, ".cache"));

            foreach (var path in candidates)
            {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) continue;
                try { Directory.CreateDirectory(path); return path; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }

            var temp = Path.Combine(Path.GetTempPath(), "AccessibleTraderCache");
            Directory.CreateDirectory(temp);
            return temp;
        }

        /// <summary>The local-app-data root with the application folder appended.</summary>
        public static string AppDataRoot(string appFolderName = "AccessibleTrader")
        {
            var dir = Path.Combine(LocalAppDataRoot(), appFolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}

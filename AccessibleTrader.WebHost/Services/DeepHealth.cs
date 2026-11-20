using System.Diagnostics;
using System.Net;
using System.Reflection;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <c>/healthz/deep</c>: what an operator who cannot watch a dashboard asks the server over ssh
    /// (code review, 2026-11-20). Plain text, one fact a line, so a screen reader reads it top to
    /// bottom: version, uptime, memory, the server clock against the venues', free disk under the
    /// data root, and when the leaderboard was last written.
    ///
    /// <para>Answers only a request made ON the server (loopback, and no forwarded-for header, which
    /// nginx always adds), so the public cannot read it; anyone else gets a plain 404, the same as a
    /// path that does not exist. <c>/healthz</c> stays the trivial readiness probe it was built to be;
    /// this one reads the disk and must not be the thing systemd waits on.</para>
    ///
    /// <para>Not here: per-venue connection state. Each signed-in user has their own venue
    /// connections, held inside their own session, and the hosted terminal is paper-only.</para>
    /// </summary>
    public static class DeepHealth
    {
        private static readonly DateTime Started = Process.GetCurrentProcess().StartTime.ToUniversalTime();

        public static bool IsLocalOperator(HttpContext ctx)
        {
            var ip = ctx.Connection.RemoteIpAddress;
            if (ip is null || !IPAddress.IsLoopback(ip)) return false;
            var h = ctx.Request.Headers;
            return !h.ContainsKey("X-Forwarded-For") && !h.ContainsKey("X-Original-For")
                && !h.ContainsKey("Forwarded");
        }

        public static string Report(ClockSkewMonitor? clock, string? dataRoot, string? leaderboardPath, DateTime nowUtc)
        {
            var lines = new List<string>();
            string version = typeof(DeepHealth).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            lines.Add($"version: {version}");
            lines.Add($"now: {nowUtc:yyyy-MM-dd HH:mm:ss} UTC");
            lines.Add($"up: {Span(nowUtc - Started)}");
            lines.Add($"memory: {Environment.WorkingSet / (1024 * 1024)} MB");

            if (clock?.Last is { } r)
            {
                string way = Math.Abs(r.SecondsAhead) < 1 ? "in step" : r.SecondsAhead > 0 ? "fast" : "slow";
                lines.Add($"clock: {Math.Abs(r.SecondsAhead):0.0} s {way} next to {r.Source}, read {Span(nowUtc - r.ObservedUtc)} ago"
                          + (clock.Warned ? "; WARNED, off by more than " + ClockSkewMonitor.WarnSeconds + " s" : ""));
            }
            else
            {
                lines.Add("clock: no venue answer read yet");
            }

            if (!string.IsNullOrEmpty(dataRoot))
            {
                try
                {
                    var drive = new DriveInfo(Path.GetFullPath(dataRoot));
                    double freeGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
                    double pct = 100d * drive.AvailableFreeSpace / Math.Max(1, drive.TotalSize);
                    lines.Add($"disk: {freeGb:0.0} GB free ({pct:0}%) under the data root" + (pct < 10 ? "; LOW" : ""));
                }
                catch (Exception ex) { lines.Add($"disk: could not read ({ex.GetType().Name})"); }
            }

            if (!string.IsNullOrEmpty(leaderboardPath))
            {
                var f = new FileInfo(leaderboardPath);
                if (!f.Exists) lines.Add("leaderboard: not written yet");
                else
                {
                    var age = nowUtc - f.LastWriteTimeUtc;
                    // The publisher rewrites every five minutes; three missed runs is a stall.
                    lines.Add($"leaderboard: written {Span(age)} ago" + (age > TimeSpan.FromMinutes(15) ? "; STALE" : ""));
                }
            }

            return string.Join("\n", lines) + "\n";
        }

        private static string Span(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalMinutes < 1) return $"{(int)t.TotalSeconds} s";
            if (t.TotalHours < 1) return $"{(int)t.TotalMinutes} min";
            if (t.TotalDays < 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
            return $"{(int)t.TotalDays} d {t.Hours} h";
        }
    }
}

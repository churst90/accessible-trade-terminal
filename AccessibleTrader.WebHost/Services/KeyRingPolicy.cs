namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Makes the DataProtection key-ring directory owner-only, and refuses to start if it
    /// cannot be.
    ///
    /// <para>
    /// <c>PersistKeysToFileSystem(dp-keys)</c> is called with no <c>ProtectKeysWith*</c>, so
    /// on Linux that directory holds <b>plaintext XML containing the master keys</b> for the
    /// auth cookie, the antiforgery token and every blob in
    /// <c>WebHostSecureStorageService</c> — including, now, the VAPID private key. Reading
    /// those files is equivalent to holding every session on the box.
    /// </para>
    ///
    /// <para>
    /// <c>SERVER_SETUP.md</c> mitigates this with <c>chmod -R 700</c> on the data root and
    /// that is the right operational answer — but it is documentation, not a control, and
    /// the matching <c>UMask=0077</c> systemd drop-in is still an open item, so a fresh
    /// deploy under the default 022 umask creates this directory world-readable and nothing
    /// says a word. The app owns the directory; the app can assert the property instead of
    /// asking an operator to remember it.
    /// </para>
    ///
    /// <para>
    /// Fail-closed on purpose. A key ring readable by other local accounts is not a
    /// degraded mode to serve traffic in — it is the whole authentication system in the
    /// clear — and the fix is one <c>chmod</c> the error message names.
    /// </para>
    /// </summary>
    public static class KeyRingPolicy
    {
        /// <summary>Bits that must NOT be set: anything granted to group or other.</summary>
        private const UnixFileMode GroupAndOther =
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        /// <summary>
        /// Creates <paramref name="keyRingDir"/> if needed, tightens it to 0700, and throws
        /// <see cref="InvalidOperationException"/> if it is still reachable by group or
        /// other afterwards. No-op on Windows, where the directory ACL governs and
        /// DataProtection encrypts the ring with DPAPI anyway.
        /// </summary>
        public static void EnsurePrivate(string keyRingDir)
        {
            Directory.CreateDirectory(keyRingDir);
            if (OperatingSystem.IsWindows()) return;

            try
            {
                var mode = File.GetUnixFileMode(keyRingDir);
                if ((mode & GroupAndOther) != 0)
                {
                    File.SetUnixFileMode(keyRingDir,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                throw new InvalidOperationException(
                    $"Could not read or set permissions on the DataProtection key ring at '{keyRingDir}'. " +
                    "That directory holds the master keys for the auth cookie, the antiforgery token and " +
                    "every encrypted secret on this instance, so it must be owner-only before the server " +
                    "starts. Fix it with: chmod 700 " + keyRingDir, ex);
            }

            var after = File.GetUnixFileMode(keyRingDir);
            if ((after & GroupAndOther) != 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to start: the DataProtection key ring at '{keyRingDir}' is readable or " +
                    $"writable beyond its owner (mode {after}). It holds the master keys for the auth " +
                    "cookie, the antiforgery token and every encrypted secret on this instance — the " +
                    "keys are stored unencrypted, so read access to that directory is read access to " +
                    "every session. Fix it with: chmod 700 " + keyRingDir);
            }
        }
    }
}

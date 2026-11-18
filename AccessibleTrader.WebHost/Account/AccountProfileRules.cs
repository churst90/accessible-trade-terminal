namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// The display-name rule in one place: 3–30 characters after trimming, letters, digits,
    /// spaces, dots, dashes and underscores only, and never an '@'. An empty value is not an
    /// error — it clears the name.
    /// </summary>
    public static class DisplayNameRules
    {
        public const int MinimumLength = 3;
        public const int MaximumLength = 30;

        public static bool TryNormalize(
            string? value, out string? normalized, out string? error)
        {
            var trimmed = (value ?? "").Trim();
            normalized = null;
            error = null;

            if (trimmed.Length == 0) return true;   // empty clears it

            if (trimmed.Contains('@'))
            {
                error = "A display name cannot contain @, because it must never look like an email address.";
                return false;
            }

            if (trimmed.Length < MinimumLength)
            {
                error = $"A display name needs at least {MinimumLength} characters.";
                return false;
            }

            if (trimmed.Length > MaximumLength)
            {
                error = $"A display name can have at most {MaximumLength} characters.";
                return false;
            }

            foreach (char c in trimmed)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == '-' || c == '_')
                    continue;

                error = "A display name can use letters, digits, spaces, dots, dashes and underscores only.";
                return false;
            }

            normalized = trimmed;
            return true;
        }
    }

    /// <summary>
    /// The last gate before account deletion removes a directory. Deleting outside
    /// <c>{dataRoot}/users/</c>, or deleting the shared <c>users/anon</c> folder, would
    /// destroy somebody else's data (or the anonymous scratch space), so both are refused
    /// here rather than trusted to the caller.
    /// </summary>
    public static class AccountDataFolderGuard
    {
        public static bool IsSafeUserFolder(string dataRoot, string folder, out string fullPath)
        {
            fullPath = Path.GetFullPath(folder);
            var usersRoot = Path.GetFullPath(Path.Combine(dataRoot, "users"));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(fullPath, usersRoot, comparison)) return false;

            var prefix = usersRoot.EndsWith(Path.DirectorySeparatorChar)
                ? usersRoot
                : usersRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, comparison)) return false;

            var leaf = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
            return !string.Equals(leaf, "anon", comparison);
        }
    }
}

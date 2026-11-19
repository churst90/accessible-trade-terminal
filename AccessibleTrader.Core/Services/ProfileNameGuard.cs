namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Reject profile names that could escape a library directory. Applied to every
    /// save/load/delete path in <see cref="WorkspaceLibraryService"/> and
    /// <see cref="IndicatorTemplateService"/> so a name like "../../something" or
    /// "C:\\Windows\\..." cannot touch files outside the library folder.
    /// </summary>
    public static class ProfileNameGuard
    {
        public static string? Sanitize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var trimmed = name.Trim();
            if (trimmed.Contains("..", StringComparison.Ordinal)) return null;
            if (Path.IsPathRooted(trimmed)) return null;
            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            // Strip any directory component; only the filename portion can name a profile.
            var baseName = Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(baseName)) return null;
            if (baseName.Equals("alerts", StringComparison.OrdinalIgnoreCase)) return null;
            return baseName;
        }
    }
}

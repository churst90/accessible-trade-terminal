namespace AccessibleTrader.Core.Models
{
    public record ShortcutDefinition(
        SystemCommand Command,
        string Key,
        bool Shift = false,
        bool Ctrl = false,
        bool Alt = false,
        string Scope = "GLOBAL"
    );

    public class ShortcutProfile
    {
        public string Name { get; set; } = "Default";
        public List<ShortcutDefinition> Shortcuts { get; set; } = new();
    }
}

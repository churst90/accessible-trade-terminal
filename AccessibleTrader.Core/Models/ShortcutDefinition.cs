using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AccessibleTrader.Core.Models
{
    public record ShortcutDefinition(
        // Written as a NAME, not an ordinal.
        //
        // Newtonsoft serialises an enum as its integer by default, and SystemCommand is
        // implicitly numbered — so deleting a member from the middle of it silently renumbers
        // every command after it, and a saved shortcuts.json then binds keys to whatever
        // command inherited each number. Retiring three split-view verbs and two pane-scroll
        // verbs is exactly that edit. A name cannot drift when the list around it changes, and
        // an unknown one throws on load, where the existing catch falls back to the defaults
        // rather than quietly binding the wrong thing.
        [property: JsonConverter(typeof(StringEnumConverter))]
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

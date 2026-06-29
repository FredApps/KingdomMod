// ModsApi — registry for mod-published runtime controls.
//
// Two flavors:
//   * Toggle  — Kingdom.Mods.RegisterToggle("My Mod", () => MyMod.Enabled, v => MyMod.Enabled = v).
//               Rendered as a checkbox in the F1 console.
//   * Choice  — Kingdom.Mods.RegisterChoice("Builders", new[] { "Lame", "Brave" },
//                                            () => MyMod.IsBrave ? 1 : 0,
//                                            v  => MyMod.IsBrave = (v == 1));
//               Rendered as a horizontal radio row.
//
// The mod owns the actual state — the registry only stores get/set callbacks
// and display labels. Both registrations are idempotent: re-registering the
// same label replaces the prior entry, which keeps things sane across
// MelonLoader hot reloads.

using System;
using System.Collections.Generic;

namespace KingdomMod
{
    /// <summary>
    /// Lightweight registry that mods use to surface on/off toggles in the
    /// KingdomMod F1 console without the loader having to know about them.
    /// </summary>
    public sealed class ModsApi
    {
        internal static ModsApi Instance { get; } = new ModsApi();
        private ModsApi() { }

        private readonly List<ModToggle> _toggles = new();
        private readonly List<ModChoice> _choices = new();

        /// <summary>
        /// Register a boolean toggle the F1 console will display. The label
        /// is the user-visible name (e.g. "Any Trees"). The get/set pair is
        /// the mod's own flag accessor — the registry keeps no state of its
        /// own. Idempotent: re-registering the same label replaces the
        /// previous entry, which keeps things sane across MelonLoader hot
        /// reloads.
        /// </summary>
        public void RegisterToggle(string label, Func<bool> get, Action<bool> set, string tooltip = null)
        {
            if (string.IsNullOrEmpty(label) || get == null || set == null) return;
            for (int i = 0; i < _toggles.Count; i++)
            {
                if (_toggles[i].Label == label)
                {
                    _toggles[i] = new ModToggle(label, get, set, tooltip);
                    return;
                }
            }
            _toggles.Add(new ModToggle(label, get, set, tooltip));
        }

        /// <summary>Read-only view of currently registered toggles.</summary>
        public IReadOnlyList<ModToggle> Toggles => _toggles;

        /// <summary>
        /// Register a multi-option radio control. <paramref name="options"/> is the
        /// list of user-visible labels; the get/set pair works in indices into that
        /// array. Idempotent on <paramref name="label"/>.
        /// </summary>
        public void RegisterChoice(string label, string[] options, Func<int> get, Action<int> set, string tooltip = null)
        {
            if (string.IsNullOrEmpty(label) || options == null || options.Length < 2 || get == null || set == null) return;
            for (int i = 0; i < _choices.Count; i++)
            {
                if (_choices[i].Label == label)
                {
                    _choices[i] = new ModChoice(label, options, get, set, tooltip);
                    return;
                }
            }
            _choices.Add(new ModChoice(label, options, get, set, tooltip));
        }

        /// <summary>Read-only view of currently registered choices.</summary>
        public IReadOnlyList<ModChoice> Choices => _choices;

        private readonly List<ModHotkey> _hotkeys = new();

        /// <summary>
        /// Register a hotkey for the F1 console's "Shortcuts" guide. <paramref name="key"/>
        /// is the user-visible key label (e.g. "F2", "F5 / F6 / F7"); <paramref name="description"/>
        /// is what it does. This is documentation only - the mod still owns the actual
        /// input handling. Idempotent on <paramref name="key"/>, so hot reloads don't
        /// duplicate rows. Registration order is preserved for display.
        /// </summary>
        public void RegisterHotkey(string key, string description)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(description)) return;
            for (int i = 0; i < _hotkeys.Count; i++)
            {
                if (_hotkeys[i].Key == key)
                {
                    _hotkeys[i] = new ModHotkey(key, description);
                    return;
                }
            }
            _hotkeys.Add(new ModHotkey(key, description));
        }

        /// <summary>Read-only view of registered hotkeys, for the F1 console's guide.</summary>
        public IReadOnlyList<ModHotkey> Hotkeys => _hotkeys;
    }

    /// <summary>A single hotkey entry for the F1 console's shortcuts guide.</summary>
    public readonly struct ModHotkey
    {
        /// <summary>User-visible key label (e.g. "F2", "F5 / F6 / F7 / F8").</summary>
        public string Key { get; }
        /// <summary>What the key does.</summary>
        public string Description { get; }
        /// <summary>Construct an entry. Prefer <see cref="ModsApi.RegisterHotkey"/>.</summary>
        public ModHotkey(string key, string description)
        {
            Key = key; Description = description;
        }
    }

    /// <summary>A single mod-published toggle entry.</summary>
    public readonly struct ModToggle
    {
        /// <summary>User-visible label (e.g. "Any Trees").</summary>
        public string Label { get; }
        /// <summary>Current value getter — invoked each frame the F1 console draws.</summary>
        public Func<bool> Get { get; }
        /// <summary>Setter invoked when the user clicks the toggle in the F1 console.</summary>
        public Action<bool> Set { get; }
        /// <summary>Optional hover tooltip describing what the toggle does (may be null).</summary>
        public string Tooltip { get; }
        /// <summary>Construct an entry. Prefer <see cref="ModsApi.RegisterToggle"/> over calling this directly.</summary>
        public ModToggle(string label, Func<bool> get, Action<bool> set, string tooltip = null)
        {
            Label = label; Get = get; Set = set; Tooltip = tooltip;
        }
    }

    /// <summary>A single mod-published radio-style choice (e.g. "Builders: Lame | Brave").</summary>
    public readonly struct ModChoice
    {
        /// <summary>User-visible label (e.g. "Builders").</summary>
        public string Label { get; }
        /// <summary>User-visible option labels, indexed parallel to Get/Set values.</summary>
        public string[] Options { get; }
        /// <summary>Returns the current option index. Invoked each frame the F1 console draws.</summary>
        public Func<int> Get { get; }
        /// <summary>Invoked when the user selects a different option. Receives the new index.</summary>
        public Action<int> Set { get; }
        /// <summary>Optional hover tooltip describing what the choice does (may be null).</summary>
        public string Tooltip { get; }
        /// <summary>Construct an entry. Prefer <see cref="ModsApi.RegisterChoice"/> over calling this directly.</summary>
        public ModChoice(string label, string[] options, Func<int> get, Action<int> set, string tooltip = null)
        {
            Label = label; Options = options; Get = get; Set = set; Tooltip = tooltip;
        }
    }
}

// CustomMountsApi - registry for mod-created mounts surfaced in the F1 console.

using System;
using System.Collections.Generic;
using Il2Cpp;

namespace KingdomMod
{
    /// <summary>
    /// Registry for custom mounts. Mods provide a factory that creates a live,
    /// configured <see cref="Steed"/> instance for the chosen player; the loader
    /// then rides it through the game's own <c>Player.Ride</c> path.
    /// </summary>
    public sealed class CustomMountsApi
    {
        internal static CustomMountsApi Instance { get; } = new CustomMountsApi();
        private CustomMountsApi() { }

        private readonly List<CustomMountDefinition> _mounts = new();

        /// <summary>
        /// Register or replace a custom mount definition. The id is stable and
        /// unique, while label and tooltip are user-facing F1 text.
        /// </summary>
        public void Register(string id, string label, string tooltip, Func<Player, Action<string>, Steed> factory, string baseMount = null)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label) || factory == null) return;
            var definition = new CustomMountDefinition(id, label, tooltip, factory, baseMount);
            for (int i = 0; i < _mounts.Count; i++)
            {
                if (string.Equals(_mounts[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    _mounts[i] = definition;
                    return;
                }
            }
            _mounts.Add(definition);
        }

        /// <summary>Read-only view of registered custom mounts.</summary>
        public IReadOnlyList<CustomMountDefinition> Mounts => _mounts;
    }

    /// <summary>A custom mount entry published by a mod.</summary>
    public readonly struct CustomMountDefinition
    {
        /// <summary>Stable id, used for replacement/idempotency.</summary>
        public string Id { get; }
        /// <summary>User-visible button label.</summary>
        public string Label { get; }
        /// <summary>User-visible hover tooltip.</summary>
        public string Tooltip { get; }
        /// <summary>Human-readable base mount preference, for logging and docs.</summary>
        public string BaseMount { get; }
        /// <summary>Factory that creates a live scene steed for the chosen player.</summary>
        public Func<Player, Action<string>, Steed> Factory { get; }

        public CustomMountDefinition(string id, string label, string tooltip, Func<Player, Action<string>, Steed> factory, string baseMount = null)
        {
            Id = id;
            Label = label;
            Tooltip = tooltip;
            Factory = factory;
            BaseMount = baseMount;
        }
    }
}

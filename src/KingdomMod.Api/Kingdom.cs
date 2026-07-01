// KingdomMod.Api — main SDK entry point.
//
// Mods write `Kingdom.Time.OnDayChanged += ...` and similar.  This file glues the
// SDK facades to the live game singletons.  Every accessor is null-safe: if the
// scene is in a state where the singleton does not exist yet, the call is a no-op
// and reads return reasonable defaults.

using System;
using KingdomMod.Internal;
using UnityEngine;

namespace KingdomMod
{
    /// <summary>
    /// Root API surface for KingdomMod.  Static — there is exactly one running game.
    /// </summary>
    public static class Kingdom
    {
        /// <summary>Lazily-resolved game singletons.</summary>
        public static GameState Game => GameState.Instance;

        /// <summary>Currency, wallets and taxes.</summary>
        public static EconomyApi Economy => EconomyApi.Instance;

        /// <summary>Day/night, seasons, pacing.</summary>
        public static TimeApi Time => TimeApi.Instance;

        /// <summary>Greed waves, blood moons, portal pressure.</summary>
        public static EnemyApi Enemies => EnemyApi.Instance;

        /// <summary>Player(s) — monarch, mount, position, wallet.</summary>
        public static PlayersApi Players => PlayersApi.Instance;

        /// <summary>Asset and pack loader (textures, sprites, audio, JSON).</summary>
        public static PackApi Packs => PackApi.Instance;

        /// <summary>Registry of mod-published runtime toggles surfaced in the F1 console.</summary>
        public static ModsApi Mods => ModsApi.Instance;

        /// <summary>Registry of custom mounts surfaced in the F1 console.</summary>
        public static CustomMountsApi CustomMounts => CustomMountsApi.Instance;

        /// <summary>True if the core <c>Managers</c> singleton is initialised.</summary>
        public static bool IsReady => GameRefs.HasManagers;
    }
}

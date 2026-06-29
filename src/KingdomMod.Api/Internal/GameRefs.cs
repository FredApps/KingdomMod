// Internal helpers — resolve game singletons safely and cache references.
//
// The IL2CPP interop assemblies expose the game's own types directly (Il2CppInterop
// preserves the original namespaces).  All game type references in this SDK go
// through this single file so a hypothetical IL2CPP renaming would be a one-spot
// fix rather than a sweep across the SDK.

using System;
using UnityEngine;

namespace KingdomMod.Internal
{
    /// <summary>Lazy, null-safe resolution of every game singleton we touch.</summary>
    public static class GameRefs
    {
        // Cached so we don't pay a static-field load on every accessor call.
        private static Il2Cpp.Managers _managers;

        /// <summary>The live <c>Managers</c> singleton, or null while the scene isn't ready.</summary>
        public static Il2Cpp.Managers Managers
        {
            get
            {
                if (_managers != null) return _managers;
                if (!Il2Cpp.Managers.InstExists) return null;
                _managers = Il2Cpp.Managers.Inst;
                return _managers;
            }
        }

        /// <summary>True if <c>Managers.Inst</c> exists.  Cheap; safe to call every frame.</summary>
        public static bool HasManagers => Il2Cpp.Managers.InstExists;

        /// <summary>The macro-state of the realm (<c>Managers.kingdom</c>).</summary>
        public static Il2Cpp.Kingdom Kingdom         => Managers?.kingdom;
        /// <summary>Run lifecycle, lose/sail callbacks, current land (<c>Managers.game</c>).</summary>
        public static Il2Cpp.Game Game               => Managers?.game;
        /// <summary>Day/night/season clock (<c>Managers.director</c>).</summary>
        public static Il2Cpp.Director Director       => Managers?.director;
        /// <summary>Greed/Cliff portal pressure (<c>Managers.enemies</c>).</summary>
        public static Il2Cpp.EnemyManager Enemies    => Managers?.enemies;
        /// <summary>All payable objects in the scene (<c>Managers.payables</c>).</summary>
        public static Il2Cpp.PayableManager Payables => Managers?.payables;
        /// <summary>Currency type registry (<c>Managers.currency</c>).</summary>
        public static Il2Cpp.CurrencyManager Currency=> Managers?.currency;
        /// <summary>Game's prefab registry (<c>Managers.prefabs</c>).</summary>
        public static Il2Cpp.PrefabManager Prefabs   => Managers?.prefabs;
        /// <summary>Run-stat tracker (<c>Managers.stats</c>).</summary>
        public static Il2Cpp.Stats Stats             => Managers?.stats;
        /// <summary>Active world bookkeeping (<c>Managers.world</c>).</summary>
        public static Il2Cpp.World World             => Managers?.world;

        /// <summary>Reset cached references when the scene/game restarts.
        /// Also clears per-API caches (wallet, etc.) so they re-resolve against the new scene.</summary>
        public static void Invalidate()
        {
            _managers = null;
            EconomyApi.Instance.InvalidateWalletCache();
        }
    }
}

// PlayersApi — local + remote monarchs.

using System.Collections.Generic;
using KingdomMod.Internal;
using UnityEngine;

namespace KingdomMod
{
    /// <summary>Player(s) — find them, query state, set built-in cheats.</summary>
    public sealed class PlayersApi
    {
        internal static PlayersApi Instance { get; } = new PlayersApi();
        private PlayersApi() { }

        /// <summary>Every Player currently in the scene (1 in single-player, 2 in co-op).</summary>
        public IEnumerable<Il2Cpp.Player> All
        {
            get
            {
                foreach (var p in Object.FindObjectsByType<Il2Cpp.Player>(FindObjectsSortMode.None))
                    yield return p;
            }
        }

        /// <summary>The first/local Player, or null if none spawned yet.</summary>
        public Il2Cpp.Player Local
        {
            get { foreach (var p in All) return p; return null; }
        }

        /// <summary>Toggle the game's built-in infinite-stamina debug flag.</summary>
        public bool InfiniteStamina
        {
            get => Il2Cpp.Player.DebugInfiniteStamina;
            set => Il2Cpp.Player.DebugInfiniteStamina = value;
        }
    }
}

// GameState — facade over the Il2Cpp.Game singleton (Managers.Inst.game).
//
// Provides clean events for the lifecycle and the most useful state queries.
// All events forward the game's own static Action delegates, so subscribing here
// is just a Harmony-free wrapper.

using System;
using KingdomMod.Internal;

namespace KingdomMod
{
    /// <summary>Game lifecycle, current land/campaign, and overall state queries.</summary>
    public sealed class GameState
    {
        internal static GameState Instance { get; } = new GameState();
        private GameState() { }

        // ---- Events (forwarded from Il2Cpp.Game's static Actions) ------------
        // These are exposed by the game itself; we just relay them.

        /// <summary>Fired when a new run/playthrough starts.</summary>
        public event Action OnGameStart
        {
            add    => Il2Cpp.Game.OnGameStart += value;
            remove => Il2Cpp.Game.OnGameStart -= value;
        }

        /// <summary>Fired when the current run ends (win or loss).</summary>
        public event Action OnGameEnd
        {
            add    => Il2Cpp.Game.OnGameEnd += value;
            remove => Il2Cpp.Game.OnGameEnd -= value;
        }

        /// <summary>Fired specifically when the player loses (monarch dies, crown lost).</summary>
        public event Action OnLose
        {
            add    => Il2Cpp.Game.OnLose += value;
            remove => Il2Cpp.Game.OnLose -= value;
        }

        /// <summary>Fired when the player triggers sail-away (escape to a new island).</summary>
        public event Action OnSailAway
        {
            add
            {
                var g = GameRefs.Game;
                if (g != null) g.OnSailAway += value;
            }
            remove
            {
                var g = GameRefs.Game;
                if (g != null) g.OnSailAway -= value;
            }
        }

        // ---- Queries -----------------------------------------------------------

        /// <summary>Current land index (1‑5 in vanilla campaigns).</summary>
        public int CurrentLand => GameRefs.Game?.currentLand ?? 0;

        /// <summary>True while the game is in a playable state (not menu/loading/credits).</summary>
        public bool InPlayableState => GameRefs.Game?.InPlayableState ?? false;

        /// <summary>True if the run has been lost.</summary>
        public bool HasLost => GameRefs.Game?.HasLost ?? false;

        /// <summary>Game version string (e.g. "2.4.0").</summary>
        public string Version => GameRefs.Game?.version ?? "unknown";

        /// <summary>True when local + remote co-op is currently joined.</summary>
        public bool IsCoopActive => Il2Cpp.Managers.IsP2Active;
    }
}

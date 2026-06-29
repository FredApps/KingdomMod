// EnemyApi — Greed/blood-moon wave queries.  Light surface for now; richer wave
// hooks (e.g. "OnWaveSpawn") are exposed by the mod loader via Harmony patches.

using KingdomMod.Internal;

namespace KingdomMod
{
    /// <summary>Greed, blood moons, and night-pressure queries.</summary>
    public sealed class EnemyApi
    {
        internal static EnemyApi Instance { get; } = new EnemyApi();
        private EnemyApi() { }

        /// <summary>True during the brief "blood moon" warning window that pauses regular spawn pressure.</summary>
        public bool IsBloodMoonTonight => GameRefs.Director?.IsRedMoonPauseTime ?? false;

        /// <summary>The raw EnemyManager — escape hatch when you need behaviour the SDK doesn't yet wrap.</summary>
        public Il2Cpp.EnemyManager Raw => GameRefs.Enemies;
    }
}

// Example: GameplayTweaks — Tier-2 Harmony patch.  Demonstrates the actual
// behaviour-modification path: prefix Director.set_secondsPerInGameHour to clamp
// it to a configurable minimum (lets the user *guarantee* slow days even if the
// game later resets the value).

using HarmonyLib;
using MelonLoader;
using KingdomMod;

[assembly: MelonInfo(typeof(KingdomMod.Examples.GameplayTweaks.GameplayMod), "Gameplay Tweaks", "0.1.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.GameplayTweaks
{
    public sealed class GameplayMod : MelonMod
    {
        internal static MelonPreferences_Entry<float> MinSecondsPerHour;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("KingdomMod.Gameplay", "Gameplay Tweaks");
            MinSecondsPerHour = cat.CreateEntry("MinSecondsPerInGameHour", 0f,
                "If > 0, clamp Director.secondsPerInGameHour to at least this value (slower days). 0 = leave alone.");

            HarmonyHelper.PatchAll(this);
            LoggerInstance.Msg("Gameplay Tweaks loaded.");
        }
    }

    /// <summary>
    /// Postfix on Director.OnLevelLoaded so we apply the clamp every time a
    /// playable scene comes up.  We don't patch the setter directly because in
    /// IL2CPP that requires hooking through Il2CppInterop; using a stable virtual
    /// like OnLevelLoaded is more update-resilient.
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.Director), nameof(Il2Cpp.Director.OnLevelLoaded))]
    internal static class DirectorOnLevelLoadedPatch
    {
        private static void Postfix(Il2Cpp.Director __instance)
        {
            var min = GameplayMod.MinSecondsPerHour.Value;
            if (min > 0f && __instance.secondsPerInGameHour < min)
                __instance.secondsPerInGameHour = min;
        }
    }
}

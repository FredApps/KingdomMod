// Example: SpeedTweaks — Tier-2 day-cycle speed controller.
//
// Two knobs:
//   * SpeedMultiplier (float)  - drives Director.ClockSpeedModifier (1.0 = vanilla
//     beginning speed). The recommended slider range is [MinSpeed, EngineMaxSpeed],
//     where EngineMaxSpeed is read from the game's own Director.rampupCurve at
//     load so the upper bound matches the fastest pace the game would naturally
//     reach in late-game.
//   * DisableProgressiveShortening (bool) - the game shortens the playable day
//     over time via Director.GetWaveRampupMultiplier (rampupCurve sampled by
//     day number) so waves arrive earlier and earlier. When true, we postfix
//     that method to always return 1f, locking every day to the beginning speed.

using HarmonyLib;
using MelonLoader;
using UnityEngine;
using KingdomMod;

[assembly: MelonInfo(typeof(KingdomMod.Examples.SpeedTweaks.SpeedMod), "Speed Tweaks", "0.1.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.SpeedTweaks
{
    public sealed class SpeedMod : MelonMod
    {
        internal static MelonPreferences_Entry<float> SpeedMultiplier;
        internal static MelonPreferences_Entry<float> MinSpeed;
        internal static MelonPreferences_Entry<bool>  DisableProgressiveShortening;

        // Slider upper bound, derived from the game's own data at level load.
        // Mods/UI can read this to size their slider; defaults to a safe 4.0
        // until we've seen a Director instance.
        internal static float EngineMaxSpeed = 4f;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("KingdomMod.Speed", "Speed Tweaks");
            SpeedMultiplier = cat.CreateEntry("SpeedMultiplier", 1.0f,
                "Multiplier on Director.ClockSpeedModifier. 1.0 = vanilla beginning speed. " +
                "Slider range: [MinSpeed, engine-derived max from rampupCurve].");
            MinSpeed = cat.CreateEntry("MinSpeed", 0.25f,
                "Lower bound of the speed slider. 0 freezes time; 0.25 = quarter speed.");
            DisableProgressiveShortening = cat.CreateEntry("DisableProgressiveShortening", false,
                "If true, lock every day to the beginning speed by forcing " +
                "Director.GetWaveRampupMultiplier to return 1.0 (disables the " +
                "rampupCurve that makes waves arrive earlier each day).");

            HarmonyHelper.PatchAll(this);
            LoggerInstance.Msg("Speed Tweaks loaded.");
        }

        internal static float ClampSpeed(float v)
        {
            var min = Mathf.Max(0f, MinSpeed.Value);
            var max = Mathf.Max(min, EngineMaxSpeed);
            return Mathf.Clamp(v, min, max);
        }
    }

    /// <summary>
    /// On every level load: sample the game's rampupCurve to derive the engine's
    /// natural max pace and use it as the slider upper bound, then apply the
    /// user's chosen multiplier to ClockSpeedModifier.
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.Director), nameof(Il2Cpp.Director.OnLevelLoaded))]
    internal static class DirectorOnLevelLoadedPatch
    {
        private static void Postfix(Il2Cpp.Director __instance)
        {
            // OnLevelLoaded runs in the middle of scene bring-up; some
            // referenced fields can NRE while the level is still wiring
            // itself. Snapshot defensively.
            try
            {
                var curve = __instance.rampupCurve;
                if (curve != null)
                {
                    // Curve max ~= the highest rampup multiplier the game would
                    // organically reach. Sample a late-game day window.
                    float max = 1f;
                    for (int day = 1; day <= 365; day++)
                    {
                        var v = curve.Evaluate(day);
                        if (v > max) max = v;
                    }
                    SpeedMod.EngineMaxSpeed = Mathf.Max(1f, max);
                }
            }
            catch { /* curve unavailable; keep last EngineMaxSpeed */ }

            try { __instance.ClockSpeedModifier = SpeedMod.ClampSpeed(SpeedMod.SpeedMultiplier.Value); }
            catch { /* setter may NRE before director finishes init */ }
        }
    }

    /// <summary>
    /// AdjustTime is the game's per-frame clock tick. Re-assert ClockSpeedModifier
    /// after the game runs so any internal reset is overwritten in the same frame.
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.Director), nameof(Il2Cpp.Director.AdjustTime))]
    internal static class DirectorAdjustTimePatch
    {
        private static void Postfix(Il2Cpp.Director __instance)
        {
            // AdjustTime runs every frame — keep this branch hot-path cheap
            // and never throw into the IL2CPP→managed transition.
            try
            {
                var wanted = SpeedMod.ClampSpeed(SpeedMod.SpeedMultiplier.Value);
                if (__instance.ClockSpeedModifier != wanted)
                    __instance.ClockSpeedModifier = wanted;
            }
            catch { }
        }
    }

    /// <summary>
    /// The "progressively shortening days" mechanic: rampupCurve sampled per day
    /// to bring wave arrivals earlier. Force 1.0 to disable the shortening.
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.Director), nameof(Il2Cpp.Director.GetWaveRampupMultiplier))]
    internal static class DirectorGetWaveRampupMultiplierPatch
    {
        private static void Postfix(ref float __result)
        {
            if (SpeedMod.DisableProgressiveShortening.Value)
                __result = 1f;
        }
    }
}

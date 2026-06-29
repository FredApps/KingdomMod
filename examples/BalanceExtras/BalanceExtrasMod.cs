// Example: BalanceExtras — Tier-1/2 bundle of common balance toggles.
//
// All knobs route through Harmony postfixes on Director.OnLevelLoaded (for
// LevelConfig/Director field overrides) and on a few getters (for the
// LockSeason / NoRedMoon behaviour toggles), so they keep working even if
// the game reloads its scriptable assets between islands.

using HarmonyLib;
using MelonLoader;
using KingdomMod;

[assembly: MelonInfo(typeof(KingdomMod.Examples.BalanceExtras.BalanceExtrasMod), "Balance Extras", "0.1.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.BalanceExtras
{
    public sealed class BalanceExtrasMod : MelonMod
    {
        internal static MelonPreferences_Entry<float> IncomeMultiplier;
        internal static MelonPreferences_Entry<int>   StartingCoins;
        internal static MelonPreferences_Entry<int>   StartingBeggars;
        internal static MelonPreferences_Entry<int>   StartingPeasants;
        internal static MelonPreferences_Entry<int>   StartingGems;
        internal static MelonPreferences_Entry<int>   FreeBoatParts;
        internal static MelonPreferences_Entry<float> SailInTime;
        internal static MelonPreferences_Entry<float> CaveEscapeTimer;
        internal static MelonPreferences_Entry<bool>  LockSeason;
        internal static MelonPreferences_Entry<int>   LockedSeason;
        internal static MelonPreferences_Entry<bool>  NoRedMoon;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("KingdomMod.BalanceExtras", "Balance Extras");

            IncomeMultiplier = cat.CreateEntry("IncomeMultiplier", 1.0f,
                "Multiplier on LevelConfig.incomeMultiplier (chests + merchant). 1 = vanilla.");
            StartingCoins    = cat.CreateEntry("StartingCoins",    -1, "Override LevelConfig.startingCoins. -1 = leave alone.");
            StartingBeggars  = cat.CreateEntry("StartingBeggars",  -1, "Override LevelConfig.startingBeggars. -1 = leave alone.");
            StartingPeasants = cat.CreateEntry("StartingPeasants", -1, "Override LevelConfig.startingPeasants. -1 = leave alone.");
            StartingGems     = cat.CreateEntry("StartingGems",     -1, "Override LevelConfig.startingGems. -1 = leave alone.");
            FreeBoatParts    = cat.CreateEntry("FreeBoatParts",    -1, "Override LevelConfig.freeBoatParts. -1 = leave alone.");

            SailInTime       = cat.CreateEntry("SailInTime",      -1f, "Override Director.sailInTime (time-of-day). -1 = leave alone.");
            CaveEscapeTimer  = cat.CreateEntry("CaveEscapeTimer", -1f, "Override LevelConfig.caveEscapeTimer. -1 = leave alone.");

            LockSeason   = cat.CreateEntry("LockSeason",   false, "If true, force CurrentSeason to LockedSeason every query.");
            LockedSeason = cat.CreateEntry("LockedSeason", 0,     "Season index: 0=Spring, 1=Summer, 2=Autumn, 3=Winter.");
            NoRedMoon    = cat.CreateEntry("NoRedMoon",    false, "If true, Director.IsRedMoonPauseTime always returns false (no red-moon downtime).");

            HarmonyHelper.PatchAll(this);
            LoggerInstance.Msg("Balance Extras loaded.");
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.Director), nameof(Il2Cpp.Director.OnLevelLoaded))]
    internal static class DirectorOnLevelLoadedPatch
    {
        // Director._currentLevelConfig is private in IL2CPP, but Il2CppInterop
        // surfaces every field as a public property on the interop type (the
        // only real managed field is the native pointer
        // NativeFieldInfoPtr__currentLevelConfig). So AccessTools.Field returns
        // null here - access the generated property directly instead.
        private static void Postfix(Il2Cpp.Director __instance)
        {
            try
            {
                var sail = BalanceExtrasMod.SailInTime.Value;
                if (sail >= 0f) __instance.sailInTime = sail;
            }
            catch { }

            Il2Cpp.LevelConfig cfg = null;
            try { cfg = __instance._currentLevelConfig; }
            catch { }
            if (cfg == null) return;

            try
            {
                var mul = BalanceExtrasMod.IncomeMultiplier.Value;
                if (mul > 0f && mul != 1f) cfg.incomeMultiplier *= mul;

                ApplyIfSet(BalanceExtrasMod.StartingCoins.Value,    v => cfg.startingCoins    = v);
                ApplyIfSet(BalanceExtrasMod.StartingBeggars.Value,  v => cfg.startingBeggars  = v);
                ApplyIfSet(BalanceExtrasMod.StartingPeasants.Value, v => cfg.startingPeasants = v);
                ApplyIfSet(BalanceExtrasMod.StartingGems.Value,     v => cfg.startingGems     = v);
                ApplyIfSet(BalanceExtrasMod.FreeBoatParts.Value,    v => cfg.freeBoatParts    = v);

                var cave = BalanceExtrasMod.CaveEscapeTimer.Value;
                if (cave >= 0f) cfg.caveEscapeTimer = cave;
            }
            catch { /* LevelConfig writes may NRE if the asset is being swapped */ }
        }

        private static void ApplyIfSet(int v, System.Action<int> set) { if (v >= 0) set(v); }
    }

    [HarmonyPatch(typeof(Il2Cpp.Director), "get_CurrentSeason")]
    internal static class DirectorGetCurrentSeasonPatch
    {
        private static void Postfix(ref Il2Cpp.Season __result)
        {
            if (BalanceExtrasMod.LockSeason.Value)
                __result = (Il2Cpp.Season)BalanceExtrasMod.LockedSeason.Value;
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.Director), nameof(Il2Cpp.Director.GetSeasonFromDay))]
    internal static class DirectorGetSeasonFromDayPatch
    {
        private static void Postfix(ref Il2Cpp.Season __result)
        {
            if (BalanceExtrasMod.LockSeason.Value)
                __result = (Il2Cpp.Season)BalanceExtrasMod.LockedSeason.Value;
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.Director), nameof(Il2Cpp.Director.GetNextDaySeason))]
    internal static class DirectorGetNextDaySeasonPatch
    {
        private static void Postfix(ref Il2Cpp.Season __result)
        {
            if (BalanceExtrasMod.LockSeason.Value)
                __result = (Il2Cpp.Season)BalanceExtrasMod.LockedSeason.Value;
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.Director), "get_IsRedMoonPauseTime")]
    internal static class DirectorIsRedMoonPauseTimePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (BalanceExtrasMod.NoRedMoon.Value) __result = false;
        }
    }
}

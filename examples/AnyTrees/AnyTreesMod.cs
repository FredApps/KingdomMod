// AnyTrees — the monarch can mark ANY tree for chopping, AND can build towers,
// farms, and any other PayableUpgrade in the gaps between trees inside the
// forest (vanilla restricts those upgrades to the deforested "buildable
// region" maintained by Forest.GetRegion).
//
// Tree-marking mechanism: in KTC each chopable tree is wrapped by a
// `PayableTree`. Two pieces of game code decide whether the monarch can
// select it as a marker target:
//
//   * `PayableTree.isSelectable` — a runtime bool the game keeps updated.
//   * `PayableTree.UpdateSelectableStatus(Portal)` — the recompute, which
//     turns the bool off for trees too close to the forest edge or behind a
//     portal exclusion.
//   * `PayableTree.CanSelect(Player)` — the per-tick gate the player input
//     code asks before letting the monarch's marker latch onto the tree.
//
// We post-fix both `UpdateSelectableStatus` (so the bool snaps back on after
// every recompute) and `CanSelect` (so anything reading the gate sees true).
// We do NOT touch `Pay`, `MarkForChopping`, or any worker code — paying to
// mark is still the monarch's choice, made for one tree at a time.
//
// Build-in-forest mechanism: `PayableUpgrade` has a per-instance field
// `onlyInBuildableRegion`. The IsLocked postfix alone wasn't enough — the
// vanilla check runs inside Awake/OnEnable and likely disables the spot's
// GameObject before IsLocked is ever asked, so the build marker simply
// doesn't appear in-forest. We mutate the field directly instead: Awake
// postfix caches each new upgrade's original flag and pushes false when
// AnyTrees is on. Runtime toggle re-sweeps every existing instance via
// Resources.FindObjectsOfTypeAll<PayableUpgrade>, flipping or restoring
// based on current state. The IsLocked postfix stays as belt-and-braces.
//
// Scope: only TOWERS get the in-forest exemption. Walls and farms keep
// the vanilla region restriction even with AnyTrees on — chopping space
// for those structures stays the player's responsibility. We inspect
// PayableUpgrade.nextPrefab; if it has a Tower component we bypass, else
// we leave the spot's original flag intact.
//
// Runtime toggle: the user can flip the behavior on/off in the F1 console.
// Patches consult AnyTreesMod.Enabled each call; when Enabled is false the
// postfixes are no-ops and vanilla selection rules apply immediately. The
// MelonPreferences entry persists the choice across launches.

using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using KingdomMod;
using Il2Cpp;
using UnityEngine;

[assembly: MelonInfo(typeof(KingdomMod.Examples.AnyTrees.AnyTreesMod), "Any Trees", "0.5.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.AnyTrees
{
    public sealed class AnyTreesMod : MelonMod
    {
        // Two independent features, each its own persisted preference:
        //   * ChopAnyTrees   — mark any tree for chopping (PayableTree patches).
        //   * GuerillaWarfare — build towers between forest trees (PayableUpgrade patches).
        private static MelonPreferences_Entry<bool> _chopAnyTrees;
        private static MelonPreferences_Entry<bool> _guerillaWarfare;

        // ---- Region-flag bookkeeping ---------------------------------------
        // PayableUpgrade.onlyInBuildableRegion is a per-instance field set by
        // the level designer. Vanilla code checks it during Awake / OnEnable
        // and likely DISABLES the GameObject when the spot lands inside a
        // forest — by the time IsLocked could be asked, the spot is gone.
        // So we mutate the field directly: cache the original value the first
        // time we see each instance, then push false while AnyTrees is on
        // (build anywhere), and push the saved value back when AnyTrees is off.
        // Keyed by the Il2Cpp object pointer so dead-objects fall out naturally
        // on next pass; on scene reload we just see new pointers.

        private static readonly Dictionary<IntPtr, bool> _originalRegionFlags = new();

        /// <summary>Tree-marking feature. Read by the PayableTree postfixes each call.
        /// Public so the loader can flip it via the registered F1 toggle.</summary>
        public static bool ChopAnyTrees
        {
            get => _chopAnyTrees?.Value ?? false;
            set
            {
                if (_chopAnyTrees == null) return;
                if (_chopAnyTrees.Value == value) return;
                _chopAnyTrees.Value = value;
                MelonPreferences.Save();
            }
        }

        /// <summary>Build-towers-in-forest feature. Read by the PayableUpgrade postfixes.
        /// Toggling re-sweeps every existing upgrade's region flag and persists the choice.</summary>
        public static bool GuerillaWarfare
        {
            get => _guerillaWarfare?.Value ?? false;
            set
            {
                if (_guerillaWarfare == null) return;
                if (_guerillaWarfare.Value == value) return;
                _guerillaWarfare.Value = value;
                MelonPreferences.Save();
                ReapplyAllRegionFlags();
            }
        }

        /// <summary>Cache the upgrade's original region flag and push our override (false while Enabled, but only for towers).</summary>
        internal static void RegisterUpgrade(PayableUpgrade u)
        {
            if (u == null) return;
            var p = u.Pointer;
            if (p == IntPtr.Zero) return;
            if (!_originalRegionFlags.ContainsKey(p))
                _originalRegionFlags[p] = u.onlyInBuildableRegion;
            u.onlyInBuildableRegion = ShouldBypassRegion(u) ? false : _originalRegionFlags[p];
        }

        /// <summary>True only when AnyTrees is enabled AND the upgrade's target is a tower.
        /// Walls / farms / workshops keep their original region restriction even with the
        /// mod on — chopping space for those structures should still be the player's job.</summary>
        private static bool ShouldBypassRegion(PayableUpgrade u)
        {
            if (!GuerillaWarfare) return false;
            var prefab = u.nextPrefab;
            if (prefab == null) return false;
            // Tower covers basic, archer, and upgrade-tier towers; Wall (defensive)
            // and Farmland (farm plot) are deliberately NOT in the whitelist.
            return prefab.GetComponent<Tower>() != null;
        }

        /// <summary>Sweep every PayableUpgrade in the scene and re-apply the field per current Enabled state.</summary>
        private static void ReapplyAllRegionFlags()
        {
            int cleared = 0, restored = 0;
            foreach (var u in Resources.FindObjectsOfTypeAll<PayableUpgrade>())
            {
                if (u == null) continue;
                var p = u.Pointer;
                if (!_originalRegionFlags.TryGetValue(p, out var orig))
                {
                    orig = u.onlyInBuildableRegion;
                    _originalRegionFlags[p] = orig;
                }
                if (ShouldBypassRegion(u)) { u.onlyInBuildableRegion = false; cleared++; }
                else                       { u.onlyInBuildableRegion = orig;  restored++; }
            }
            MelonLogger.Msg($"[AnyTrees] Region flag re-applied: GuerillaWarfare={GuerillaWarfare} (towers cleared: {cleared}, others restored/left: {restored}).");
        }

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("KingdomMod.AnyTrees", "Any Trees");
            // Default OFF so a fresh install matches vanilla until the player opts in.
            // MelonPreferences persists these to UserData/MelonPreferences.cfg, so the
            // F1 menu state is remembered across sessions.
            _chopAnyTrees = cat.CreateEntry("ChopAnyTrees", false,
                "When true, every tree is selectable for chopping (including forest-edge trees).");
            _guerillaWarfare = cat.CreateEntry("GuerillaWarfare", false,
                "When true, towers can be built between trees inside the forest.");

            HarmonyHelper.PatchAll(this);

            // Two independent F1 → Mods controls:
            //   Builder cowardice — radio: Lame (off) = vanilla, Brave (on) =
            //                       mark any tree for chopping. Index 0 = Lame so
            //                       the Reset (all off) button lands on vanilla.
            //   Guerilla Warfare  — toggle: build towers in forest areas.
            Kingdom.Mods.RegisterChoice("Builder cowardice",
                new[] { "Lame", "Brave" },
                () => ChopAnyTrees ? 1 : 0,
                idx => ChopAnyTrees = (idx == 1),
                "Lame = vanilla (only cleared trees can be marked). Brave = mark ANY tree for chopping, including forest-edge and deep-forest trees. Workers still only chop trees you've paid to mark.");
            Kingdom.Mods.RegisterChoice("Guerilla Warfare",
                new[] { "Off", "On" },
                () => GuerillaWarfare ? 1 : 0,
                idx => GuerillaWarfare = (idx == 1),
                "Build towers between the trees inside the forest, instead of only in the cleared buildable region. Affects towers only; walls/farms/workshops keep the vanilla restriction.");

            // Apply the persisted GuerillaWarfare state to any upgrades already in the scene.
            ReapplyAllRegionFlags();

            LoggerInstance.Msg($"AnyTrees ready (ChopAnyTrees={ChopAnyTrees}, GuerillaWarfare={GuerillaWarfare}). " +
                               "Toggle in F1 → Mods. Workers still only chop trees the monarch has paid to mark.");
        }
    }

    /// <summary>
    /// The game periodically calls <c>UpdateSelectableStatus</c> on every
    /// <see cref="PayableTree"/> to recompute whether the monarch's marker is
    /// allowed to latch on.  We skip the original entirely and force
    /// isSelectable = true; the postfix approach raced with the original's
    /// own propagation paths (RPC + indicator refresh).
    /// </summary>
    [HarmonyPatch(typeof(PayableTree), "UpdateSelectableStatus")]
    internal static class UpdateSelectableStatusPatch
    {
        private static bool _loggedHit;

        private static bool Prefix(PayableTree __instance)
        {
            if (!AnyTreesMod.ChopAnyTrees) return true;
            if (__instance == null) return true;

            // Once the tree is marked, let vanilla compute isSelectable = false
            // so the coin indicator gets torn down. Forcing true here is what
            // kept the indicator visible after marking.
            var workable = __instance.GetComponent<WorkableTree>();
            if (workable != null && workable.IsMarked()) return true;

            __instance.isSelectable = true;
            if (!_loggedHit)
            {
                _loggedHit = true;
                MelonLoader.MelonLogger.Msg("[AnyTrees] UpdateSelectableStatus patch firing (first hit).");
            }
            return false;
        }
    }

    /// <summary>
    /// Belt-and-braces: the marker UI may consult <c>CanSelect(Player)</c>
    /// directly without going through <c>isSelectable</c>.  Force-true here
    /// guarantees the monarch's marker can latch onto every tree, including
    /// edge/forest-boundary trees that the vanilla check would reject.
    /// </summary>
    [HarmonyPatch(typeof(PayableTree), nameof(PayableTree.CanSelect))]
    internal static class CanSelectPatch
    {
        private static bool _loggedHit;

        private static void Postfix(PayableTree __instance, ref bool __result)
        {
            if (!AnyTreesMod.ChopAnyTrees) return;
            if (__instance == null) return;

            // Don't override post-marking — same reason as UpdateSelectableStatus.
            var workable = __instance.GetComponent<WorkableTree>();
            if (workable != null && workable.IsMarked()) return;

            __result = true;
            if (!_loggedHit)
            {
                _loggedHit = true;
                MelonLoader.MelonLogger.Msg("[AnyTrees] CanSelect patch firing (first hit).");
            }
        }
    }

    /// <summary>
    /// Payment gate. The marker indicator's "crossed coin" visual is driven
    /// from CanPay, separate from CanSelect. Forcing the result true lets
    /// the monarch actually pay the 1-coin marker fee on edge trees the
    /// vanilla check rejected.
    ///
    /// Important: once the tree has already been marked, the coin indicator
    /// should disappear (vanilla returns false from CanPay because the tree
    /// is already paid). We MUST NOT override that — otherwise the coin
    /// stays visible on already-marked trees. Skip the override when the
    /// sibling WorkableTree reports the tree is marked.
    /// </summary>
    [HarmonyPatch(typeof(PayableTree), "CanPay")]
    internal static class CanPayPatch
    {
        private static bool _loggedHit;

        private static void Postfix(PayableTree __instance, ref bool __result)
        {
            if (!AnyTreesMod.ChopAnyTrees) return;
            if (__instance == null) return;

            // If the tree is already marked, leave vanilla's result alone so
            // the coin indicator hides like normal.
            try
            {
                var workable = __instance.GetComponent<WorkableTree>();
                if (workable != null && workable.IsMarked()) return;
            }
            catch { /* GetComponent shouldn't throw, but stay defensive */ }

            __result = true;
            if (!_loggedHit)
            {
                _loggedHit = true;
                MelonLoader.MelonLogger.Msg("[AnyTrees] CanPay patch firing (first hit).");
            }
        }
    }

    /// <summary>
    /// Hook every newly-spawned PayableUpgrade and cache its original
    /// onlyInBuildableRegion value. While AnyTrees is on we flip the field
    /// to false so the vanilla region check short-circuits before it can
    /// disable the spot's GameObject. ReapplyAllRegionFlags handles the
    /// runtime toggle for already-spawned instances.
    /// </summary>
    [HarmonyPatch(typeof(PayableUpgrade), "Awake")]
    internal static class PayableUpgradeAwakePatch
    {
        private static void Postfix(PayableUpgrade __instance)
        {
            AnyTreesMod.RegisterUpgrade(__instance);
        }
    }

    /// <summary>
    /// Belt-and-braces: if any code path still consults IsLocked and returns
    /// InvalidRegion (e.g. the field mutation raced an Awake call), the
    /// postfix here clears that specific reason. Every other lock reason
    /// (StoneTechRequired, NeedsPassengerKnight, ...) is left to vanilla.
    /// </summary>
    [HarmonyPatch(typeof(PayableUpgrade), nameof(PayableUpgrade.IsLocked))]
    internal static class PayableUpgradeIsLockedPatch
    {
        private static bool _loggedHit;

        private static void Postfix(PayableUpgrade __instance, ref bool __result, ref LockIndicator.LockReason reason)
        {
            if (!AnyTreesMod.GuerillaWarfare) return;
            if (!__result) return;
            if (reason != LockIndicator.LockReason.InvalidRegion) return;
            // Towers only — walls/farms must keep their region restriction.
            var prefab = __instance != null ? __instance.nextPrefab : null;
            if (prefab == null || prefab.GetComponent<Tower>() == null) return;

            __result = false;
            reason = LockIndicator.LockReason.Invalid;
            if (!_loggedHit)
            {
                _loggedHit = true;
                MelonLoader.MelonLogger.Msg("[AnyTrees] PayableUpgrade.IsLocked region-block bypassed for tower (first hit).");
            }
        }
    }
}

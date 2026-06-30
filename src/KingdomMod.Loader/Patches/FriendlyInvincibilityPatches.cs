using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace KingdomMod.Loader.Patches
{
    internal static class FriendlyInvincibility
    {
        private static readonly Dictionary<IntPtr, bool> OriginalStates = new();
        private static float _nextSweep;

        public static bool Enabled => LoaderMod.Instance != null && LoaderMod.Instance.FriendlyInvincibilityEnabled;

        public static void Tick()
        {
            if (!Enabled)
            {
                RestoreAll();
                return;
            }

            if (Time.unscaledTime < _nextSweep) return;
            _nextSweep = Time.unscaledTime + 0.5f;
            ApplyToFriendlyDamageables();
        }

        public static bool IsProtected(Damageable damageable)
        {
            if (damageable == null) return false;
            var pointer = damageable.Pointer;
            return pointer != IntPtr.Zero && OriginalStates.ContainsKey(pointer);
        }

        private static void ApplyToFriendlyDamageables()
        {
            foreach (var damageable in Resources.FindObjectsOfTypeAll<Damageable>())
            {
                if (damageable == null || damageable.gameObject == null) continue;
                if (!damageable.gameObject.activeInHierarchy) continue;
                if (!IsFriendly(damageable)) continue;

                var pointer = damageable.Pointer;
                if (pointer == IntPtr.Zero) continue;
                if (!OriginalStates.ContainsKey(pointer))
                    OriginalStates[pointer] = damageable.invulnerable;
                if (!damageable.invulnerable)
                    damageable.invulnerable = true;
            }
        }

        private static void RestoreAll()
        {
            if (OriginalStates.Count == 0) return;

            var snapshot = new List<IntPtr>(OriginalStates.Keys);
            foreach (var pointer in snapshot)
            {
                if (!OriginalStates.TryGetValue(pointer, out var original)) continue;
                var damageable = FindDamageable(pointer);
                if (damageable != null)
                {
                    try { damageable.invulnerable = original; } catch { }
                }
                OriginalStates.Remove(pointer);
            }
        }

        private static Damageable FindDamageable(IntPtr pointer)
        {
            foreach (var damageable in Resources.FindObjectsOfTypeAll<Damageable>())
            {
                if (damageable != null && damageable.Pointer == pointer)
                    return damageable;
            }
            return null;
        }

        private static bool IsFriendly(Damageable damageable)
        {
            try
            {
                var go = damageable.gameObject;
                if (go == null) return false;
                if (go.GetComponentInParent<Player>() != null) return true;
                if (go.GetComponentInParent<Peasant>() != null) return true;
                if (go.GetComponentInParent<Worker>() != null) return true;
                if (go.GetComponentInParent<Archer>() != null) return true;
                if (go.GetComponentInParent<Knight>() != null) return true;
                if (go.GetComponentInParent<Berserker>() != null) return true;
                if (go.GetComponentInParent<Hermit>() != null) return true;
                if (go.GetComponentInParent<Farmer>() != null) return true;
                if (go.GetComponentInParent<Pikeman>() != null) return true;
                if (go.GetComponentInParent<WarriorGhost>() != null) return true;
                if (go.GetComponentInParent<WarriorGhostLeader>() != null) return true;
                if (go.GetComponentInParent<Catapult>() != null) return true;
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(Damageable), nameof(Damageable.HitFlash))]
    internal static class DamageableHitFlashInvincibilityPatch
    {
        private static bool Prefix(Damageable __instance)
        {
            return !FriendlyInvincibility.Enabled || !FriendlyInvincibility.IsProtected(__instance);
        }
    }
}

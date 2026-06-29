using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace KingdomMod.Loader.Patches
{
    internal static class BoarVanishFixPatch
    {
        private const float NestReturnRadius = 7.5f;

        private static readonly Dictionary<IntPtr, BoarTrack> _boars = new();
        private static readonly Dictionary<IntPtr, PendingGroupFix> _groups = new();
        private static readonly HashSet<IntPtr> _killed = new();
        private static MethodInfo _spawnRewardMethod;
        private static MethodInfo _spawnLeftoverMethod;
        private static FieldInfo _spawnedBoarField;
        private static FieldInfo _spawnedFlagField;
        private static bool _loggedActive;

        private struct BoarTrack
        {
            public BoarSpawnGroup Group;
            public Vector3 LastPosition;
            public bool RewardGenerated;
            public bool NestReturn;
        }

        private struct PendingGroupFix
        {
            public Vector3 LastPosition;
            public bool RewardGenerated;
            public bool NestReturn;
        }

        internal static void Tick()
        {
            var loader = LoaderMod.Instance;
            if (loader == null || !loader.BoarVanishFixEnabled) return;

            if (!_loggedActive)
            {
                _loggedActive = true;
                MelonLogger.Msg("[KingdomMod.Loader] Boar vanish fix active.");
            }

            foreach (var boar in Resources.FindObjectsOfTypeAll<Boar>())
            {
                if (!IsLiveSceneBoar(boar)) continue;
                var key = boar.Pointer;
                if (!_boars.TryGetValue(key, out var track))
                    track = new BoarTrack();
                track.LastPosition = boar.transform.position;
                _boars[key] = track;
            }
        }

        internal static void TrackInit(Boar boar, BoarSpawnGroup group)
        {
            if (!Enabled || boar == null) return;
            try
            {
                _boars[boar.Pointer] = new BoarTrack
                {
                    Group = group,
                    LastPosition = boar.transform.position
                };
            }
            catch { }
        }

        internal static void TrackDeath(Boar boar)
        {
            if (boar == null) return;
            try { _killed.Add(boar.Pointer); } catch { }
        }

        internal static void TrackGone(Boar boar, string source)
        {
            var loader = LoaderMod.Instance;
            if (loader == null || !loader.BoarVanishFixEnabled || boar == null) return;
            if (!IsWinter()) return;

            var key = boar.Pointer;
            if (_killed.Contains(key))
            {
                _boars.Remove(key);
                _killed.Remove(key);
                return;
            }

            if (IsDespawnOnLoad(boar))
            {
                _boars.Remove(key);
                return;
            }

            if (!_boars.TryGetValue(key, out var track))
            {
                track = new BoarTrack { LastPosition = SafePosition(boar) };
            }

            if (track.Group == null)
                track.Group = GetPrivateField<BoarSpawnGroup>(boar, "spawnBush");

            bool nestReturn = IsNearNest(track.Group, track.LastPosition);
            track.NestReturn = nestReturn;

            if (track.Group != null)
            {
                _groups[track.Group.Pointer] = new PendingGroupFix
                {
                    LastPosition = track.LastPosition,
                    RewardGenerated = track.RewardGenerated,
                    NestReturn = nestReturn
                };
            }

            if (nestReturn)
            {
                loader.LogToConsole($"Boar vanish fix: boar returned to nest ({source}); no coin effect.");
                _boars.Remove(key);
                return;
            }

            if (!track.RewardGenerated)
            {
                bool generated = TryRunCoinEffect(boar, track.LastPosition, loader);
                track.RewardGenerated = generated;
                if (track.Group != null && _groups.TryGetValue(track.Group.Pointer, out var pending))
                {
                    pending.RewardGenerated = generated;
                    _groups[track.Group.Pointer] = pending;
                }

                if (generated)
                {
                    loader.ReportFixApplied("Boar vanish fix", $"Boar vanished away from nest; generated boar coin reward and blocked nest respawn. Source: {source}.");
                }
                else
                {
                    loader.ReportFixApplied("Boar vanish fix", $"Boar vanished away from nest; coin effect was unavailable, so stale nest respawn was blocked. Source: {source}.");
                }
            }

            SuppressImmediateRespawn(track.Group, boar);
            _boars[key] = track;
        }

        internal static bool ShouldSuppressReset(BoarSpawnGroup group)
        {
            if (group == null || !Enabled) return false;
            if (!_groups.TryGetValue(group.Pointer, out var pending)) return false;
            if (pending.NestReturn)
            {
                _groups.Remove(group.Pointer);
                return false;
            }

            LoaderMod.Instance?.ReportFixApplied("Boar vanish fix", "Suppressed stale boar nest reset after field disappearance.");
            return true;
        }

        internal static void TrackSpawn(BoarSpawnGroup group)
        {
            if (group == null || !Enabled) return;
            if (!_groups.TryGetValue(group.Pointer, out var pending)) return;
            if (pending.NestReturn)
            {
                _groups.Remove(group.Pointer);
                return;
            }

            LoaderMod.Instance?.ReportFixApplied("Boar vanish fix", "Detected attempted nest respawn after field disappearance; leaving stale reset suppressed.");
            SetGroupSpawned(group, true, null);
        }

        private static bool Enabled => LoaderMod.Instance == null || LoaderMod.Instance.BoarVanishFixEnabled;

        private static bool IsLiveSceneBoar(Boar boar)
        {
            try
            {
                return boar != null
                       && boar.gameObject != null
                       && boar.gameObject.activeInHierarchy
                       && boar.gameObject.scene.handle != 0;
            }
            catch { return false; }
        }

        private static bool IsWinter()
        {
            try { return Kingdom.Time.CurrentSeason == Season.Winter; }
            catch { return false; }
        }

        private static bool IsDespawnOnLoad(Boar boar)
        {
            try { return boar.DespawnOnLoad; }
            catch { return false; }
        }

        private static Vector3 SafePosition(Boar boar)
        {
            try { return boar.transform.position; }
            catch { return Vector3.zero; }
        }

        private static bool IsNearNest(BoarSpawnGroup group, Vector3 pos)
        {
            if (group == null) return false;
            try
            {
                var nest = group.transform.position;
                float dx = pos.x - nest.x;
                float dy = pos.y - nest.y;
                return (dx * dx) + (dy * dy) <= NestReturnRadius * NestReturnRadius;
            }
            catch { return false; }
        }

        private static bool TryRunCoinEffect(Boar boar, Vector3 position, LoaderMod loader)
        {
            try
            {
                boar.transform.position = position;
                var routine = InvokeRewardCoroutine(boar, "SpawnRewardCurrency");
                if (routine == null)
                    routine = InvokeRewardCoroutine(boar, "SpawnLeftoverCurrency");
                if (routine == null)
                {
                    loader.LogToConsole("Boar vanish fix: reward coroutine not found.");
                    return false;
                }

                MelonCoroutines.Start(routine);
                return true;
            }
            catch (Exception e)
            {
                loader.LogToConsole($"Boar vanish fix: coin effect failed: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        private static IEnumerator InvokeRewardCoroutine(Boar boar, string name)
        {
            MethodInfo method;
            if (name == "SpawnRewardCurrency")
                method = _spawnRewardMethod ??= AccessTools.Method(typeof(Boar), "SpawnRewardCurrency");
            else
                method = _spawnLeftoverMethod ??= AccessTools.Method(typeof(Boar), "SpawnLeftoverCurrency");

            return method?.Invoke(boar, Array.Empty<object>()) as IEnumerator;
        }

        private static void SuppressImmediateRespawn(BoarSpawnGroup group, Boar boar)
        {
            if (group == null) return;
            SetGroupSpawned(group, true, boar);
        }

        private static void SetGroupSpawned(BoarSpawnGroup group, bool spawned, Boar boar)
        {
            try
            {
                _spawnedFlagField ??= AccessTools.Field(typeof(BoarSpawnGroup), "_spawnedBoar");
                _spawnedFlagField?.SetValue(group, spawned);
            }
            catch { }

            try
            {
                _spawnedBoarField ??= AccessTools.Field(typeof(BoarSpawnGroup), "spawnedBoar");
                _spawnedBoarField?.SetValue(group, boar);
            }
            catch { }
        }

        private static T GetPrivateField<T>(object instance, string name) where T : class
        {
            try { return AccessTools.Field(instance.GetType(), name)?.GetValue(instance) as T; }
            catch { return null; }
        }
    }

    [HarmonyPatch(typeof(Boar), nameof(Boar.Init))]
    internal static class BoarInitPatch
    {
        private static void Postfix(Boar __instance, BoarSpawnGroup spawner)
            => BoarVanishFixPatch.TrackInit(__instance, spawner);
    }

    [HarmonyPatch]
    internal static class BoarDeathPatch
    {
        private static MethodBase TargetMethod()
            => AccessTools.Method(typeof(Boar), "HandleOnDeath");

        private static void Prefix(Boar __instance)
            => BoarVanishFixPatch.TrackDeath(__instance);
    }

    [HarmonyPatch]
    internal static class BoarDisablePatch
    {
        private static MethodBase TargetMethod()
            => AccessTools.Method(typeof(Boar), "OnDisable");

        private static void Postfix(Boar __instance)
            => BoarVanishFixPatch.TrackGone(__instance, "OnDisable");
    }

    [HarmonyPatch]
    internal static class BoarDestroyPatch
    {
        private static MethodBase TargetMethod()
            => AccessTools.Method(typeof(Boar), "OnDestroy");

        private static void Prefix(Boar __instance)
            => BoarVanishFixPatch.TrackGone(__instance, "OnDestroy");
    }

    [HarmonyPatch(typeof(BoarSpawnGroup), nameof(BoarSpawnGroup.ResetBoarTrigger))]
    internal static class BoarSpawnGroupResetPatch
    {
        private static bool Prefix(BoarSpawnGroup __instance)
            => !BoarVanishFixPatch.ShouldSuppressReset(__instance);
    }

    [HarmonyPatch]
    internal static class BoarSpawnGroupSpawnPatch
    {
        private static MethodBase TargetMethod()
            => AccessTools.Method(typeof(BoarSpawnGroup), "SpawnBoar");

        private static void Postfix(BoarSpawnGroup __instance)
            => BoarVanishFixPatch.TrackSpawn(__instance);
    }
}

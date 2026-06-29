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
    [HarmonyPatch]
    internal static class CrownPickupFixPatch
    {
        private const float StuckDelaySeconds = 10f;
        private const float RepairRadius = 1.25f;
        private const float RetryCooldownSeconds = 2f;
        private static readonly Dictionary<IntPtr, CrownTrack> _tracks = new();
        private static readonly Dictionary<IntPtr, LossTrack> _losses = new();
        private static MethodInfo _pickupCrownMethod;
        private static bool _loggedActive;

        private struct CrownTrack
        {
            public float FirstSeen;
            public float NextRetry;
        }

        private struct LossTrack
        {
            public float FirstSeen;
            public float NextRetry;
        }

        private static MethodBase TargetMethod()
            => AccessTools.Method(typeof(Player), "Update");

        private static void Postfix(Player __instance)
        {
            var loader = LoaderMod.Instance;
            if (loader == null || !loader.CrownPickupFixEnabled) return;
            if (__instance == null) return;
            var playerKey = __instance.Pointer;
            if (PlayerHasCrown(__instance))
            {
                _losses.Remove(playerKey);
                return;
            }
            if (!IsEligibleCrownlessPlayer(__instance)) return;

            if (!_loggedActive)
            {
                _loggedActive = true;
                MelonLogger.Msg("[KingdomMod.Loader] Crown pickup fix active (10s delay, 1.25 radius, missing-object fallback).");
            }

            float now = Time.time;
            foreach (var crown in Resources.FindObjectsOfTypeAll<Crown>())
            {
                if (crown == null) continue;
                var key = crown.Pointer;
                if (!IsTrackableDroppedCrown(crown))
                {
                    _tracks.Remove(key);
                    continue;
                }

                if (!_tracks.TryGetValue(key, out var track))
                {
                    track = new CrownTrack { FirstSeen = now, NextRetry = 0f };
                    _tracks[key] = track;
                    continue;
                }

                if (now - track.FirstSeen < StuckDelaySeconds) continue;
                if (now < track.NextRetry) continue;
                if (!IsNear(__instance, crown, RepairRadius)) continue;

                track.NextRetry = now + RetryCooldownSeconds;
                _tracks[key] = track;
                TryRepairPickup(__instance, crown, loader);
            }

            TryReturnMissingCrown(__instance, now, loader);
        }

        internal static void TrackCrownLoss(Player player, bool destroyImmediately)
        {
            var loader = LoaderMod.Instance;
            if (loader == null || !loader.CrownPickupFixEnabled) return;
            if (player == null || destroyImmediately) return;
            if (PlayerHasCrown(player)) return;

            try
            {
                _losses[player.Pointer] = new LossTrack
                {
                    FirstSeen = Time.time,
                    NextRetry = 0f
                };
            }
            catch { }
        }

        private static bool IsEligibleCrownlessPlayer(Player player)
        {
            try { if (!player.CanInteractWithWorld) return false; } catch { }
            try { if (player.gameObject == null || !player.gameObject.activeInHierarchy) return false; } catch { return false; }
            return true;
        }

        private static bool PlayerHasCrown(Player player)
        {
            try { return player == null || player.hasCrown; }
            catch { return true; }
        }

        private static bool IsTrackableDroppedCrown(Crown crown)
        {
            try
            {
                if (crown.gameObject == null) return false;
                if (!crown.gameObject.activeInHierarchy) return false;
                if (crown.gameObject.scene.handle == 0) return false;
                if (crown.heldByFoe) return false;

                var droppable = crown.droppable;
                if (droppable == null) return false;
                if (droppable.pickedUp) return false;

                var owner = crown.owningPlayer;
                if (owner != null && owner.hasCrown) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsNear(Player player, Crown crown, float radius)
        {
            try
            {
                float dx = player.transform.position.x - crown.transform.position.x;
                float dy = player.transform.position.y - crown.transform.position.y;
                return (dx * dx) + (dy * dy) <= radius * radius;
            }
            catch
            {
                return false;
            }
        }

        private static void TryRepairPickup(Player player, Crown crown, LoaderMod loader)
        {
            try
            {
                var method = _pickupCrownMethod ??= AccessTools.Method(typeof(Player), "PickupCrown", new[] { typeof(Crown), typeof(bool) });
                if (method == null)
                {
                    loader.LogToConsole("Crown pickup fix: PickupCrown method not found.");
                    return;
                }

                PrepareDroppableForPlayer(crown, player);
                var routine = method.Invoke(player, new object[] { crown, true }) as IEnumerator;
                if (routine == null)
                {
                    loader.LogToConsole("Crown pickup fix: pickup routine unavailable.");
                    return;
                }

                MelonCoroutines.Start(routine);
                loader.ReportFixApplied("Crown pickup fix", $"Repaired dropped crown near Player {player.playerId + 1}.");
            }
            catch (Exception e)
            {
                loader.LogToConsole($"Crown pickup fix failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void TryReturnMissingCrown(Player player, float now, LoaderMod loader)
        {
            var key = player.Pointer;
            if (!_losses.TryGetValue(key, out var loss)) return;
            if (now - loss.FirstSeen < StuckDelaySeconds) return;
            if (now < loss.NextRetry) return;
            if (HasRelevantDroppedCrown(player)) return;

            loss.NextRetry = now + RetryCooldownSeconds;
            _losses[key] = loss;

            try
            {
                player.ForceCrownState(crownInPossession: true, updateWallet: true);
                _losses.Remove(key);
                loader.ReportFixApplied("Crown pickup fix", $"Returned missing crown to Player {player.playerId + 1}.");
            }
            catch (Exception e)
            {
                loader.LogToConsole($"Crown return failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static bool HasRelevantDroppedCrown(Player player)
        {
            foreach (var crown in Resources.FindObjectsOfTypeAll<Crown>())
            {
                if (crown == null) continue;
                try
                {
                    if (crown.gameObject == null) continue;
                    if (!crown.gameObject.activeInHierarchy) continue;
                    if (crown.gameObject.scene.handle == 0) continue;
                    if (crown.heldByFoe) return true;

                    var droppable = crown.droppable;
                    if (droppable == null || droppable.pickedUp) continue;

                    var owner = crown.owningPlayer;
                    if (owner != null)
                    {
                        if (owner.Pointer == player.Pointer) return true;
                        continue;
                    }

                    return true;
                }
                catch { }
            }

            return false;
        }

        private static void PrepareDroppableForPlayer(Crown crown, Player player)
        {
            try
            {
                var droppable = crown.droppable;
                if (droppable == null) return;
                droppable.friendlyClaimer = player.gameObject;
                droppable.enemyClaimer = null;
                droppable.notDropperTarget = false;
                droppable.anyPlayerTarget = true;
                droppable.pickUpPolicy = PickUpPolicy.AnyPlayer;
                droppable.pickedUp = false;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.LoseCrown))]
    internal static class PlayerLoseCrownPatch
    {
        private static void Postfix(Player __instance, bool destroyImmediately)
            => CrownPickupFixPatch.TrackCrownLoss(__instance, destroyImmediately);
    }
}

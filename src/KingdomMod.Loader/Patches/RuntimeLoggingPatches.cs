using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace KingdomMod.Loader.Patches
{
    internal static class RuntimeLog
    {
        private static readonly Dictionary<string, MethodBase> OptionalMethods = new();
        private static readonly HashSet<string> LoggedMissingOptionalMethods = new();

        public static bool Enabled(RuntimeLogLevel level) => RuntimeInteractionLogger.Level >= level;

        public static void Event(RuntimeLogLevel level, string category, string action,
            UnityEngine.Object subject = null,
            UnityEngine.Object target = null,
            System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object>> data = null,
            string before = null,
            string after = null,
            Exception exception = null)
            => RuntimeInteractionLogger.Event(level, category, action, subject, target, data, before, after, exception);

        public static string Obj(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            try { return $"{obj.GetIl2CppType().Name}:{obj.name}"; }
            catch { return obj.name; }
        }

        public static string Go(GameObject go)
        {
            if (go == null) return null;
            try { return $"{go.name}@{go.transform.position.x:0.##},{go.transform.position.y:0.##},{go.transform.position.z:0.##}"; }
            catch { return go.name; }
        }

        public static string DroppableState(Droppable droppable)
        {
            if (droppable == null) return null;
            try
            {
                return $"pickedUp={droppable.pickedUp};friendly={Go(droppable.friendlyClaimer)};enemy={Go(droppable.enemyClaimer)};policy={droppable.pickUpPolicy};anyPlayer={droppable.anyPlayerTarget};notDropper={droppable.notDropperTarget}";
            }
            catch (Exception e) { return "droppable-state-error:" + e.GetType().Name; }
        }

        public static string CrownState(Crown crown)
        {
            if (crown == null) return null;
            try
            {
                var owner = crown.owningPlayer;
                return $"owner={(owner != null ? owner.playerId.ToString() : "null")};heldByFoe={crown.heldByFoe};droppable=({DroppableState(crown.droppable)})";
            }
            catch (Exception e) { return "crown-state-error:" + e.GetType().Name; }
        }

        public static string DamageableState(Damageable damageable)
        {
            if (damageable == null) return null;
            try { return $"hp={damageable.hitPoints};dead={damageable.isDead};invulnerable={damageable.invulnerable};damagedBy={damageable.damagedBy};surface={damageable.surface}"; }
            catch (Exception e) { return "damageable-state-error:" + e.GetType().Name; }
        }

        public static string WalletState(Wallet wallet)
        {
            if (wallet == null) return null;
            try { return $"coins={wallet.Coins};gems={wallet.Gems};capacity={wallet.TotalCapacity}"; }
            catch (Exception e) { return "wallet-state-error:" + e.GetType().Name; }
        }

        public static string ConstructionState(ConstructionBuildingComponent c)
        {
            if (c == null) return null;
            try { return $"needsMoreWork={c.NeedsMoreWork};shouldCancel={c.ShouldCancelBuild}"; }
            catch (Exception e) { return "construction-state-error:" + e.GetType().Name; }
        }

        public static string PlayerState(Player player)
        {
            if (player == null) return null;
            try { return $"playerId={player.playerId};crown={player.hasCrown};model={player.model};item={player.equippedItemOfPower};coins={player.coins};gems={player.gems}"; }
            catch (Exception e) { return "player-state-error:" + e.GetType().Name; }
        }

        public static string BoarState(Boar boar)
        {
            if (boar == null) return null;
            try { return $"despawnOnLoad={boar.DespawnOnLoad};damageable=({DamageableState(boar.Damageable)});reward={boar.rewardCurrencyType}"; }
            catch (Exception e) { return "boar-state-error:" + e.GetType().Name; }
        }

        public static bool RelevantStateMachineOwner(StateMachine sm)
        {
            try
            {
                // Il2CppInterop exposes IL2CPP instance fields as properties, not
                // reflectable .NET fields, so AccessTools.Field can't find _owner;
                // read the generated property directly instead.
                var owner = sm._owner;
                if (owner == null) return false;
                var go = owner.gameObject;
                return go.GetComponentInParent<Player>() != null
                       || go.GetComponentInParent<Worker>() != null
                       || go.GetComponentInParent<Boar>() != null
                       || go.GetComponentInParent<CrownStealer>() != null
                       || go.GetComponentInParent<Portal>() != null
                       || go.GetComponentInParent<Knight>() != null
                       || go.GetComponentInParent<Archer>() != null
                       || go.GetComponentInParent<Peasant>() != null
                       || go.GetComponentInParent<Berserker>() != null;
            }
            catch { return false; }
        }

        public static UnityEngine.Object StateMachineOwner(StateMachine sm)
        {
            try { return sm._owner; }
            catch { return null; }
        }

        public static bool HasOptionalMethod(Type type, string name)
            => OptionalMethod(type, name) != null;

        public static MethodBase OptionalMethod(Type type, string name)
        {
            var key = type.FullName + "." + name;
            if (OptionalMethods.TryGetValue(key, out var method)) return method;

            method = AccessTools.Method(type, name);
            if (method != null)
            {
                OptionalMethods[key] = method;
                return method;
            }

            if (LoggedMissingOptionalMethods.Add(key))
            {
                try { MelonLogger.Warning($"[KingdomMod.RuntimeLog] optional patch target missing: {key}"); } catch { }
                RuntimeInteractionLogger.Event(RuntimeLogLevel.None, "logging", "patch_target_missing",
                    data: RuntimeInteractionLogger.Fields(("type", type.FullName), ("method", name)));
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.LoseCrown))]
    internal static class RuntimeLogPlayerLoseCrownPatch
    {
        private static void Prefix(Player __instance, Vector2 force, bool destroyImmediately, bool knockedByPlayer, out string __state)
        {
            __state = RuntimeLog.PlayerState(__instance);
            RuntimeLog.Event(RuntimeLogLevel.BugFocused, "crown", "player_lose_crown_start", __instance,
                data: RuntimeInteractionLogger.Fields(("force", $"{force.x:0.###},{force.y:0.###}"), ("destroyImmediately", destroyImmediately), ("knockedByPlayer", knockedByPlayer)),
                before: __state);
        }

        private static void Postfix(Player __instance, bool destroyImmediately, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "crown", "player_lose_crown_end", __instance,
                data: RuntimeInteractionLogger.Fields(("destroyImmediately", destroyImmediately)), before: __state, after: RuntimeLog.PlayerState(__instance));
    }

    [HarmonyPatch(typeof(Crown), nameof(Crown.AssignDroppedCrown))]
    internal static class RuntimeLogCrownAssignDroppedPatch
    {
        private static void Prefix(Crown __instance, int playerId, out string __state)
        {
            __state = RuntimeLog.CrownState(__instance);
            RuntimeLog.Event(RuntimeLogLevel.BugFocused, "crown", "assign_dropped_start", __instance,
                data: RuntimeInteractionLogger.Fields(("playerId", playerId)), before: __state);
        }

        private static void Postfix(Crown __instance, int playerId, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "crown", "assign_dropped_end", __instance,
                data: RuntimeInteractionLogger.Fields(("playerId", playerId)), before: __state, after: RuntimeLog.CrownState(__instance));
    }

    [HarmonyPatch(typeof(Crown), "set_heldByFoe")]
    internal static class RuntimeLogCrownHeldByFoePatch
    {
        private static void Prefix(Crown __instance, bool value, out string __state)
        {
            __state = RuntimeLog.CrownState(__instance);
        }

        private static void Postfix(Crown __instance, bool value, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "crown", "held_by_foe_changed", __instance,
                data: RuntimeInteractionLogger.Fields(("value", value)), before: __state, after: RuntimeLog.CrownState(__instance));
    }

    [HarmonyPatch]
    internal static class RuntimeLogCrownOnEnablePatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(Crown), "OnEnable");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(Crown), "OnEnable");
        private static void Postfix(Crown __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "crown", "on_enable", __instance, after: RuntimeLog.CrownState(__instance));
    }

    [HarmonyPatch]
    internal static class RuntimeLogCrownOnDisablePatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(Crown), "OnDisable");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(Crown), "OnDisable");
        private static void Prefix(Crown __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "crown", "on_disable", __instance, before: RuntimeLog.CrownState(__instance));
    }

    [HarmonyPatch]
    internal static class RuntimeLogCrownOnDestroyPatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(Crown), "OnDestroy");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(Crown), "OnDestroy");
        private static void Prefix(Crown __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "crown", "on_destroy", __instance, before: RuntimeLog.CrownState(__instance));
    }

    [HarmonyPatch(typeof(Boar), nameof(Boar.Init))]
    internal static class RuntimeLogBoarInitPatch
    {
        private static void Postfix(Boar __instance, BoarSpawnGroup spawner)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "boar", "init", __instance, spawner,
                data: RuntimeInteractionLogger.Fields(("season", SafeSeason())), after: RuntimeLog.BoarState(__instance));

        private static string SafeSeason()
        {
            try { return Kingdom.Time.CurrentSeason.ToString(); }
            catch { return null; }
        }
    }

    [HarmonyPatch]
    internal static class RuntimeLogBoarDeathPatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(Boar), "HandleOnDeath");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(Boar), "HandleOnDeath");
        private static void Prefix(Boar __instance, GameObject killer)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "boar", "death", __instance, killer, before: RuntimeLog.BoarState(__instance));
    }

    [HarmonyPatch]
    internal static class RuntimeLogBoarDisablePatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(Boar), "OnDisable");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(Boar), "OnDisable");
        private static void Prefix(Boar __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "boar", "on_disable", __instance, before: RuntimeLog.BoarState(__instance));
    }

    [HarmonyPatch]
    internal static class RuntimeLogBoarDestroyPatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(Boar), "OnDestroy");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(Boar), "OnDestroy");
        private static void Prefix(Boar __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "boar", "on_destroy", __instance, before: RuntimeLog.BoarState(__instance));
    }

    [HarmonyPatch(typeof(BoarSpawnGroup), nameof(BoarSpawnGroup.ResetBoarTrigger))]
    internal static class RuntimeLogBoarResetPatch
    {
        private static void Prefix(BoarSpawnGroup __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "boar", "spawn_group_reset", __instance);
    }

    [HarmonyPatch]
    internal static class RuntimeLogBoarSpawnPatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(BoarSpawnGroup), "SpawnBoar");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(BoarSpawnGroup), "SpawnBoar");
        private static void Prefix(BoarSpawnGroup __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "boar", "spawn_boar_start", __instance);
        private static void Postfix(BoarSpawnGroup __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "boar", "spawn_boar_end", __instance);
    }

    [HarmonyPatch(typeof(Damageable), nameof(Damageable.ReceiveFatalDamage))]
    internal static class RuntimeLogFatalDamagePatch
    {
        private static void Prefix(Damageable __instance, GameObject damager, DamageSource source, out string __state)
        {
            __state = RuntimeLog.DamageableState(__instance);
            RuntimeLog.Event(RuntimeLogLevel.BugFocused, "damage", "fatal_start", __instance, damager,
                data: RuntimeInteractionLogger.Fields(("source", source)), before: __state);
        }

        private static void Postfix(Damageable __instance, GameObject damager, DamageSource source, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "damage", "fatal_end", __instance, damager,
                data: RuntimeInteractionLogger.Fields(("source", source)), before: __state, after: RuntimeLog.DamageableState(__instance));
    }

    [HarmonyPatch(typeof(Damageable), nameof(Damageable.ReceiveDamage), new[] { typeof(int), typeof(GameObject), typeof(DamageSource) })]
    internal static class RuntimeLogDamagePatch
    {
        private static void Prefix(Damageable __instance, int damageMultiplier, GameObject damager, DamageSource source, out string __state)
        {
            __state = RuntimeLog.DamageableState(__instance);
            RuntimeLog.Event(RuntimeLogLevel.BugFocused, "damage", "receive_start", __instance, damager,
                data: RuntimeInteractionLogger.Fields(("damageMultiplier", damageMultiplier), ("source", source)), before: __state);
        }

        private static void Postfix(Damageable __instance, int damageMultiplier, GameObject damager, DamageSource source, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "damage", "receive_end", __instance, damager,
                data: RuntimeInteractionLogger.Fields(("damageMultiplier", damageMultiplier), ("source", source)), before: __state, after: RuntimeLog.DamageableState(__instance));
    }

    [HarmonyPatch(typeof(Damageable), nameof(Damageable.ReceiveDamage), new[] { typeof(int), typeof(GameObject), typeof(DamageSource), typeof(Vector2) })]
    internal static class RuntimeLogDamageForcePatch
    {
        private static void Prefix(Damageable __instance, int damageMultiplier, GameObject damager, DamageSource source, Vector2 damageForce, out string __state)
        {
            __state = RuntimeLog.DamageableState(__instance);
            RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "damage", "receive_force_start", __instance, damager,
                data: RuntimeInteractionLogger.Fields(("damageMultiplier", damageMultiplier), ("source", source), ("force", $"{damageForce.x:0.###},{damageForce.y:0.###}")), before: __state);
        }

        private static void Postfix(Damageable __instance, int damageMultiplier, GameObject damager, DamageSource source, Vector2 damageForce, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "damage", "receive_force_end", __instance, damager,
                data: RuntimeInteractionLogger.Fields(("damageMultiplier", damageMultiplier), ("source", source), ("force", $"{damageForce.x:0.###},{damageForce.y:0.###}")), before: __state, after: RuntimeLog.DamageableState(__instance));
    }

    [HarmonyPatch(typeof(Droppable), "OnEnable")]
    internal static class RuntimeLogDroppableEnablePatch
    {
        private static void Postfix(Droppable __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "droppable", "on_enable", __instance, after: RuntimeLog.DroppableState(__instance));
    }

    [HarmonyPatch(typeof(Droppable), "OnDisable")]
    internal static class RuntimeLogDroppableDisablePatch
    {
        private static void Prefix(Droppable __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "droppable", "on_disable", __instance, before: RuntimeLog.DroppableState(__instance));
    }

    // No RuntimeLogDroppableDestroyPatch: Droppable never declares OnDestroy, so
    // patching it only yields "method missing" warnings. OnDisable (above) already
    // fires on the same teardown path, so the destroy hook is redundant anyway.

    [HarmonyPatch(typeof(DroppableCurrency), "OnEnable")]
    internal static class RuntimeLogDroppableCurrencyEnablePatch
    {
        private static void Postfix(DroppableCurrency __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "currency", "droppable_currency_enable", __instance,
                data: RuntimeInteractionLogger.Fields(("currencyType", SafeCurrency(__instance))), after: RuntimeLog.DroppableState(__instance));

        private static string SafeCurrency(DroppableCurrency currency)
        {
            try { return currency.CurrencyType.ToString(); }
            catch { return null; }
        }
    }

    [HarmonyPatch(typeof(Wallet), nameof(Wallet.RemoveCurrency))]
    internal static class RuntimeLogWalletRemovePatch
    {
        private static void Prefix(Wallet __instance, CurrencyType currencyType, int amountToRemove, out string __state)
        {
            __state = RuntimeLog.WalletState(__instance);
            RuntimeLog.Event(RuntimeLogLevel.BugFocused, "wallet", "remove_start", __instance,
                data: RuntimeInteractionLogger.Fields(("currencyType", currencyType), ("amount", amountToRemove)), before: __state);
        }

        private static void Postfix(Wallet __instance, CurrencyType currencyType, int amountToRemove, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "wallet", "remove_end", __instance,
                data: RuntimeInteractionLogger.Fields(("currencyType", currencyType), ("amount", amountToRemove)), before: __state, after: RuntimeLog.WalletState(__instance));
    }

    [HarmonyPatch(typeof(Wallet), nameof(Wallet.SetCurrency))]
    internal static class RuntimeLogWalletSetPatch
    {
        private static void Prefix(Wallet __instance, CurrencyType currencyType, int value, out string __state)
        {
            __state = RuntimeLog.WalletState(__instance);
            RuntimeLog.Event(RuntimeLogLevel.BugFocused, "wallet", "set_start", __instance,
                data: RuntimeInteractionLogger.Fields(("currencyType", currencyType), ("value", value)), before: __state);
        }

        private static void Postfix(Wallet __instance, CurrencyType currencyType, int value, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "wallet", "set_end", __instance,
                data: RuntimeInteractionLogger.Fields(("currencyType", currencyType), ("value", value)), before: __state, after: RuntimeLog.WalletState(__instance));
    }

    [HarmonyPatch(typeof(Wallet), nameof(Wallet.FastSetCurrency))]
    internal static class RuntimeLogWalletFastSetPatch
    {
        private static void Prefix(Wallet __instance, CurrencyType currencyType, int incCurrency, out string __state)
        {
            __state = RuntimeLog.WalletState(__instance);
            RuntimeLog.Event(RuntimeLogLevel.BugFocused, "wallet", "fast_set_start", __instance,
                data: RuntimeInteractionLogger.Fields(("currencyType", currencyType), ("increment", incCurrency)), before: __state);
        }

        private static void Postfix(Wallet __instance, CurrencyType currencyType, int incCurrency, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "wallet", "fast_set_end", __instance,
                data: RuntimeInteractionLogger.Fields(("currencyType", currencyType), ("increment", incCurrency)), before: __state, after: RuntimeLog.WalletState(__instance));
    }

    [HarmonyPatch(typeof(ConstructionBuildingComponent), nameof(ConstructionBuildingComponent.InitializeBuild))]
    internal static class RuntimeLogConstructionInitializePatch
    {
        private static void Prefix(ConstructionBuildingComponent __instance)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "construction", "initialize_build", __instance, before: RuntimeLog.ConstructionState(__instance));
    }

    [HarmonyPatch(typeof(ConstructionBuildingComponent), nameof(ConstructionBuildingComponent.IncrementBuild))]
    internal static class RuntimeLogConstructionIncrementPatch
    {
        private static void Prefix(ConstructionBuildingComponent __instance, int addPoints, out string __state)
        {
            __state = RuntimeLog.ConstructionState(__instance);
            RuntimeLog.Event(RuntimeLogLevel.BugFocused, "construction", "increment_start", __instance,
                data: RuntimeInteractionLogger.Fields(("addPoints", addPoints)), before: __state);
        }

        private static void Postfix(ConstructionBuildingComponent __instance, int addPoints, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "construction", "increment_end", __instance,
                data: RuntimeInteractionLogger.Fields(("addPoints", addPoints)), before: __state, after: RuntimeLog.ConstructionState(__instance));
    }

    [HarmonyPatch(typeof(ConstructionBuildingComponent), nameof(ConstructionBuildingComponent.ForceComplete))]
    internal static class RuntimeLogConstructionForceCompletePatch
    {
        private static void Prefix(ConstructionBuildingComponent __instance, out string __state)
        {
            __state = RuntimeLog.ConstructionState(__instance);
            RuntimeLog.Event(RuntimeLogLevel.BugFocused, "construction", "force_complete_start", __instance, before: __state);
        }

        private static void Postfix(ConstructionBuildingComponent __instance, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "construction", "force_complete_end", __instance, before: __state, after: RuntimeLog.ConstructionState(__instance));
    }

    [HarmonyPatch(typeof(Worker), nameof(Worker.AssignJob))]
    internal static class RuntimeLogWorkerAssignJobPatch
    {
        private static void Prefix(Worker __instance, Workable job)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "worker", "assign_job", __instance, job,
                data: RuntimeInteractionLogger.Fields(("playerId", SafePlayerId(__instance)), ("job", RuntimeLog.Obj(job))));

        private static int SafePlayerId(Worker worker)
        {
            try { return worker.PlayerId; }
            catch { return -1; }
        }
    }

    [HarmonyPatch(typeof(Worker), nameof(Worker.SetCurrentWork))]
    internal static class RuntimeLogWorkerSetCurrentWorkPatch
    {
        private static void Prefix(Worker __instance, Workable work)
            => RuntimeLog.Event(RuntimeLogLevel.BugFocused, "worker", "set_current_work", __instance, work,
                data: RuntimeInteractionLogger.Fields(("work", RuntimeLog.Obj(work))));
    }

    [HarmonyPatch(typeof(Worker), nameof(Worker.IsAvailable))]
    internal static class RuntimeLogWorkerAvailablePatch
    {
        private static void Postfix(Worker __instance, bool __result)
            => RuntimeLog.Event(RuntimeLogLevel.MaximumRaw, "worker", "is_available", __instance,
                data: RuntimeInteractionLogger.Fields(("result", __result)));
    }

    [HarmonyPatch]
    internal static class RuntimeLogCrownStealerPickupPatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(CrownStealer), "PickupLoot");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(CrownStealer), "PickupLoot");
        private static void Prefix(CrownStealer __instance, Droppable droppable)
            => RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "enemy", "crownstealer_pickup_loot", __instance, droppable, before: RuntimeLog.DroppableState(droppable));
    }

    [HarmonyPatch]
    internal static class RuntimeLogCrownStealerDropPatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(CrownStealer), "DropLoot");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(CrownStealer), "DropLoot");
        private static void Prefix(CrownStealer __instance)
            => RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "enemy", "crownstealer_drop_loot", __instance);
    }

    [HarmonyPatch]
    internal static class RuntimeLogCrownStealerDeathPatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(CrownStealer), "HandleOnDeath");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(CrownStealer), "HandleOnDeath");
        private static void Prefix(CrownStealer __instance, GameObject killer)
            => RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "enemy", "crownstealer_death", __instance, killer);
    }

    [HarmonyPatch]
    internal static class RuntimeLogPortalDeathPatch
    {
        private static bool Prepare() => RuntimeLog.HasOptionalMethod(typeof(Portal), "HandleOnDeath");
        private static MethodBase TargetMethod() => RuntimeLog.OptionalMethod(typeof(Portal), "HandleOnDeath");
        private static void Prefix(Portal __instance, GameObject killer)
            => RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "portal", "death", __instance, killer);
    }

    [HarmonyPatch(typeof(Player), "set_model")]
    internal static class RuntimeLogPlayerModelPatch
    {
        private static void Prefix(Player __instance, MonarchType value, out string __state)
        {
            __state = RuntimeLog.PlayerState(__instance);
        }

        private static void Postfix(Player __instance, MonarchType value, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "player", "model_changed", __instance,
                data: RuntimeInteractionLogger.Fields(("model", value)), before: __state, after: RuntimeLog.PlayerState(__instance));
    }

    [HarmonyPatch(typeof(Player), "set_equippedItemOfPower")]
    internal static class RuntimeLogPlayerItemPatch
    {
        private static void Prefix(Player __instance, ItemOfPower.ItemType value, out string __state)
        {
            __state = RuntimeLog.PlayerState(__instance);
        }

        private static void Postfix(Player __instance, ItemOfPower.ItemType value, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "player", "item_power_changed", __instance,
                data: RuntimeInteractionLogger.Fields(("item", value)), before: __state, after: RuntimeLog.PlayerState(__instance));
    }

    [HarmonyPatch(typeof(Player), "set_hasCrown")]
    internal static class RuntimeLogPlayerCrownStatePatch
    {
        private static void Prefix(Player __instance, bool value, out string __state)
        {
            __state = RuntimeLog.PlayerState(__instance);
        }

        private static void Postfix(Player __instance, bool value, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "player", "has_crown_changed", __instance,
                data: RuntimeInteractionLogger.Fields(("hasCrown", value)), before: __state, after: RuntimeLog.PlayerState(__instance));
    }

    [HarmonyPatch(typeof(StateMachine), nameof(StateMachine.GoToState), new[] { typeof(int) })]
    internal static class RuntimeLogStateMachineGoToStatePatch
    {
        private static void Prefix(StateMachine __instance, int state)
        {
            if (RuntimeInteractionLogger.Level == RuntimeLogLevel.EventHeavy && !RuntimeLog.RelevantStateMachineOwner(__instance)) return;
            RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "state_machine", "go_to_state", RuntimeLog.StateMachineOwner(__instance),
                data: RuntimeInteractionLogger.Fields(("previous", SafeCurrent(__instance)), ("next", state), ("manual", false)));
        }

        private static int SafeCurrent(StateMachine sm)
        {
            try { return sm.Current; }
            catch { return -1; }
        }
    }

    [HarmonyPatch(typeof(StateMachine), nameof(StateMachine.GoToState), new[] { typeof(int), typeof(bool) })]
    internal static class RuntimeLogStateMachineGoToStateManualPatch
    {
        private static void Prefix(StateMachine __instance, int state, bool isManualTransition)
        {
            if (RuntimeInteractionLogger.Level == RuntimeLogLevel.EventHeavy && !RuntimeLog.RelevantStateMachineOwner(__instance)) return;
            RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "state_machine", "go_to_state", RuntimeLog.StateMachineOwner(__instance),
                data: RuntimeInteractionLogger.Fields(("previous", SafeCurrent(__instance)), ("next", state), ("manual", isManualTransition)));
        }

        private static int SafeCurrent(StateMachine sm)
        {
            try { return sm.Current; }
            catch { return -1; }
        }
    }

    [HarmonyPatch(typeof(StateMachine), nameof(StateMachine.GoToAndUpdate), new[] { typeof(int) })]
    internal static class RuntimeLogStateMachineGoToAndUpdatePatch
    {
        private static void Prefix(StateMachine __instance, int state)
        {
            if (RuntimeInteractionLogger.Level == RuntimeLogLevel.EventHeavy && !RuntimeLog.RelevantStateMachineOwner(__instance)) return;
            RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "state_machine", "go_to_and_update", RuntimeLog.StateMachineOwner(__instance),
                data: RuntimeInteractionLogger.Fields(("previous", SafeCurrent(__instance)), ("next", state), ("manual", false)));
        }

        private static int SafeCurrent(StateMachine sm)
        {
            try { return sm.Current; }
            catch { return -1; }
        }
    }

    [HarmonyPatch(typeof(StateMachine), nameof(StateMachine.GoToAndUpdate), new[] { typeof(int), typeof(bool) })]
    internal static class RuntimeLogStateMachineGoToAndUpdateManualPatch
    {
        private static void Prefix(StateMachine __instance, int state, bool isManualTransition)
        {
            if (RuntimeInteractionLogger.Level == RuntimeLogLevel.EventHeavy && !RuntimeLog.RelevantStateMachineOwner(__instance)) return;
            RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "state_machine", "go_to_and_update", RuntimeLog.StateMachineOwner(__instance),
                data: RuntimeInteractionLogger.Fields(("previous", SafeCurrent(__instance)), ("next", state), ("manual", isManualTransition)));
        }

        private static int SafeCurrent(StateMachine sm)
        {
            try { return sm.Current; }
            catch { return -1; }
        }
    }

    [HarmonyPatch(typeof(Director), nameof(Director.OnLevelLoaded))]
    internal static class RuntimeLogDirectorLevelLoadedPatch
    {
        private static void Postfix(Director __instance)
            => RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "time", "level_loaded", __instance,
                data: RuntimeInteractionLogger.Fields(("daysInReign", SafeDays()), ("islandDays", SafeIslandDays()), ("season", SafeSeason())));

        private static int SafeDays()
        {
            try { return Kingdom.Time.DaysInReign; }
            catch { return -1; }
        }

        private static int SafeIslandDays()
        {
            try { return Kingdom.Time.IslandDays; }
            catch { return -1; }
        }

        private static string SafeSeason()
        {
            try { return Kingdom.Time.CurrentSeason.ToString(); }
            catch { return null; }
        }
    }

    [HarmonyPatch(typeof(Director), "set_CurrentSeason")]
    internal static class RuntimeLogDirectorSeasonPatch
    {
        private static void Prefix(Director __instance, Season value, out string __state)
        {
            try { __state = __instance.CurrentSeason.ToString(); }
            catch { __state = null; }
        }

        private static void Postfix(Director __instance, Season value, string __state)
            => RuntimeLog.Event(RuntimeLogLevel.EventHeavy, "time", "season_changed", __instance,
                data: RuntimeInteractionLogger.Fields(("previous", __state), ("current", value)));
    }
}

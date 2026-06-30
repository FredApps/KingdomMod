using System;
using Il2Cpp;
using UnityEngine;

namespace KingdomMod.Loader.Console
{
    internal static class NpcGiftSpawner
    {
        public static void SpawnBeggar(Player player, int playerNumber, Action<string> log)
        {
            var source = FindPrefabOrInstance<Beggar>();
            if (source == null)
            {
                log("No Beggar prefab/instance is loaded yet.");
                return;
            }

            var beggar = Spawn(source, player, 0, "Gifted Beggar");
            if (beggar == null) { log("Beggar spawn failed."); return; }
            try { KingdomMod.Internal.GameRefs.Kingdom?.AddBeggar(beggar); } catch { }
            log($"Spawned beggar beside Player {playerNumber}.");
        }

        public static void SpawnPeasant(Player player, int playerNumber, Action<string> log)
        {
            SpawnSimple<Peasant>("Peasant", player, playerNumber, log);
        }

        public static void SpawnBuilder(Player player, int playerNumber, Action<string> log)
        {
            SpawnSimple<Worker>("Builder", player, playerNumber, log);
        }

        public static void SpawnArcher(Player player, int playerNumber, Action<string> log)
        {
            SpawnSimple<Archer>("Archer", player, playerNumber, log);
        }

        public static void SpawnBerserker(Player player, int playerNumber, Action<string> log)
        {
            SpawnSimple<Berserker>("Berserker", player, playerNumber, log);
        }

        public static void SpawnHermit(Player player, int playerNumber, Hermit.HermitType type, Action<string> log)
        {
            var source = FindPrefabOrInstance<Hermit>(h => h != null && h.Type == type);
            if (source == null)
            {
                log($"No {type} hermit prefab/instance is loaded yet.");
                return;
            }

            var hermit = Spawn(source, player, 0, $"Gifted {type} Hermit");
            if (hermit == null) { log($"{type} hermit spawn failed."); return; }
            log($"Spawned {type} hermit beside Player {playerNumber}.");
        }

        public static void SpawnSquire(Player player, int playerNumber, Action<string> log)
        {
            SpawnKnightLike("Squire", player, playerNumber, preferSquire: true, fillCoins: false, log);
        }

        public static void SpawnKnight(Player player, int playerNumber, Action<string> log)
        {
            SpawnKnightLike("Knight", player, playerNumber, preferSquire: false, fillCoins: true, log);
        }

        public static void SpawnGhostParty(Player player, int playerNumber, Action<string> log)
        {
            var leaderSource = FindPrefabOrInstance<WarriorGhostLeader>();
            var ghostSource = FindPrefabOrInstance<WarriorGhost>();
            if (leaderSource == null || ghostSource == null)
            {
                log("Hel ghost prefabs are not loaded yet.");
                return;
            }

            var leader = Spawn(leaderSource, player, 0, "Gifted Warrior Ghost Leader");
            if (leader == null)
            {
                log("Ghost leader spawn failed.");
                return;
            }

            int facing = MonarchFacing(player);
            int duration = GhostDuration();

            // The archers' follow/charge AI asks their GhostHolder for a formation
            // slot (IGhostHolder.GetGhostFollowIndex) and the despawn calls back into
            // it (RemoveActiveGhost). The only IGhostHolder is HelsHead (the Hel item),
            // so borrow its prefab as the holder and register our units in its active
            // list; RemoveActiveGhost self-cleans the list when they expire.
            var holder = FindPrefabOrInstance<HelsHead>();

            RegisterWithHolder(holder, leader);

            // Join the leader and archers through the game's own formation path.
            // The Hel summon calls AddToFormation(ref spawnedLeader) starting from
            // null: the first unit (the leader) sets spawnedLeader to itself, then
            // each archer joins it. Seeding it with the leader breaks that handshake,
            // so start from null and let the leader establish the formation.
            WarriorGhostLeader formationLeader = null;
            try { leader.AddToFormation(ref formationLeader); } catch { }
            if (formationLeader == null) formationLeader = leader;

            try { leader.Summoner = player; } catch { }
            FaceUnit(leader, facing);
            StartDespawnTimer(leader, duration);

            int followers = 0;
            for (int i = 0; i < 4; i++)
            {
                var ghost = Spawn(ghostSource, player, i + 1, "Gifted Warrior Ghost");
                if (ghost == null) continue;
                RegisterWithHolder(holder, ghost);
                try { ghost.AddToFormation(ref formationLeader); } catch { }
                try { ghost.Summoner = player; } catch { }
                FaceUnit(ghost, facing);
                StartDespawnTimer(ghost, duration);
                followers++;
            }

            // Mirror the trophy summon: charge forward in the monarch's facing
            // direction. The leader's FSM runs Charge() while _shouldCharge is set
            // and drags its followers along.
            try { leader._shouldCharge = true; } catch { }

            log($"Spawned Hel ghost party beside Player {playerNumber}: 1 leader, {followers} ghosts.");
        }

        // Lifetime (seconds) the Hel item gives its summoned ghosts. Read from a
        // loaded HelsHead prefab so gifted ghosts despawn on the same timer as a
        // real summon; falls back to a sane default if the prefab isn't loaded.
        private static int GhostDuration()
        {
            try
            {
                var hel = FindPrefabOrInstance<HelsHead>();
                if (hel != null && hel._ghostDuration > 0) return hel._ghostDuration;
            }
            catch { }
            return 30;
        }

        // Point a ghost at a HelsHead acting as its IGhostHolder and add it to that
        // holder's active list, so the formation/follow AI can resolve a slot and the
        // despawn callback can deregister it.
        private static void RegisterWithHolder(HelsHead holder, HelsGhost ghost)
        {
            if (holder == null || ghost == null) return;
            try { ghost.GhostHolder = holder.TryCast<IGhostHolder>(); } catch { }
            try
            {
                if (holder._activeGhosts == null)
                    holder._activeGhosts = new Il2CppSystem.Collections.Generic.List<HelsGhost>();
                holder._activeGhosts.Add(ghost);
            }
            catch { }
        }

        // Start the same death countdown the trophy summon uses: set the lifetime
        // and let the ghost's own coroutine despawn it when it elapses.
        private static void StartDespawnTimer(HelsGhost ghost, int duration)
        {
            try
            {
                ghost.Duration = duration;
                ghost.StartDeathCountdown();
            }
            catch { }
        }

        private static int MonarchFacing(Player player)
        {
            try
            {
                var mover = player.mover ?? player.GetMover;
                if (mover != null)
                {
                    int dir = (int)mover.GetDirection();
                    if (dir != 0) return dir;
                }
            }
            catch { }
            return 1;
        }

        private static void FaceUnit(Component unit, int facing)
        {
            try
            {
                var mover = unit.GetComponentInChildren<Mover>();
                if (mover == null) return;
                mover.facingMode = (Mover.FacingMode)facing;
                mover.SetDirection(facing);
            }
            catch { }
        }

        private static void SpawnSimple<T>(string label, Player player, int playerNumber, Action<string> log) where T : Component
        {
            var source = FindPrefabOrInstance<T>();
            if (source == null)
            {
                log($"No {label} prefab/instance is loaded yet.");
                return;
            }

            var unit = Spawn(source, player, 0, $"Gifted {label}");
            if (unit == null) { log($"{label} spawn failed."); return; }
            log($"Spawned {label.ToLowerInvariant()} beside Player {playerNumber}.");
        }

        private static void SpawnKnightLike(string label, Player player, int playerNumber, bool preferSquire, bool fillCoins, Action<string> log)
        {
            var source = FindPrefabOrInstance<Knight>(k => MatchesKnightKind(k, preferSquire));
            source ??= FindPrefabOrInstance<Knight>();
            if (source == null)
            {
                log($"No {label} prefab/instance is loaded yet.");
                return;
            }

            var knight = Spawn(source, player, 0, $"Gifted {label}");
            if (knight == null) { log($"{label} spawn failed."); return; }

            try { knight.rank = preferSquire ? 0 : Math.Max(1, knight.rank); } catch { }
            if (fillCoins) FillKnightWallet(knight, log);
            log($"Spawned {label.ToLowerInvariant()} beside Player {playerNumber}.");
        }

        private static bool MatchesKnightKind(Knight knight, bool squire)
        {
            if (knight == null) return false;
            string name = SafeName(knight).ToLowerInvariant();
            if (squire)
                return name.Contains("squire") || name.Contains("shield");
            return name.Contains("knight") && !name.Contains("squire");
        }

        private static void FillKnightWallet(Knight knight, Action<string> log)
        {
            try
            {
                var wallet = knight.Wallet ?? knight.GetComponentInChildren<Wallet>();
                if (wallet == null)
                {
                    log("Knight spawned, but no wallet was found to fill.");
                    return;
                }
                int target = wallet.TotalCapacity > 0 ? wallet.TotalCapacity : Math.Max(wallet.Coins, 12);
                wallet.Coins = target;
            }
            catch (Exception e)
            {
                log($"Knight coin fill failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static T FindPrefabOrInstance<T>(Func<T, bool> predicate = null) where T : Component
        {
            T fallback = null;
            foreach (var item in Resources.FindObjectsOfTypeAll<T>())
            {
                if (item == null || item.gameObject == null) continue;
                if (predicate != null && !predicate(item)) continue;
                if (item.gameObject.scene.handle == 0) return item;
                fallback ??= item;
            }
            return fallback;
        }

        private static T Spawn<T>(T source, Player player, int offsetIndex, string name) where T : Component
        {
            if (source == null || player == null) return null;
            try
            {
                var instance = UnityEngine.Object.Instantiate(source, SpawnPosition(player, offsetIndex), Quaternion.identity);
                if (instance == null) return null;
                instance.name = name;
                instance.gameObject.SetActive(true);
                return instance;
            }
            catch
            {
                return null;
            }
        }

        private static Vector3 SpawnPosition(Player player, int offsetIndex)
        {
            float side = offsetIndex % 2 == 0 ? 1f : -1f;
            float distance = 1.25f + 0.35f * offsetIndex;
            return player.transform.position + new Vector3(side * distance, 0f, 0f);
        }

        private static string SafeName(Component component)
        {
            try { return component.name ?? ""; } catch { return ""; }
        }
    }
}

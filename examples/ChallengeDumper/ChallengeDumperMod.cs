// GameDataDumper (legacy name: ChallengeDumper) — F3 writes runtime JSON
// snapshots of ScriptableObject / MonoBehaviour data that is not extractable
// from a static IL2CPP dump.  The static dump shows field *names* but not
// the deserialised *values* — those only exist once Unity has loaded the
// .asset files into managed memory.  Free AssetRipper can't read MonoBehaviour
// values in an IL2CPP build without a paid TypeTree generator, so this mod
// fills the gap.
//
// Output: <MelonLoader>/UserData/KingdomMod/dump/
//   challenges.json     — ChallengeData (with real Condition[] contents)
//   steeds.json         — Steed prefabs (mount stats)
//   levelconfigs.json   — LevelConfig SOs (per-island balance)
//   biomes.json         — BiomeData SOs (biome composition)
//   npcs/hermits/powers/monarchs/buffs/campaigns/biome*.json
//   seasons.json and prefabs.json for broad runtime discovery
//
// Tip: open the relevant menu before pressing F3 — Unity only deserialises
// SOs once they're referenced.  Dumping from the main menu catches less than
// dumping mid-run.

using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

[assembly: MelonInfo(typeof(KingdomMod.Examples.ChallengeDumper.ChallengeDumperMod), "Game Data Dumper", "0.2.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.ChallengeDumper
{
    public sealed class ChallengeDumperMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            Kingdom.Mods.RegisterHotkey("F3", "Dump loaded game data to JSON");
            LoggerInstance.Msg("Game Data Dumper loaded. F3 dumps runtime JSON snapshots to UserData/KingdomMod/dump/.");
        }

        public override void OnUpdate()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F3))
                DumpAll();
        }

        private void DumpAll()
        {
            var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "KingdomMod", "dump");
            Directory.CreateDirectory(dir);

            Dump<ChallengeData>(dir, "challenges.json", WriteChallenge);
            Dump<Steed>        (dir, "steeds.json",     WriteSteed);
            Dump<LevelConfig>  (dir, "levelconfigs.json", WriteLevelConfig);
            Dump<BiomeData>    (dir, "biomes.json",     WriteBiome);
            DumpNpcs(dir);
            DumpHermits(dir);
            DumpPowers(dir);
            DumpMonarchs(dir);
            DumpBuffs(dir);
            Dump<CampaignData>       (dir, "campaigns.json",   WriteCampaign);
            Dump<BiomeSpecificAssets>(dir, "biomeassets.json", WriteBiomeSpecificAssets);
            Dump<BiomeSwapData>      (dir, "biomeswaps.json",  WriteBiomeSwap);
            DumpSeasons(dir);
            DumpPrefabs(dir);
        }

        private void Dump<T>(string dir, string filename, System.Action<JsonW, T> writeOne) where T : UnityEngine.Object
        {
            var all = Resources.FindObjectsOfTypeAll<T>();
            if (all == null || all.Length == 0)
            {
                LoggerInstance.Warning($"No {typeof(T).Name} instances in memory — skipping {filename}.");
                return;
            }
            var w = new JsonW();
            w.BeginArray();
            int written = 0;
            for (int i = 0; i < all.Length; i++)
            {
                T item = null;
                try { item = all[i]; } catch { }
                if (item == null) continue;
                try
                {
                    w.Comma(written > 0);
                    writeOne(w, item);
                    written++;
                }
                catch (System.Exception e)
                {
                    LoggerInstance.Warning($"  skipped {item.name}: {e.Message}");
                }
            }
            w.EndArray();

            string path;
            try
            {
                path = Path.GetFullPath(Path.Combine(dir, filename));
                File.WriteAllText(path, w.ToString());
            }
            catch (System.Exception e)
            {
                LoggerInstance.Warning($"  {filename}: write failed: {e.Message}");
                return;
            }
            LoggerInstance.Msg($"  {filename}: {written}/{all.Length} entries → {path}");
        }

        // ------------------------------------------------------------------
        // Per-type writers
        // ------------------------------------------------------------------

        private static void WriteChallenge(JsonW w, ChallengeData c)
        {
            w.BeginObject();
            w.Field("assetName", c.name);
            w.Field("id", c.id);
            w.Field("menuIndex", c.menuIndex);
            w.Field("challengeType", c.challengeType.ToString());
            w.Field("challengeState", c.challengeState.ToString());
            w.Field("isMultiplayer", c.isMultiplayer);
            w.Field("isSeasonalEvent", c.isSeasonalEvent);
            w.Field("isPrebuilt", c.isPrebuilt);
            w.Field("zombieMode", c.zombieMode);
            w.Field("includeHermits", c.includeHermits);
            w.Field("challengeSeed", c.challengeSeed);
            w.Field("dailyChallengeExpireDays", c.dailyChallengeExpireDays);
            w.Field("forceSelectBiomeIndex", c.forceSelectBiomeIndex);
            w.Field("startingCurrencyBagType", c.startingCurrencyBagType.ToString());
            w.Field("unlocksCrownOnCompletion", c.unlocksCrownOnCompletion.ToString());
            w.Field("unlockedTechAge", c.unlockedTechAge.ToString());
            w.Field("optionalBehaviours", ((int)c.optionalBehaviours).ToString());
            w.Field("customChallengeDataOptionsString", c.customChallengeDataOptionsString);

            w.Field("validBiomes", JoinStringList(c.validBiomes));
            w.FieldArray("p1DefaultSteed",          SteedArray(c.p1DefaultSteed));
            w.FieldArray("p2DefaultSteed",          SteedArray(c.p2DefaultSteed));
            w.FieldArray("startingPlayerModels",    MonarchArray(c.startingPlayerModels));
            w.FieldArray("startingPlayerItemOfPower", ItemOfPowerArray(c.startingPlayerItemOfPower));

            w.FieldArray("levelConfigs",     RefNames(c.levelConfigs));
            w.FieldArray("customSteeds",     RefNames(c.customSteeds));
            w.FieldArray("mapLandPrefabs",   RefNames(c.mapLandPrefabs));

            w.BeginNested("endConditions");                            WriteConditions(w, c.endConditions);                            w.EndNested(false);
            w.BeginNested("bronzeTierConditions");                     WriteConditions(w, c.bronzeTierConditions);                     w.EndNested(false);
            w.BeginNested("silverTierConditions");                     WriteConditions(w, c.silverTierConditions);                     w.EndNested(false);
            w.BeginNested("goldTierConditions");                       WriteConditions(w, c.goldTierConditions);                       w.EndNested(false);
            w.BeginNested("cursedTierConditions");                     WriteConditions(w, c.cursedTierConditions);                     w.EndNested(false);
            w.BeginNested("bronzeTierConditionsMultiplayerP1");        WriteConditions(w, c.bronzeTierConditionsMultiplayerP1);        w.EndNested(false);
            w.BeginNested("silverTierConditionsMultiplayerP1");        WriteConditions(w, c.silverTierConditionsMultiplayerP1);        w.EndNested(false);
            w.BeginNested("goldTierConditionsMultiplayerP1");          WriteConditions(w, c.goldTierConditionsMultiplayerP1);          w.EndNested(false);
            w.BeginNested("bronzeTierConditionsMultiplayerP2");        WriteConditions(w, c.bronzeTierConditionsMultiplayerP2);        w.EndNested(false);
            w.BeginNested("silverTierConditionsMultiplayerP2");        WriteConditions(w, c.silverTierConditionsMultiplayerP2);        w.EndNested(false);
            w.BeginNested("goldTierConditionsMultiplayerP2");          WriteConditions(w, c.goldTierConditionsMultiplayerP2);          w.EndNested(false);
            w.BeginNested("cursedTierConditionsMultiplayer");          WriteConditions(w, c.cursedTierConditionsMultiplayer);          w.EndNested(false);

            w.Field("customSwapData", RefName(c.customSwapData), isLast: true);
            w.EndObject();
        }

        private static void WriteConditions(JsonW w, Il2CppReferenceArray<Condition> arr)
        {
            w.BeginInlineArray();
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    w.Comma(i > 0);
                    var c = arr[i];
                    if (c == null) { w.Raw("null"); continue; }
                    w.BeginInlineObject();
                    w.InlineField("type",          c.type.ToString());
                    w.InlineField("stat",          c.stat.ToString());
                    w.InlineField("comparison",    c.comparison.ToString());
                    w.InlineField("requirement",   c.requirement);
                    w.InlineField("isTriggerStat", c.isTriggerStat, isLast: true);
                    w.EndInlineObject();
                }
            }
            w.EndInlineArray();
        }

        private static void WriteSteed(JsonW w, Steed s)
        {
            w.BeginObject();
            w.Field("assetName",            s.name);
            w.Field("steedType",            s.steedType.ToString());
            w.Field("walkSpeed",            s.walkSpeed);
            w.Field("runSpeed",             s.runSpeed);
            w.Field("forestSpeedMultiplier", s.forestSpeedMultiplier);
            w.Field("walkStaminaRate",      s.walkStaminaRate);
            w.Field("runStaminaRate",       s.runStaminaRate);
            w.Field("glideStaminaRate",     s.glideStaminaRate);
            w.Field("standStaminaRate",     s.standStaminaRate);
            w.Field("reserveStamina",       s.reserveStamina);
            w.Field("reserveProbability",   s.reserveProbability);
            w.Field("glidePayThreshold",    s.glidePayThreshold);
            w.Field("rearingDuration",      s.rearingDuration);
            w.Field("rearingCooldown",      s.rearingCooldown);
            w.Field("canEat",               s.canEat);
            w.Field("eatDelay",             s.eatDelay);
            w.Field("eatFullStaminaDelay",  s.eatFullStaminaDelay);
            w.Field("eatDuration",          s.eatDuration);
            w.Field("onlyEatsAtNight",      s.onlyEatsAtNight);
            w.Field("wellFedDuration",      s.wellFedDuration);
            w.Field("tiredDuration",        s.tiredDuration);
            w.Field("eatAmbientThreshold",  s.eatAmbientThreshold);
            w.Field("attractsDeer",         s.attractsDeer);
            w.Field("wanderRange",          s.wanderRange);
            w.Field("recolorToCoatOfArms",  s.recolorToCoatOfArms);
            w.Field("hasBirthAnim",         s.hasBirthAnim);
            w.Field("disableSteedSwitching", s.disableSteedSwitching);
            w.Field("resumesGallopingAfterUsingAbility", s.resumesGallopingAfterUsingAbility);
            w.Field("forwardAnimsToRuler",  s.forwardAnimsToRuler, isLast: true);
            w.EndObject();
        }

        private static void WriteLevelConfig(JsonW w, LevelConfig l)
        {
            w.BeginObject();
            w.Field("assetName",       l.name);
            w.Field("blockName",       l.blockName);
            w.Field("questType",       l.questType.ToString());
            w.Field("seasonChangeDays", l.seasonChangeDays);
            w.Field("islandMonumentID", l.islandMonumentID);
            w.Field("startingCoins",   l.startingCoins);
            w.Field("startingBeggars", l.startingBeggars);
            w.Field("startingPeasants", l.startingPeasants);
            w.Field("startingCoinsContinueOverride", l.startingCoinsContinueOverride);
            w.Field("startingGems",    l.startingGems);
            w.Field("incomeMultiplier", l.incomeMultiplier);
            w.Field("freeBoatParts",   l.freeBoatParts);
            w.Field("caveEscapeTimer", l.caveEscapeTimer);
            w.Field("shouldPlayVictoryMusicOnGreedDefeat", l.shouldPlayVictoryMusicOnGreedDefeat);
            w.Field("minLevelWidth",   l.minLevelWidth);
            w.Field("gemCount",        l.gemCount);
            w.Field("twoCliffs",       l.twoCliffs);
            w.Field("caveless",        l.caveless);
            w.Field("riverless",       l.riverless);
            w.Field("randomizeCliffSide", l.randomizeCliffSide);
            w.Field("sideDistributionBias", l.SideDistributionBias);
            w.Field("levelBiomeOverrides", RefName(l.levelBiomeOverrides));
            w.Field("landCycleData",      RefName(l.landCycleData));
            w.Field("alternateLandCycleData", RefName(l.alternateLandCycleData), isLast: true);
            w.EndObject();
        }

        private static void WriteBiome(JsonW w, BiomeData b)
        {
            w.BeginObject();
            w.Field("assetName",        b.name);
            w.Field("blockName",        b.blockName);
            w.Field("baseData",         RefName(b.baseData));
            w.Field("swapData",         RefName(b.swapData));
            w.Field("maxIslands",       b.MaxIslands);
            w.Field("bossDayCount",     b.bossDays != null ? b.bossDays.Count : 0);
            w.Field("recoveryDayCount", b.recoveryDays != null ? b.recoveryDays.Count : 0);
            w.Field("regularDayCount",  b.regularDays != null ? b.regularDays.Count : 0);
            w.FieldArray("levelConfigs", RefNames(b.levelConfigs));
            w.Field("campaignData",     RefName(b.campaignData), isLast: true);
            w.EndObject();
        }

        private void DumpNpcs(string dir)
        {
            var w = new JsonW();
            w.BeginArray();
            int written = 0;
            written = DumpComponentGroup<Beggar>(w, "Beggar", written, WriteBeggar);
            written = DumpComponentGroup<Peasant>(w, "Peasant", written, WritePeasant);
            written = DumpComponentGroup<Worker>(w, "Builder", written, WriteWorker);
            written = DumpComponentGroup<Archer>(w, "Archer", written, WriteArcher);
            written = DumpComponentGroup<Knight>(w, "Knight", written, WriteKnight);
            written = DumpComponentGroup<Berserker>(w, "Berserker", written, WriteBerserker);
            written = DumpComponentGroup<WarriorGhostLeader>(w, "WarriorGhostLeader", written, WriteWarriorGhostLeader);
            written = DumpComponentGroup<WarriorGhost>(w, "WarriorGhost", written, WriteWarriorGhost);
            w.EndArray();
            WriteJson(dir, "npcs.json", w, written);
        }

        private void DumpHermits(string dir)
        {
            var w = new JsonW();
            w.BeginArray();
            int written = DumpComponentGroup<Hermit>(w, "Hermit", 0, WriteHermit);
            w.EndArray();
            WriteJson(dir, "hermits.json", w, written);
        }

        private void DumpPowers(string dir)
        {
            var w = new JsonW();
            w.BeginArray();
            int written = 0;
            written = DumpComponentGroup<ItemOfPower>(w, "ItemOfPower", written, WriteItemOfPower);
            written = DumpComponentGroup<ItemOfPowerReward>(w, "ItemOfPowerReward", written, WriteItemOfPowerReward);
            written = DumpComponentGroup<HelsHead>(w, "HelsHead", written, WriteHelsHead);
            written = DumpComponentGroup<Player>(w, "PlayerPowerState", written, WritePlayerPowerState);
            w.EndArray();
            WriteJson(dir, "powers.json", w, written);
        }

        private void DumpMonarchs(string dir)
        {
            var w = new JsonW();
            w.BeginArray();
            int written = 0;
            for (int i = 0; i < (int)MonarchType.Total; i++)
            {
                w.Comma(written > 0);
                var monarch = (MonarchType)i;
                w.BeginObject();
                w.Field("recordKind", "MonarchType");
                w.Field("value", i);
                w.Field("name", monarch.ToString(), isLast: true);
                w.EndObject();
                written++;
            }

            written = DumpComponentGroup<Player>(w, "PlayerMonarchState", written, WritePlayerMonarchState);
            written = DumpScriptableObjectGroup<BiomeSpecificAssets>(w, "BiomeSpecificAssets", written, WriteBiomeSpecificAssetsMonarchs);
            w.EndArray();
            WriteJson(dir, "monarchs.json", w, written);
        }

        private void DumpBuffs(string dir)
        {
            var w = new JsonW();
            w.BeginArray();
            int written = 0;
            written = DumpScriptableObjectGroup<BuffData>(w, "BuffData", written, WriteBuffDataEntry);
            written = DumpComponentGroup<Damageable>(w, "Damageable", written, WriteDamageableEntry);
            w.EndArray();
            WriteJson(dir, "buffs.json", w, written);
        }

        private void DumpSeasons(string dir)
        {
            var w = new JsonW();
            w.BeginArray();
            int written = 0;
            written = DumpScriptableObjectGroup<Day>(w, "Day", written, WriteDay);
            w.EndArray();
            WriteJson(dir, "seasons.json", w, written);
        }

        private void DumpPrefabs(string dir)
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            var w = new JsonW();
            w.BeginArray();
            int written = 0;
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    GameObject go = null;
                    try { go = all[i]; } catch { }
                    if (go == null) continue;
                    try
                    {
                        w.Comma(written > 0);
                        w.BeginObject();
                        WriteGameObjectFields(w, "GameObject", go);
                        w.Field("componentCount", ComponentCount(go), isLast: true);
                        w.EndObject();
                        written++;
                    }
                    catch (System.Exception e)
                    {
                        LoggerInstance.Warning($"  skipped GameObject {SafeObjectName(go)}: {e.Message}");
                    }
                }
            }
            w.EndArray();
            WriteJson(dir, "prefabs.json", w, written);
        }

        private int DumpComponentGroup<T>(JsonW w, string kind, int written, System.Action<JsonW, string, T> writeOne) where T : Component
        {
            var all = Resources.FindObjectsOfTypeAll<T>();
            if (all == null || all.Length == 0)
            {
                LoggerInstance.Warning($"No {typeof(T).Name} instances in memory.");
                return written;
            }

            for (int i = 0; i < all.Length; i++)
            {
                T item = null;
                try { item = all[i]; } catch { }
                if (item == null) continue;
                try
                {
                    w.Comma(written > 0);
                    writeOne(w, kind, item);
                    written++;
                }
                catch (System.Exception e)
                {
                    LoggerInstance.Warning($"  skipped {kind} {SafeObjectName(item)}: {e.Message}");
                }
            }

            return written;
        }

        private int DumpScriptableObjectGroup<T>(JsonW w, string kind, int written, System.Action<JsonW, string, T> writeOne) where T : ScriptableObject
        {
            var all = Resources.FindObjectsOfTypeAll<T>();
            if (all == null || all.Length == 0)
            {
                LoggerInstance.Warning($"No {typeof(T).Name} instances in memory.");
                return written;
            }

            for (int i = 0; i < all.Length; i++)
            {
                T item = null;
                try { item = all[i]; } catch { }
                if (item == null) continue;
                try
                {
                    w.Comma(written > 0);
                    writeOne(w, kind, item);
                    written++;
                }
                catch (System.Exception e)
                {
                    LoggerInstance.Warning($"  skipped {kind} {SafeObjectName(item)}: {e.Message}");
                }
            }

            return written;
        }

        private void WriteJson(string dir, string filename, JsonW w, int written)
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(dir, filename));
                File.WriteAllText(path, w.ToString());
                LoggerInstance.Msg($"  {filename}: {written} entries -> {path}");
            }
            catch (System.Exception e)
            {
                LoggerInstance.Warning($"  {filename}: write failed: {e.Message}");
            }
        }

        private static void WriteBeggar(JsonW w, string kind, Beggar b)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, b);
            w.Field("playerId", Safe(() => b.PlayerId));
            w.Field("despawnOnLoad", Safe(() => b.DespawnOnLoad));
            w.Field("hasFoundBaker", Safe(() => b.hasFoundBaker), isLast: true);
            w.EndObject();
        }

        private static void WritePeasant(JsonW w, string kind, Peasant p)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, p);
            w.Field("hasBuffable", Safe(() => p.Buffable != null), isLast: true);
            w.EndObject();
        }

        private static void WriteWorker(JsonW w, string kind, Worker worker)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, worker);
            w.Field("playerId", Safe(() => worker.PlayerId));
            w.Field("despawnOnLoad", Safe(() => worker.DespawnOnLoad));
            w.Field("wallet", RefName(Safe(() => worker.Wallet)));
            w.Field("canEmbark", Safe(() => worker.CanEmbark));
            w.Field("unitType", Safe(() => worker.UnitType.ToString()), isLast: true);
            w.EndObject();
        }

        private static void WriteArcher(JsonW w, string kind, Archer archer)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, archer);
            w.Field("playerId", Safe(() => archer.PlayerId));
            w.Field("despawnOnLoad", Safe(() => archer.DespawnOnLoad));
            w.Field("harmless", archer.harmless);
            w.Field("shootRange", archer.shootRange);
            w.Field("towerShootRange", archer.towerShootRange, isLast: true);
            w.EndObject();
        }

        private static void WriteKnight(JsonW w, string kind, Knight knight)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, knight);
            w.Field("playerId", Safe(() => knight.PlayerId));
            w.Field("despawnOnLoad", Safe(() => knight.DespawnOnLoad));
            w.Field("rank", knight.rank);
            w.Field("numArchers", Safe(() => knight.numArchers));
            w.Field("needsArmor", Safe(() => knight.NeedsArmor));
            w.Field("isRetreating", Safe(() => knight.isRetreating));
            w.Field("isCharging", Safe(() => knight.isCharging));
            w.Field("side", Safe(() => knight.side.ToString()));
            w.Field("wallet", RefName(Safe(() => knight.Wallet)), isLast: true);
            w.EndObject();
        }

        private static void WriteBerserker(JsonW w, string kind, Berserker berserker)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, berserker);
            w.Field("playerId", Safe(() => berserker.PlayerId));
            w.Field("despawnOnLoad", Safe(() => berserker.DespawnOnLoad));
            w.Field("hasBuffable", Safe(() => berserker.Buffable != null), isLast: true);
            w.EndObject();
        }

        private static void WriteWarriorGhostLeader(JsonW w, string kind, WarriorGhostLeader leader)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, leader);
            w.Field("statueBuffActive", Safe(() => leader.statueBuffActive));
            w.Field("hasBuffable", Safe(() => leader.Buffable != null), isLast: true);
            w.EndObject();
        }

        private static void WriteWarriorGhost(JsonW w, string kind, WarriorGhost ghost)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, ghost);
            w.Field("isAvailable", Safe(() => ghost.isAvailable));
            w.Field("hasBuffable", Safe(() => ghost.Buffable != null), isLast: true);
            w.EndObject();
        }

        private static void WriteHermit(JsonW w, string kind, Hermit hermit)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, hermit);
            w.Field("hermitType", Safe(() => hermit.Type.ToString()));
            w.Field("canSailAway", Safe(() => hermit.CanSailAway));
            w.Field("lostOnCrownLost", Safe(() => hermit.LostOnCrownLost));
            w.Field("hasBuffable", Safe(() => hermit.Buffable != null), isLast: true);
            w.EndObject();
        }

        private static void WriteItemOfPower(JsonW w, string kind, ItemOfPower item)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, item);
            w.Field("itemType", item.itemType.ToString());
            w.Field("itemCooldown", Safe(() => item.ItemCooldown));
            w.Field("isChanneledAbility", item.IsChanneledAbility);
            w.Field("canActivate", Safe(() => item.CanActivate()), isLast: true);
            w.EndObject();
        }

        private static void WriteItemOfPowerReward(JsonW w, string kind, ItemOfPowerReward reward)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, reward);
            w.Field("canBePaidByLoadedPlayer", CanAnyLoadedPlayerPay(reward), isLast: true);
            w.EndObject();
        }

        private static void WriteHelsHead(JsonW w, string kind, HelsHead head)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, head);
            w.Field("itemType", head.itemType.ToString());
            w.Field("itemCooldown", Safe(() => head.ItemCooldown));
            w.Field("isChanneledAbility", head.IsChanneledAbility);
            w.Field("canActivate", Safe(() => head.CanActivate()), isLast: true);
            w.EndObject();
        }

        private static void WritePlayerPowerState(JsonW w, string kind, Player player)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, player);
            w.Field("playerId", player.playerId);
            w.Field("equippedItemOfPower", Safe(() => player.equippedItemOfPower.ToString()));
            w.Field("model", Safe(() => player.model.ToString()));
            w.Field("hasCrown", Safe(() => player.hasCrown));
            w.Field("coins", Safe(() => player.coins));
            w.Field("gems", Safe(() => player.gems), isLast: true);
            w.EndObject();
        }

        private static void WritePlayerMonarchState(JsonW w, string kind, Player player)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, player);
            w.Field("playerId", player.playerId);
            w.Field("model", Safe(() => player.model.ToString()));
            w.Field("hat", Safe(() => player.hat.ToString()));
            w.Field("skinColor", Safe(() => ColorString(player.skinColor)));
            w.Field("steedType", Safe(() => player.steed != null ? player.steed.steedType.ToString() : null));
            w.Field("equippedItemOfPower", Safe(() => player.equippedItemOfPower.ToString()), isLast: true);
            w.EndObject();
        }

        private static void WriteBuffDataEntry(JsonW w, string kind, BuffData buff)
        {
            w.BeginObject();
            WriteScriptableObjectFields(w, kind, buff);
            w.Field("id", buff.ID);
            w.Field("buffType", buff.BuffType.ToString());
            w.Field("effectDuration", buff.EffectDuration);
            w.Field("overlayColor", ColorString(buff.OverlayColor), isLast: true);
            w.EndObject();
        }

        private static void WriteDamageableEntry(JsonW w, string kind, Damageable damageable)
        {
            w.BeginObject();
            WriteComponentFields(w, kind, damageable);
            w.Field("invulnerable", Safe(() => damageable.invulnerable));
            w.Field("isDead", Safe(() => damageable.isDead));
            w.Field("useHitPoints", damageable.useHitPoints);
            w.Field("hitPoints", Safe(() => damageable.hitPoints));
            w.Field("initialHitPoints", damageable.initialHitPoints);
            w.Field("damagedBy", damageable.damagedBy.ToString());
            w.Field("ignoredWhenInvulnerable", damageable.ignoredWhenInvulnerable, isLast: true);
            w.EndObject();
        }

        private static void WriteCampaign(JsonW w, CampaignData campaign)
        {
            w.BeginObject();
            WriteScriptableObjectFields(w, "CampaignData", campaign);
            w.Field("mainMap", RefName(campaign.mainMap));
            w.FieldArray("mapLandPrefabs", RefNames(campaign.mapLandPrefabs), isLast: true);
            w.EndObject();
        }

        private static void WriteBiomeSpecificAssets(JsonW w, BiomeSpecificAssets assets)
        {
            w.BeginObject();
            WriteScriptableObjectFields(w, "BiomeSpecificAssets", assets);
            w.FieldArray("rulerPortraits", RefNames(assets.rulerPortraits));
            w.FieldArray("uniquePrefabMasterCopies", RefNames(assets.uniquePrefabMasterCopies));
            w.FieldArray("uniqueShopPrefabs", RefNames(assets.uniqueShopPrefabs));
            w.FieldArray("uniqueCharacters", RefNames(assets.uniqueCharacters));
            w.FieldArray("uniqueScatterableData", RefNames(assets.uniqueScatterableData));
            w.FieldArray("biomeSteeds", RefNames(assets.biomeSteeds));
            w.FieldArray("itemsOfPower", RefNames(assets.itemsOfPower));
            w.Field("biomeBanner", RefName(assets.biomeBanner));
            w.Field("biomeBannerTorn", RefName(assets.biomeBannerTorn));
            w.Field("fleetBoatPrefab", RefName(assets.fleetBoatPrefab), isLast: true);
            w.EndObject();
        }

        private static void WriteBiomeSpecificAssetsMonarchs(JsonW w, string kind, BiomeSpecificAssets assets)
        {
            w.BeginObject();
            WriteScriptableObjectFields(w, kind, assets);
            w.FieldArray("rulerPortraits", RefNames(assets.rulerPortraits));
            w.Field("rulerPortraitTypePairCount", Safe(() => assets.rulerPortraitTypePairs != null ? assets.rulerPortraitTypePairs.Count : 0), isLast: true);
            w.EndObject();
        }

        private static void WriteBiomeSwap(JsonW w, BiomeSwapData swap)
        {
            w.BeginObject();
            WriteScriptableObjectFields(w, "BiomeSwapData", swap);
            w.Field("assetNameField", swap.assetName);
            w.Field("prefabSwapCount", CountOf(swap.prefabSwapPool));
            w.Field("spriteSwapCount", CountOf(swap.spriteSwapPool));
            w.Field("animatorSwapCount", CountOf(swap.animatorSwapPool));
            w.Field("scriptableObjectSwapCount", CountOf(swap.scriptableObjectSwapPool));
            w.Field("poolSwapCount", CountOf(swap.poolSwapData), isLast: true);
            w.EndObject();
        }

        private static void WriteDay(JsonW w, string kind, Day day)
        {
            w.BeginObject();
            WriteScriptableObjectFields(w, kind, day);
            w.Field("seasonOptions", day.seasonOptions.ToString());
            w.Field("weatherOptions", day.weatherOptions.ToString());
            w.Field("biomeExclusive", day.biomeExlusive);
            w.Field("shouldBeInRotation", day.ShouldBeInRotation);
            w.Field("trackCount", CountOf(day.tracks));
            w.Field("notes", day.notes, isLast: true);
            w.EndObject();
        }

        // ------------------------------------------------------------------
        // Conversion helpers
        // ------------------------------------------------------------------

        private static void WriteComponentFields(JsonW w, string kind, Component c)
        {
            var go = c != null ? c.gameObject : null;
            WriteGameObjectFields(w, kind, go);
            w.Field("componentType", c != null ? c.GetIl2CppType().FullName : null);
        }

        private static void WriteGameObjectFields(JsonW w, string kind, GameObject go)
        {
            w.Field("recordKind", kind);
            w.Field("assetName", SafeObjectName(go));
            w.Field("isPrefab", IsPrefab(go));
            w.Field("activeSelf", go != null && Safe(() => go.activeSelf));
            w.Field("activeInHierarchy", go != null && Safe(() => go.activeInHierarchy));
            w.Field("sceneHandle", SceneHandle(go));
            w.Field("sceneName", SceneName(go));
            w.Field("path", TransformPath(go));
            w.Field("position", PositionString(go));
            w.FieldArray("components", ComponentNames(go));
        }

        private static void WriteScriptableObjectFields(JsonW w, string kind, ScriptableObject obj)
        {
            w.Field("recordKind", kind);
            w.Field("assetName", SafeObjectName(obj));
            w.Field("il2cppType", obj != null ? obj.GetIl2CppType().FullName : null);
        }

        private static T Safe<T>(System.Func<T> get)
        {
            try { return get(); }
            catch { return default; }
        }

        private static bool CanAnyLoadedPlayerPay(ItemOfPowerReward reward)
        {
            if (reward == null) return false;
            var players = Resources.FindObjectsOfTypeAll<Player>();
            if (players == null) return false;
            for (int i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null) continue;
                if (Safe(() => reward.CanPay(player))) return true;
            }

            return false;
        }

        private static bool IsPrefab(GameObject go)
        {
            if (go == null) return false;
            return Safe(() => go.scene.handle == 0);
        }

        private static int SceneHandle(GameObject go)
        {
            return go == null ? -1 : Safe(() => go.scene.handle);
        }

        private static string SceneName(GameObject go)
        {
            return go == null ? null : Safe(() => go.scene.name);
        }

        private static string PositionString(GameObject go)
        {
            if (go == null) return null;
            return Safe(() => Vector3String(go.transform.position));
        }

        private static string Vector3String(Vector3 v)
        {
            return v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ","
                + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ","
                + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string ColorString(Color c)
        {
            return c.r.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ","
                + c.g.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ","
                + c.b.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ","
                + c.a.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string TransformPath(GameObject go)
        {
            if (go == null) return null;
            return Safe(() =>
            {
                var t = go.transform;
                var path = SafeObjectName(go);
                while (t != null && t.parent != null)
                {
                    t = t.parent;
                    path = SafeObjectName(t.gameObject) + "/" + path;
                }

                return path;
            });
        }

        private static int ComponentCount(GameObject go)
        {
            var names = ComponentNames(go);
            return names.Count;
        }

        private static IList<string> ComponentNames(GameObject go)
        {
            var names = new List<string>();
            if (go == null) return names;
            var components = Safe(() => go.GetComponents<Component>());
            if (components == null) return names;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = null;
                try { component = components[i]; } catch { }
                if (component == null) continue;
                names.Add(component.GetIl2CppType().Name);
            }

            return names;
        }

        private static int CountOf<T>(Il2CppSystem.Collections.Generic.List<T> list)
        {
            return list != null ? list.Count : 0;
        }

        private static string SafeObjectName(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            return Safe(() => obj.name) ?? Safe(() => obj.GetIl2CppType().Name);
        }

        private static string JoinStringList(Il2CppSystem.Collections.Generic.List<string> list)
        {
            if (list == null) return "";
            var parts = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++) parts.Add(list[i]);
            return string.Join(", ", parts);
        }

        private static IList<string> SteedArray(Il2CppStructArray<SteedType> arr)
        {
            var r = new List<string>();
            if (arr == null) return r;
            for (int i = 0; i < arr.Length; i++) r.Add(arr[i].ToString());
            return r;
        }

        private static IList<string> MonarchArray(Il2CppStructArray<MonarchType> arr)
        {
            var r = new List<string>();
            if (arr == null) return r;
            for (int i = 0; i < arr.Length; i++) r.Add(arr[i].ToString());
            return r;
        }

        private static IList<string> ItemOfPowerArray(Il2CppStructArray<ItemOfPower.ItemType> arr)
        {
            var r = new List<string>();
            if (arr == null) return r;
            for (int i = 0; i < arr.Length; i++) r.Add(arr[i].ToString());
            return r;
        }

        private static IList<string> RefNames<T>(Il2CppReferenceArray<T> arr) where T : Il2CppSystem.Object
        {
            var r = new List<string>();
            if (arr == null) return r;
            for (int i = 0; i < arr.Length; i++) r.Add(RefName(arr[i] as UnityEngine.Object));
            return r;
        }

        private static IList<string> RefNames<T>(Il2CppSystem.Collections.Generic.List<T> list) where T : Il2CppSystem.Object
        {
            var r = new List<string>();
            if (list == null) return r;
            for (int i = 0; i < list.Count; i++) r.Add(RefName(list[i] as UnityEngine.Object));
            return r;
        }

        private static string RefName(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            return obj.name ?? obj.GetIl2CppType().Name;
        }

        // ------------------------------------------------------------------
        // Tiny indented JSON writer. Hand-rolled so we control exactly how
        // Il2Cpp arrays/lists/PPtrs flatten and so we don't drag a serializer
        // in just for this mod.
        // ------------------------------------------------------------------

        private sealed class JsonW
        {
            private readonly StringBuilder _sb = new(64 * 1024);
            private int _depth;

            public override string ToString() => _sb.ToString();

            public void BeginArray()  { _sb.Append("[\n"); _depth++; }
            public void EndArray()    { _depth--; _sb.Append("\n").Append(Indent()).Append(']'); }
            public void BeginObject() { _sb.Append(Indent()).Append("{\n"); _depth++; }
            public void EndObject()   { _depth--; _sb.Append("\n").Append(Indent()).Append('}'); }
            public void Comma(bool emit) { if (emit) _sb.Append(",\n"); }

            public void Raw(string s) { _sb.Append(s); }

            public void Field(string key, string v, bool isLast = false)  => WriteLine(key, JsonStr(v), isLast);
            public void Field(string key, int v,    bool isLast = false)  => WriteLine(key, v.ToString(System.Globalization.CultureInfo.InvariantCulture), isLast);
            public void Field(string key, float v,  bool isLast = false)  => WriteLine(key, v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), isLast);
            public void Field(string key, bool v,   bool isLast = false)  => WriteLine(key, v ? "true" : "false", isLast);

            public void FieldArray(string key, IList<string> items, bool isLast = false)
            {
                _sb.Append(Indent()).Append('"').Append(key).Append("\": [");
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) _sb.Append(", ");
                    _sb.Append(JsonStr(items[i]));
                }
                _sb.Append(isLast ? "]\n" : "],\n");
            }

            public void BeginNested(string key)
            {
                _sb.Append(Indent()).Append('"').Append(key).Append("\": ");
            }

            public void EndNested(bool isLast)
            {
                _sb.Append(isLast ? "\n" : ",\n");
            }

            public void BeginInlineArray()  { _sb.Append('['); }
            public void EndInlineArray()    { _sb.Append(']'); }
            public void BeginInlineObject() { _sb.Append('{'); }
            public void EndInlineObject()   { _sb.Append('}'); }

            public void InlineField(string key, string v, bool isLast = false)
                => InlineWrite(key, JsonStr(v), isLast);
            public void InlineField(string key, float v,  bool isLast = false)
                => InlineWrite(key, v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), isLast);
            public void InlineField(string key, bool v,   bool isLast = false)
                => InlineWrite(key, v ? "true" : "false", isLast);

            private void InlineWrite(string key, string rawValue, bool isLast)
            {
                _sb.Append('"').Append(key).Append("\":").Append(rawValue);
                if (!isLast) _sb.Append(',');
            }

            private void WriteLine(string key, string rawValue, bool isLast)
            {
                _sb.Append(Indent()).Append('"').Append(key).Append("\": ").Append(rawValue);
                _sb.Append(isLast ? "\n" : ",\n");
            }

            private string Indent() => new(' ', _depth * 2);

            private static string JsonStr(string s)
            {
                if (s == null) return "null";
                var b = new StringBuilder(s.Length + 2);
                b.Append('"');
                foreach (var ch in s)
                {
                    switch (ch)
                    {
                        case '\\': b.Append("\\\\"); break;
                        case '"':  b.Append("\\\""); break;
                        case '\n': b.Append("\\n");  break;
                        case '\r': b.Append("\\r");  break;
                        case '\t': b.Append("\\t");  break;
                        default:
                            if (ch < 0x20) b.Append("\\u").Append(((int)ch).ToString("x4"));
                            else b.Append(ch);
                            break;
                    }
                }
                b.Append('"');
                return b.ToString();
            }
        }
    }
}

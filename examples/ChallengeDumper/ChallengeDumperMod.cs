// GameDataDumper (legacy name: ChallengeDumper) — F3 writes runtime JSON
// snapshots of ScriptableObject / MonoBehaviour data that is not extractable
// from a static IL2CPP dump.  The static dump shows field *names* but not
// the deserialised *values* — those only exist once Unity has loaded the
// .asset files into managed memory.  Free AssetRipper can't read MonoBehaviour
// values in an IL2CPP build without a paid TypeTree generator, so this mod
// fills the gap.
//
// Output: <MelonLoader>/UserData/KingdomMod/dump/
//   challenges.json     — ChallengeData (now with real Condition[] contents)
//   steeds.json         — Steed prefabs (mount stats)
//   levelconfigs.json   — LevelConfig SOs (per-island balance)
//   biomes.json         — BiomeData SOs (biome composition)
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
            Kingdom.Mods.RegisterHotkey("F3", "Dump challenges / steeds / level configs / biomes to JSON");
            LoggerInstance.Msg("Game Data Dumper loaded. F3 dumps challenges/steeds/level configs/biomes to UserData/KingdomMod/dump/.");
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

        // ------------------------------------------------------------------
        // Conversion helpers
        // ------------------------------------------------------------------

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

            public void FieldArray(string key, IList<string> items)
            {
                _sb.Append(Indent()).Append('"').Append(key).Append("\": [");
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) _sb.Append(", ");
                    _sb.Append(JsonStr(items[i]));
                }
                _sb.Append("],\n");
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

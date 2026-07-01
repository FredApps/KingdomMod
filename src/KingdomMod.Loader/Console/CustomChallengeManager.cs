using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader.Utils;
using UnityEngine;

namespace KingdomMod.Loader.Console
{
    internal sealed class CustomChallengeManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly List<CustomChallengeDesign> _designs = new();
        private ChallengeData _activeChallenge;
        private readonly List<LevelConfig> _activeLevelConfigs = new();
        private string _activeName = "";

        public static CustomChallengeManager Instance { get; } = new CustomChallengeManager();
        public IReadOnlyList<CustomChallengeDesign> Designs => _designs;
        public string ActiveName => _activeName;
        public string Folder => Path.Combine(MelonEnvironment.UserDataDirectory, "KingdomMod", "custom-challenges");

        public int Refresh(Action<string> log)
        {
            _designs.Clear();
            Directory.CreateDirectory(Folder);
            SeedSampleIfEmpty();

            foreach (var path in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    var design = JsonSerializer.Deserialize<CustomChallengeDesign>(File.ReadAllText(path), JsonOptions);
                    if (design == null) continue;
                    design.SourcePath = path;
                    design.Normalize();
                    _designs.Add(design);
                }
                catch (Exception e)
                {
                    log?.Invoke($"Challenge import skipped {Path.GetFileName(path)}: {e.Message}");
                }
            }

            _designs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            log?.Invoke($"Custom challenges: {_designs.Count} imported from {Folder}.");
            return _designs.Count;
        }

        public bool Apply(int index, Action<string> log)
        {
            if (index < 0 || index >= _designs.Count)
            {
                log?.Invoke("Custom challenge: selection is no longer valid; refresh and try again.");
                return false;
            }

            var design = _designs[index];
            var overrideSet = false;
            try
            {
                var baseChallenge = FindChallenge(design);
                if (baseChallenge == null)
                {
                    log?.Invoke($"Custom challenge {design.Name}: base challenge '{design.BaseChallenge}' not loaded. Open the challenge menu, then Refresh.");
                    return false;
                }

                DestroyActiveOverride(restoreHolder: true);

                var custom = UnityEngine.Object.Instantiate(baseChallenge);
                custom.name = design.Name;
                custom.id = design.IdHash();
                custom.menuIndex = baseChallenge.menuIndex;
                custom.challengeState = ChallengeData.State.Available;
                custom.isPrebuilt = false;
                custom.isMultiplayer = design.IsMultiplayer ?? baseChallenge.isMultiplayer;
                custom.includeHermits = design.IncludeHermits ?? baseChallenge.includeHermits;
                custom.zombieMode = design.ZombieMode ?? baseChallenge.zombieMode;
                custom.challengeSeed = design.ChallengeSeed ?? baseChallenge.challengeSeed;
                custom.forceSelectBiomeIndex = design.ForceSelectBiomeIndex ?? baseChallenge.forceSelectBiomeIndex;
                if (TryParseEnum(design.StartingCurrencyBagType, out CurrencyBagType bag)) custom.startingCurrencyBagType = bag;
                if (design.CustomOptionsString != null) custom.customChallengeDataOptionsString = design.CustomOptionsString;

                var islands = BuildLevelConfigs(design, baseChallenge, log);
                if (islands == null || islands.Length == 0)
                {
                    DestroyChallengeClone(custom);
                    DestroyActiveOverride(restoreHolder: false);
                    return false;
                }
                custom.levelConfigs = islands;

                var holder = ChallengeHolder.Inst;
                if (holder == null)
                {
                    log?.Invoke("Custom challenge: ChallengeHolder is not ready. Open the challenge menu, then try again.");
                    DestroyChallengeClone(custom);
                    DestroyActiveOverride(restoreHolder: false);
                    return false;
                }

                holder.SetChallengeDataOverride(custom);
                overrideSet = true;
                _activeChallenge = custom;
                _activeName = design.Name;
                RuntimeInteractionLogger.Event(RuntimeLogLevel.EventHeavy, "challenge", "apply-custom", custom, null,
                    data: RuntimeInteractionLogger.Fields(("name", design.Name), ("base", baseChallenge.name), ("islands", custom.levelConfigs?.Length ?? 0)));
                log?.Invoke($"Custom challenge active: {design.Name}. Start a challenge through the game's challenge menu to use it.");
                return true;
            }
            catch (Exception e)
            {
                DestroyActiveOverride(restoreHolder: overrideSet);
                log?.Invoke($"Custom challenge {design.Name}: apply failed: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        public void Clear(Action<string> log)
        {
            try
            {
                DestroyActiveOverride(restoreHolder: true);
            }
            catch (Exception e)
            {
                log?.Invoke($"Custom challenge clear failed: {e.Message}");
            }
            log?.Invoke("Custom challenge override cleared.");
        }

        private Il2CppReferenceArray<LevelConfig> BuildLevelConfigs(CustomChallengeDesign design, ChallengeData baseChallenge, Action<string> log)
        {
            var requested = design.Islands.Count > 0 ? design.Islands : new List<CustomIslandDesign> { new CustomIslandDesign() };
            var arr = new Il2CppReferenceArray<LevelConfig>(requested.Count);
            for (int i = 0; i < requested.Count; i++)
            {
                var island = requested[i];
                var baseLevel = FindLevelConfig(island.BaseLevelConfig)
                    ?? FindLevelConfig(design.BaseLevelConfig)
                    ?? FirstBaseLevel(baseChallenge);
                if (baseLevel == null)
                {
                    log?.Invoke($"Custom challenge {design.Name}: no base island config loaded for island {i + 1}.");
                    return null;
                }

                var clone = UnityEngine.Object.Instantiate(baseLevel);
                clone.name = string.IsNullOrWhiteSpace(island.Name) ? $"{design.Name} Island {i + 1}" : island.Name;
                ApplyIsland(clone, island);
                arr[i] = clone;
                _activeLevelConfigs.Add(clone);
            }

            return arr;
        }

        private void DestroyActiveOverride(bool restoreHolder)
        {
            if (restoreHolder)
            {
                try { ChallengeHolder.Inst?.RestoreChallengeDataOverride(); } catch { }
            }

            if (_activeChallenge != null)
                DestroyChallengeClone(_activeChallenge);

            for (int i = 0; i < _activeLevelConfigs.Count; i++)
            {
                if (_activeLevelConfigs[i] != null)
                    UnityEngine.Object.Destroy(_activeLevelConfigs[i]);
            }
            _activeLevelConfigs.Clear();
            _activeChallenge = null;
            _activeName = "";
        }

        private static void DestroyChallengeClone(ChallengeData challenge)
        {
            if (challenge != null)
                UnityEngine.Object.Destroy(challenge);
        }

        private static void ApplyIsland(LevelConfig cfg, CustomIslandDesign island)
        {
            if (island.BlockName != null) cfg.blockName = island.BlockName;
            if (TryParseEnum(island.QuestType, out QuestType questType)) cfg.questType = questType;
            if (island.SeasonChangeDays.HasValue) cfg.seasonChangeDays = Clamp(island.SeasonChangeDays.Value, 2f, 30f);
            if (island.IslandMonumentID.HasValue) cfg.islandMonumentID = island.IslandMonumentID.Value;
            if (island.StartingCoins.HasValue) cfg.startingCoins = Clamp(island.StartingCoins.Value, 0, 200);
            if (island.StartingBeggars.HasValue) cfg.startingBeggars = Clamp(island.StartingBeggars.Value, 0, 50);
            if (island.StartingPeasants.HasValue) cfg.startingPeasants = Clamp(island.StartingPeasants.Value, 0, 50);
            if (island.StartingCoinsContinueOverride.HasValue) cfg.startingCoinsContinueOverride = Clamp(island.StartingCoinsContinueOverride.Value, -1, 200);
            if (island.StartingGems.HasValue) cfg.startingGems = Clamp(island.StartingGems.Value, 0, 50);
            if (island.IncomeMultiplier.HasValue) cfg.incomeMultiplier = Clamp(island.IncomeMultiplier.Value, 0.1f, 10f);
            if (island.FreeBoatParts.HasValue) cfg.freeBoatParts = Clamp(island.FreeBoatParts.Value, 0, 200);
            if (island.CaveEscapeTimer.HasValue) cfg.caveEscapeTimer = Clamp(island.CaveEscapeTimer.Value, 0f, 600f);
            if (island.ShouldPlayVictoryMusicOnGreedDefeat.HasValue) cfg.shouldPlayVictoryMusicOnGreedDefeat = island.ShouldPlayVictoryMusicOnGreedDefeat.Value;
            if (island.MinLevelWidth.HasValue) cfg.minLevelWidth = Clamp(island.MinLevelWidth.Value, 200, 1200);
            if (island.GemCount.HasValue) cfg.gemCount = Clamp(island.GemCount.Value, 0, 100);
            if (island.TwoCliffs.HasValue) cfg.twoCliffs = island.TwoCliffs.Value;
            if (island.Caveless.HasValue) cfg.caveless = island.Caveless.Value;
            if (island.Riverless.HasValue) cfg.riverless = island.Riverless.Value;
            if (island.RandomizeCliffSide.HasValue) cfg.randomizeCliffSide = island.RandomizeCliffSide.Value;
            if (island.SideDistributionBias.HasValue) cfg._sideDistributionBias = Clamp(island.SideDistributionBias.Value, 1f, 9f);
        }

        private static ChallengeData FindChallenge(CustomChallengeDesign design)
        {
            foreach (var challenge in Resources.FindObjectsOfTypeAll<ChallengeData>())
            {
                if (challenge == null) continue;
                if (Matches(challenge.name, design.BaseChallenge)) return challenge;
                if (design.BaseChallengeId.HasValue && challenge.id == design.BaseChallengeId.Value) return challenge;
                if (TryParseEnum(design.BaseChallengeType, out ChallengeData.ChallengeType type) && challenge.challengeType == type) return challenge;
            }

            bool requestedBase = !string.IsNullOrWhiteSpace(design.BaseChallenge)
                || design.BaseChallengeId.HasValue
                || !string.IsNullOrWhiteSpace(design.BaseChallengeType);
            return requestedBase ? null : FirstUsableChallenge();
        }

        private static ChallengeData FirstUsableChallenge()
        {
            foreach (var challenge in Resources.FindObjectsOfTypeAll<ChallengeData>())
                if (challenge != null && challenge.challengeState == ChallengeData.State.Available && challenge.levelConfigs != null && challenge.levelConfigs.Length > 0)
                    return challenge;
            return null;
        }

        private static LevelConfig FindLevelConfig(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (var cfg in Resources.FindObjectsOfTypeAll<LevelConfig>())
                if (cfg != null && Matches(cfg.name, name))
                    return cfg;
            return null;
        }

        private static LevelConfig FirstBaseLevel(ChallengeData challenge)
        {
            try
            {
                if (challenge?.levelConfigs != null && challenge.levelConfigs.Length > 0)
                    return challenge.levelConfigs[0];
            }
            catch { }
            foreach (var cfg in Resources.FindObjectsOfTypeAll<LevelConfig>())
                if (cfg != null) return cfg;
            return null;
        }

        private static bool Matches(string actual, string requested)
        {
            if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(requested)) return false;
            return string.Equals(Clean(actual), Clean(requested), StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string value) => value.Replace("(Clone)", "").Trim();
        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));

        private static bool TryParseEnum<T>(string value, out T result) where T : struct
        {
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out result)) return true;
            result = default;
            return false;
        }

        private void SeedSampleIfEmpty()
        {
            if (Directory.GetFiles(Folder, "*.json").Length > 0) return;
            var sample = new CustomChallengeDesign
            {
                Schema = 1,
                Id = "sample.custom-island",
                Name = "Sample Custom Island",
                Description = "Starter JSON generated by KingdomMod. Edit it in the asset designer, then Refresh in F1.",
                BaseChallenge = "Daily Challenge Island",
                BaseChallengeType = "DailyChallengeIsland",
                BaseLevelConfig = "Daily_Challenge_OakAndBirch_LandConfig",
                ChallengeSeed = 12345,
                IncludeHermits = true,
                IsMultiplayer = true,
                ZombieMode = false,
                ForceSelectBiomeIndex = -1,
                StartingCurrencyBagType = "Bag",
                Islands = new List<CustomIslandDesign>
                {
                    new CustomIslandDesign
                    {
                        Name = "Sample Island 1",
                        BaseLevelConfig = "Daily_Challenge_OakAndBirch_LandConfig",
                        StartingCoins = 10,
                        StartingBeggars = 2,
                        StartingPeasants = 0,
                        StartingGems = 2,
                        FreeBoatParts = 20,
                        IncomeMultiplier = 1.25f,
                        CaveEscapeTimer = 25f,
                        MinLevelWidth = 560,
                        GemCount = 4,
                        SeasonChangeDays = 3f,
                        TwoCliffs = true,
                        Caveless = false,
                        Riverless = false,
                        RandomizeCliffSide = false,
                        SideDistributionBias = 5f
                    }
                }
            };
            File.WriteAllText(Path.Combine(Folder, "sample.custom-challenge.json"), JsonSerializer.Serialize(sample, JsonOptions));
        }
    }

    internal sealed class CustomChallengeDesign
    {
        public int Schema { get; set; } = 1;
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string BaseChallenge { get; set; }
        public int? BaseChallengeId { get; set; }
        public string BaseChallengeType { get; set; }
        public string BaseLevelConfig { get; set; }
        public int? ChallengeSeed { get; set; }
        public bool? IsMultiplayer { get; set; }
        public bool? IncludeHermits { get; set; }
        public bool? ZombieMode { get; set; }
        public int? ForceSelectBiomeIndex { get; set; }
        public string StartingCurrencyBagType { get; set; }
        public string CustomOptionsString { get; set; }
        public List<CustomIslandDesign> Islands { get; set; } = new();
        public string SourcePath { get; set; }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = Path.GetFileNameWithoutExtension(SourcePath) ?? "custom.challenge";
            if (string.IsNullOrWhiteSpace(Name)) Name = Id;
            Islands ??= new List<CustomIslandDesign>();
        }

        public int IdHash()
        {
            unchecked
            {
                var key = string.IsNullOrWhiteSpace(Id) ? Name : Id;
                uint hash = 17;
                for (int i = 0; i < key.Length; i++) hash = hash * 31 + key[i];
                return 100000 + (int)(hash % 899999);
            }
        }
    }

    internal sealed class CustomIslandDesign
    {
        public string Name { get; set; }
        public string BaseLevelConfig { get; set; }
        public string BlockName { get; set; }
        public string QuestType { get; set; }
        public float? SeasonChangeDays { get; set; }
        public int? IslandMonumentID { get; set; }
        public int? StartingCoins { get; set; }
        public int? StartingBeggars { get; set; }
        public int? StartingPeasants { get; set; }
        public int? StartingCoinsContinueOverride { get; set; }
        public int? StartingGems { get; set; }
        public float? IncomeMultiplier { get; set; }
        public int? FreeBoatParts { get; set; }
        public float? CaveEscapeTimer { get; set; }
        public bool? ShouldPlayVictoryMusicOnGreedDefeat { get; set; }
        public int? MinLevelWidth { get; set; }
        public int? GemCount { get; set; }
        public bool? TwoCliffs { get; set; }
        public bool? Caveless { get; set; }
        public bool? Riverless { get; set; }
        public bool? RandomizeCliffSide { get; set; }
        public float? SideDistributionBias { get; set; }
    }
}

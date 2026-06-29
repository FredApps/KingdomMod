// Example: BalanceTweaks — Tier-1 numeric tweaks driven from MelonPreferences
// and optional no-code balance packs.
// Demonstrates: SDK config wrappers + JSON pack loading + simple set-on-init.

using MelonLoader;
using MelonLoader.Utils;
using KingdomMod;

[assembly: MelonInfo(typeof(KingdomMod.Examples.BalanceTweaks.BalanceMod), "Balance Tweaks", "0.1.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.BalanceTweaks
{
    public sealed class BalanceMod : MelonMod
    {
        private MelonPreferences_Entry<float> _hourSecondsOverride;
        private MelonPreferences_Entry<bool>  _giveStartingCoins;
        private MelonPreferences_Entry<int>   _startingCoinAmount;
        private BalancePackSettings _packSettings;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("KingdomMod.Balance", "Balance Tweaks");
            _hourSecondsOverride = cat.CreateEntry("SecondsPerInGameHour", 0f,
                "If > 0, set Director.secondsPerInGameHour to this value (longer days). 0 = leave alone.");
            _giveStartingCoins   = cat.CreateEntry("GiveStartingCoins", false,
                "Give the local monarch a coin top-up on game start.");
            _startingCoinAmount  = cat.CreateEntry("StartingCoinAmount", 25,
                "How many coins to give at game start (if enabled).");

            LoadBalancePack();
            Kingdom.Game.OnGameStart += ApplyOnStart;
        }

        private void ApplyOnStart()
        {
            var secondsPerHour = _packSettings?.SecondsPerInGameHour ?? _hourSecondsOverride.Value;
            if (secondsPerHour > 0f)
                Kingdom.Time.SecondsPerInGameHour = secondsPerHour;

            var startingCoins = _packSettings?.StartingCoins ?? _startingCoinAmount.Value;
            if (_giveStartingCoins.Value && startingCoins > 0)
            {
                Kingdom.Economy.GiveCoins(startingCoins);
                LoggerInstance.Msg($"Granted {startingCoins} starting coins.");
            }
        }

        private void LoadBalancePack()
        {
            foreach (var pack in Kingdom.Packs.DiscoverPacks(MelonEnvironment.ModsDirectory))
            {
                if (!pack.HasBalance)
                    continue;

                _packSettings = Kingdom.Packs.LoadJson<BalancePackSettings>(pack.BalancePath);
                if (_packSettings != null)
                {
                    LoggerInstance.Msg($"Loaded balance pack: {pack.Name} {pack.Version}");
                    return;
                }
            }
        }
    }

    public sealed class BalancePackSettings
    {
        public float SecondsPerInGameHour { get; set; }
        public int StartingCoins { get; set; }
    }
}

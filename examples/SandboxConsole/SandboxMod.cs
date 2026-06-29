// Example: SandboxConsole — a developer/cheat mod.
// Demonstrates: subscribing to SDK events, using built-in cheats, logging.

using MelonLoader;
using KingdomMod;

[assembly: MelonInfo(typeof(KingdomMod.Examples.SandboxConsole.SandboxMod), "Sandbox Console", "0.1.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.SandboxConsole
{
    public sealed class SandboxMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Sandbox Console ready. Press F1 in-game for the KingdomMod panel.");

            // Day-rollover ping — shows the SDK's event surface works.
            Kingdom.Time.OnDayChanged += () =>
                LoggerInstance.Msg($"Day {Kingdom.Time.DaysInReign} (island {Kingdom.Time.IslandDays}, season {Kingdom.Time.CurrentSeason})");

            Kingdom.Time.OnSeasonChanged += s =>
                LoggerInstance.Msg($"Season changed to {s}.");

            Kingdom.Game.OnGameStart += () =>
                LoggerInstance.Msg($"Run started on land {Kingdom.Game.CurrentLand}.");

            Kingdom.Game.OnLose += () =>
                LoggerInstance.Warning("Monarch lost the crown. Try toggling 'Infinite money' from the F1 panel.");
        }
    }
}

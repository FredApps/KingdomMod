// Example: SpeedHotkeys — Tier-3 keyboard control of Director.ClockSpeedModifier.
//
//   F5 = slow down by Step
//   F6 = reset to 1.0
//   F7 = speed up by Step
//   F8 = toggle freeze (0.0) <-> last non-zero speed
//
// Independent of SpeedTweaks — drives Director.ClockSpeedModifier directly via
// the SDK's Kingdom.Time wrapper. If SpeedTweaks is also loaded, its per-frame
// reassertion will win; in that case use this mod's hotkeys to nudge its
// SpeedMultiplier preference instead (see ApplyDelta).

using MelonLoader;
using UnityEngine;
using KingdomMod;

[assembly: MelonInfo(typeof(KingdomMod.Examples.SpeedHotkeys.SpeedHotkeysMod), "Speed Hotkeys", "0.1.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.SpeedHotkeys
{
    public sealed class SpeedHotkeysMod : MelonMod
    {
        private static MelonPreferences_Entry<float> _step;
        private static MelonPreferences_Entry<float> _min;
        private static MelonPreferences_Entry<float> _max;
        private float _lastNonZero = 1f;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("KingdomMod.SpeedHotkeys", "Speed Hotkeys");
            _step = cat.CreateEntry("Step", 0.25f, "Speed change per F5/F7 press.");
            _min  = cat.CreateEntry("Min",  0.25f, "Lower clamp for F5.");
            _max  = cat.CreateEntry("Max",  4.0f,  "Upper clamp for F7.");
            Kingdom.Mods.RegisterHotkey("F5 / F6 / F7 / F8", "Game speed: slower / reset 1x / faster / freeze toggle");
            LoggerInstance.Msg("Speed Hotkeys loaded. F5/F6/F7/F8.");
        }

        public override void OnUpdate()
        {
            if      (Input.GetKeyDown(KeyCode.F5)) ApplyDelta(-_step.Value);
            else if (Input.GetKeyDown(KeyCode.F6)) Set(1.0f);
            else if (Input.GetKeyDown(KeyCode.F7)) ApplyDelta(+_step.Value);
            else if (Input.GetKeyDown(KeyCode.F8)) ToggleFreeze();
        }

        // Kingdom.IsReady becomes true once the Managers singleton has spawned.
        // Hitting an F-key on the main menu before that point would write through
        // a null Director and log misleading values; guard at the entry points.
        private bool DirectorReady() => Kingdom.IsReady;

        private void ApplyDelta(float delta)
        {
            if (!DirectorReady()) { LoggerInstance.Msg("Speed Hotkeys: no active game — ignored."); return; }
            Set(Kingdom.Time.ClockSpeedMultiplier + delta);
        }

        private void Set(float value)
        {
            if (!DirectorReady()) { LoggerInstance.Msg("Speed Hotkeys: no active game — ignored."); return; }
            value = Mathf.Clamp(value, _min.Value, _max.Value);
            try { Kingdom.Time.ClockSpeedMultiplier = value; }
            catch (System.Exception e) { LoggerInstance.Warning($"Speed Hotkeys: clock write failed: {e.Message}"); return; }
            if (value > 0f) _lastNonZero = value;
            LoggerInstance.Msg($"Clock x{value:0.00}");
        }

        private void ToggleFreeze()
        {
            if (!DirectorReady()) { LoggerInstance.Msg("Speed Hotkeys: no active game — ignored."); return; }
            float current;
            try { current = Kingdom.Time.ClockSpeedMultiplier; }
            catch { return; }
            if (current > 0f)
            {
                _lastNonZero = current;
                try { Kingdom.Time.ClockSpeedMultiplier = 0f; } catch { return; }
                LoggerInstance.Msg("Time frozen.");
            }
            else
            {
                try { Kingdom.Time.ClockSpeedMultiplier = _lastNonZero; } catch { return; }
                LoggerInstance.Msg($"Time resumed at x{_lastNonZero:0.00}.");
            }
        }
    }
}

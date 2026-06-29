// Example: HudOverlay — Tier-3 IMGUI overlay.
//
// Top-left panel showing day/phase/season/clock and the next few scheduled
// Director events (wave arrivals, portal opens, season starts). Toggle with F2.

using System.Linq;
using MelonLoader;
using UnityEngine;
using KingdomMod;
using KingdomMod.Internal;

[assembly: MelonInfo(typeof(KingdomMod.Examples.HudOverlay.HudOverlayMod), "HUD Overlay", "0.1.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.HudOverlay
{
    public sealed class HudOverlayMod : MelonMod
    {
        private static MelonPreferences_Entry<bool> _visible;
        private static MelonPreferences_Entry<int>  _forecastCount;
        private GUIStyle _style;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("KingdomMod.Hud", "HUD Overlay");
            _visible       = cat.CreateEntry("Visible", false, "Show the overlay (toggle in-game with F2).");
            _forecastCount = cat.CreateEntry("ForecastCount", 5, "How many upcoming Director events to list.");
            Kingdom.Mods.RegisterHotkey("F2", "Toggle the day / phase / season / next-events HUD overlay");
            LoggerInstance.Msg("HUD Overlay loaded. F2 to toggle.");
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F2))
                _visible.Value = !_visible.Value;
        }

        public override void OnGUI()
        {
            if (!_visible.Value) return;

            var d = GameRefs.Director;
            if (d == null) return;

            // The Director wrapper survives even while the game's internal
            // state is half-torn-down between scenes / on the main menu. Its
            // getters (IsNight, IsDaytime, GetNextDaySeason, .events) NRE
            // internally when accessed in that window. Snapshot everything we
            // need behind try/catch and fall back to safe defaults so the
            // overlay never propagates an exception into IMGUI.
            int totalDay = SafeInt(() => d.TotalDaysInReign);
            int islandDay = SafeInt(() => d.CurrentIslandDays);
            int island = SafeInt(() => Kingdom.Game.CurrentLand);
            float curTime = SafeFloat(() => d.currentTime);
            bool isNight = SafeBool(() => d.IsNight);
            bool isDay = SafeBool(() => d.IsDaytime);
            bool isPaused = SafeBool(() => d.IsTimePaused);
            string season = SafeStr(() => d.CurrentSeason.ToString(), "?");
            string nextSeason = SafeStr(() => d.GetNextDaySeason().ToString(), "?");
            float clockMod = SafeFloat(() => d.ClockSpeedModifier);
            float secsPerHour = SafeFloat(() => d.secondsPerInGameHour);
            var events = SafeRef(() => d.events);

            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };

            int limit = Mathf.Max(1, _forecastCount.Value);
            int shown = 0;
            bool hasEvents = events != null;
            if (hasEvents)
            {
                foreach (var e in events)
                {
                    if (e == null) continue;
                    if (e.startDay < totalDay) continue;
                    if (shown >= limit) break;
                    shown++;
                }
            }

            // Header: 5 status lines (last one +22). "Next events:" label adds 18
            // only if the director surfaced an events array. Then 18 per event.
            float contentHeight = 22 + (18 * 4 + 22) + (hasEvents ? 18 : 0) + (18 * shown);
            GUI.Box(new Rect(8, 8, 260, contentHeight + 6), "KingdomMod HUD");

            var y = 30f;
            int hour = Mathf.FloorToInt(curTime);
            int minute = Mathf.FloorToInt((curTime - hour) * 60f);

            GUI.Label(new Rect(16, y, 244, 18), $"Day {totalDay}  day on island: {islandDay}", _style); y += 18;
            GUI.Label(new Rect(16, y, 244, 18), $"Island: {island}", _style); y += 18;
            GUI.Label(new Rect(16, y, 244, 18), $"{hour:00}:{minute:00}  {(isNight ? "Night" : isDay ? "Day" : "Twilight")}  {(isPaused ? "[PAUSED]" : "")}", _style); y += 18;
            GUI.Label(new Rect(16, y, 244, 18), $"Season: {season}  (next: {nextSeason})", _style); y += 18;
            GUI.Label(new Rect(16, y, 244, 18), $"Clock x{clockMod:0.00}  {secsPerHour:0.0}s/hr", _style); y += 22;

            if (!hasEvents) return;

            GUI.Label(new Rect(16, y, 244, 18), "Next events:", _style); y += 18;

            int drawn = 0;
            foreach (var e in events)
            {
                if (e == null) continue;
                if (e.startDay < totalDay) continue;
                if (drawn >= shown) break;
                GUI.Label(new Rect(16, y, 244, 18), FormatEvent(e), _style);
                y += 18;
                drawn++;
            }
        }

        // Internal-NRE-resilient snapshot helpers. Wrapping the calls in
        // try/catch out here, *outside* the IMGUI Layout/Repaint passes,
        // doesn't desync layout state — we capture the values into locals
        // and the actual GUI.Label calls cannot themselves throw.
        private static int    SafeInt  (System.Func<int> f)    { try { return f(); } catch { return 0; } }
        private static float  SafeFloat(System.Func<float> f)  { try { return f(); } catch { return 0f; } }
        private static bool   SafeBool (System.Func<bool> f)   { try { return f(); } catch { return false; } }
        private static string SafeStr  (System.Func<string> f, string fb) { try { return f() ?? fb; } catch { return fb; } }
        private static T      SafeRef<T>(System.Func<T> f) where T : class { try { return f(); } catch { return null; } }

        private static string FormatEvent(Il2Cpp.DirectorEvent e)
        {
            string label = e.type switch
            {
                Il2Cpp.DirectorEvent.Type.SpawnWave    => $"Wave {(string.IsNullOrEmpty(e.waveName) ? "" : e.waveName)} ({e.side})",
                Il2Cpp.DirectorEvent.Type.OpenPortal   => $"Portal opens ({e.side})",
                Il2Cpp.DirectorEvent.Type.ScheduleWave => $"Wave scheduled ({e.side})",
                Il2Cpp.DirectorEvent.Type.StartSeason  => $"Season → {e.season}",
                Il2Cpp.DirectorEvent.Type.PlayTrack    => $"Track: {e.trackName}",
                _ => e.type.ToString(),
            };
            int hour = Mathf.FloorToInt(e.startTime);
            int minute = Mathf.FloorToInt((e.startTime - hour) * 60f);
            return $"  D{e.startDay} {hour:00}:{minute:00}  {label}";
        }
    }
}

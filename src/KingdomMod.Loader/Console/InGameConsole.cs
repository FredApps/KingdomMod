// IMGUI console â€” minimal, dependency-free, opens with F1.  Shows status, lets
// you flip the built-in cheats, give yourself currency, and swap either
// player's mount on the fly. This is the reference UI surface for KingdomMod;
// richer per-mod tabs can be added later.
//
// IMGUI constraint: KTC's Unity build has stripped GUI.DoTextField, so we
// cannot use GUILayout.TextField (it throws "Method unstripping failed").
// All editable values must use button steppers instead. Also: never wrap
// IMGUI calls in try/catch â€” Layout/Repaint must commit the same control
// counts, and swallowing a mid-pass exception desyncs them and triggers
// "Getting control N's position in a group with only M controls" the next
// frame. Just don't call APIs that can throw.

using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace KingdomMod.Loader.Console
{
    internal sealed class InGameConsole
    {
        private bool _visible;
        private Rect _window;
        private bool _positioned;
        private Vector2 _scroll;
        private Vector2 _mountScroll;
        private int _coinsAmount = 10;
        private int _gemsAmount = 1;
        private int _mountTarget;
        private int _giftTarget;
        private bool _showMounts;
        private GUIStyle _boldLabel;
        private GUIStyle _titleLabel;
        private GUIStyle _cursorStyle;
        private readonly List<Steed> _mountOptions = new();
        private readonly List<string> _log = new()
        {
            "Console ready. This log shows F1 actions, mount swaps, resets, and setting errors."
        };

        // Window layout â€” wide bar pinned to the bottom of the screen, where
        // KTC's UI doesn't already paint gameplay. We position lazily on first
        // open so we have the actual Screen dimensions; once the user drags
        // the window we keep their position across toggles.
        private const float WindowHeight = 296f;       // ~30% shorter top edge (was 423)
        private const float WindowBottomMargin = 24f;

        // Cursor state captured when the console opens, restored when it closes â€”
        // KTC keeps the system cursor hidden during gameplay so IMGUI has nothing
        // to render. Drawing our own software pointer avoids a visible fight with
        // the game repeatedly hiding the hardware cursor.
        private bool _savedCursorVisible;
        private CursorLockMode _savedCursorLock;
        private bool _cursorOverridden;
        private bool _cursorSuspended;

        public void Toggle()
        {
            _visible = !_visible;
            if (_visible)
            {
                if (!_positioned) { PositionAtBottom(); _positioned = true; }
                CaptureAndShowCursor();
            }
            else
            {
                RestoreCursor();
            }
        }

        public void OnUpdate(bool cursorAllowed)
        {
            if (!_visible)
            {
                RestoreCursor();
                return;
            }

            if (!cursorAllowed)
            {
                SuspendCursorOverride();
                return;
            }

            if (_cursorSuspended) CaptureAndShowCursor();
            MaintainCursorOverride();
        }

        private void PositionAtBottom()
        {
            int sw = Screen.width  > 0 ? Screen.width  : 1920;
            int sh = Screen.height > 0 ? Screen.height : 1080;
            // Full screen width, pinned to the bottom.
            float width  = sw;
            float height = WindowHeight;
            float x = 0f;
            float y = sh - height - WindowBottomMargin;
            _window = new Rect(x, y, width, height);
        }

        private void CaptureAndShowCursor()
        {
            if (_cursorOverridden) return;
            _savedCursorVisible = Cursor.visible;
            _savedCursorLock = Cursor.lockState;
            _cursorOverridden = true;
            _cursorSuspended = false;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.None;
        }

        private void RestoreCursor()
        {
            if (!_cursorOverridden) return;
            Cursor.visible = _savedCursorVisible;
            Cursor.lockState = _savedCursorLock;
            _cursorOverridden = false;
            _cursorSuspended = false;
        }

        private void SuspendCursorOverride()
        {
            if (!_cursorOverridden) return;
            Cursor.visible = _savedCursorVisible;
            Cursor.lockState = _savedCursorLock;
            _cursorOverridden = false;
            _cursorSuspended = true;
        }

        private void MaintainCursorOverride()
        {
            if (!_cursorOverridden) return;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.None;
        }

        public void Log(string line)
        {
            _log.Add(line);
            if (_log.Count > 200) _log.RemoveAt(0);
        }

        public void OnGUI()
        {
            if (!_visible) return;
            MaintainCursorOverride();

            // Pin as a full-width bar flush with the bottom of the screen.
            // GUILayout.Window grows the window to fit its content, which can be
            // taller than WindowHeight (status + columns + mount/log + shortcuts),
            // so a fixed top would push the bottom off-screen. Re-derive the top
            // each frame from the height GUILayout computed last frame, so the
            // bottom edge always lands at the screen bottom (minus the margin).
            int sw = Screen.width  > 0 ? Screen.width  : 1920;
            int sh = Screen.height > 0 ? Screen.height : 1080;
            float h = _window.height > 0f ? _window.height : WindowHeight;
            float y = sh - h - WindowBottomMargin;
            if (y < 0f) y = 0f;
            _window.x = 0f;
            _window.width = sw;
            _window.y = y;

            _window = GUILayout.Window(0xCAB1ED, _window, (GUI.WindowFunction)DrawWindow, "KingdomMod  (F1 to hide)");
            ReserveConsoleMouseRegion();
            DrawSoftwareCursor();
        }

        private void DrawWindow(int id)
        {
            EnsureStyles();
            // Top: full-width status line. The console now auto-opens at the main
            // menu, where the game managers aren't loaded yet, so every Kingdom.*
            // getter is guarded - they evaluate before GUILayout.Label, so this
            // can't desync IMGUI control counts.
            // Top row: full-width status on the left, Reset pinned top-right.
            GUILayout.BeginHorizontal();
            DrawStatusLine();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Reset",
                    "Turn every cheat and mod option in this panel back to its vanilla / off state."),
                    GUILayout.Width(80)))
                ResetAll();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Main controls row: loader cheats/fixes, mod-published controls, gifts.
            GUILayout.BeginHorizontal();

            // Column 1 â€” cheats. Coin cheats are tri-state (None / NoTax /
            // Infinite) since they're mutually exclusive â€” running both would
            // double-swap the bag visual. Rendered as radio-style toggles.
            GUILayout.BeginVertical(GUILayout.Width(220));
            GUILayout.Label(new GUIContent("Cheats", "Built-in loader cheats. Each one persists across sessions."), _titleLabel);
            GUILayout.Label(new GUIContent("  Coins:", "How coins behave: vanilla, tax-free, or unlimited."));
            DrawCoinCheatRadio();
            GUILayout.Space(2);
            GUILayout.Label(new GUIContent("  Infinite stamina:", "Whether sprinting drains stamina."));
            DrawInfiniteStaminaRadio();
            GUILayout.EndVertical();

            GUILayout.Space(12);

            GUILayout.BeginVertical(GUILayout.Width(180));
            GUILayout.Label(new GUIContent("Fixes", "Small loader-side bug fixes. Each one persists across sessions."), _titleLabel);
            GUILayout.Label(new GUIContent("  Crown pickup:", "Repairs stuck dropped crowns after 10s, or returns the crown if the dropped object disappears."));
            DrawCrownPickupFixRadio();
            GUILayout.Label(new GUIContent("  Boar vanish:", "Repairs winter boars that disappear without dying; preserves coin rewards unless the boar returned to its nest."));
            DrawBoarVanishFixRadio();
            GUILayout.EndVertical();

            GUILayout.Space(12);

            // Column 1b â€” mod-published toggles + choices (Kingdom.Mods registry).
            // Each mod owns its own state; we just render get/set.
            GUILayout.BeginVertical(GUILayout.Width(240));
            GUILayout.Label(new GUIContent("Mods", "Options published by installed mods. Hover an option for what it does."), _titleLabel);
            var toggles = Kingdom.Mods.Toggles;
            var choices = Kingdom.Mods.Choices;
            if (toggles.Count == 0 && choices.Count == 0)
            {
                GUILayout.Label("(no mods registered)");
            }
            else
            {
                for (int i = 0; i < toggles.Count; i++)
                {
                    var t = toggles[i];
                    bool cur = false;
                    try { cur = t.Get(); } catch { /* mod's getter threw â€” show as off */ }
                    var content = new GUIContent(" " + t.Label,
                        string.IsNullOrEmpty(t.Tooltip) ? t.Label : t.Tooltip);
                    bool next = GUILayout.Toggle(cur, content);
                    if (next != cur)
                    {
                        Log($"{t.Label}: {(next ? "On" : "Off")}.");
                        try { t.Set(next); } catch (System.Exception e) { Log($"{t.Label}: set failed â€” {e.Message}"); }
                    }
                }
                for (int i = 0; i < choices.Count; i++) DrawModChoice(choices[i]);
            }
            GUILayout.EndVertical();

            GUILayout.Space(12);

            // Column 3 â€” currency steppers (no text fields â€” KTC's Unity build
            // has stripped GUI.DoTextField).
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Gifts", "Give coins, gems, or a beggar to the selected player."), _titleLabel, GUILayout.Width(52));
            GUILayout.Label(new GUIContent("Target:", "Which player receives gifts."), GUILayout.Width(48));
            if (GUILayout.Toggle(_giftTarget == 0, new GUIContent("P1", "Give gifts to Player 1."), "Button", GUILayout.Width(36))) _giftTarget = 0;
            if (GUILayout.Toggle(_giftTarget == 1, new GUIContent("P2", "Give gifts to Player 2."), "Button", GUILayout.Width(36))) _giftTarget = 1;
            GUILayout.EndHorizontal();
            DrawGiftSliderRow("Coins", ref _coinsAmount, 1, 200, GiveCoinsToTarget);
            DrawGiftSliderRow("Gems",  ref _gemsAmount,  1, 12,  GiveGemsToTarget);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Beggar:", GUILayout.Width(90));
            if (GUILayout.Button(new GUIContent("Spawn", "Spawn a beggar beside the selected player."), GUILayout.Width(90))) SpawnBeggarForTarget();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            if (_showMounts)
            {
                DrawMountSection();
            }
            else
            {
                // Mount + Log row.
                GUILayout.BeginHorizontal();

                // Left: Mount section.
                GUILayout.BeginVertical(GUILayout.Width(_window.width * 0.55f));
                DrawMountSection();
                GUILayout.EndVertical();

                GUILayout.Space(12);

                // Right: Log.
                GUILayout.BeginVertical();
                GUILayout.Label("Log", _titleLabel);
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(96));
                for (int i = _log.Count - 1; i >= 0; i--) GUILayout.Label(_log[i]);
                GUILayout.EndScrollView();
                GUILayout.EndVertical();

                GUILayout.EndHorizontal();
            }

            DrawShortcutsGuide();

            // Tooltip bar â€” shows the hovered control's tooltip. Always drawn
            // (empty string when nothing is hovered) so the IMGUI control count
            // stays identical between the Layout and Repaint passes; conditionally
            // adding it would desync the layout. GUI.tooltip is populated during
            // Repaint after the control under the cursor draws.
            GUILayout.Space(4);
            GUILayout.Label("i  " + GUI.tooltip);

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        // Turn every cheat and mod option in the panel back to vanilla/off:
        // registry toggles -> false, choices -> index 0, coin cheat -> None,
        // infinite stamina -> off. Each target owns its own persistence, so the
        // off state survives the next launch. Always available (it also clears
        // the loader cheats, which exist even when no mods are registered).
        private void ResetAll()
        {
            var toggles = Kingdom.Mods.Toggles;
            var choices = Kingdom.Mods.Choices;
            int n = 0;
            for (int i = 0; i < toggles.Count; i++)
            {
                try { if (toggles[i].Get()) { toggles[i].Set(false); n++; } }
                catch (System.Exception e) { Log($"{toggles[i].Label}: reset failed â€” {e.Message}"); }
            }
            for (int i = 0; i < choices.Count; i++)
            {
                try { if (choices[i].Get() != 0) { choices[i].Set(0); n++; } }
                catch (System.Exception e) { Log($"{choices[i].Label}: reset failed â€” {e.Message}"); }
            }
            // Loader-owned cheats (not in the Kingdom.Mods registry).
            try
            {
                if (Kingdom.Economy.CoinCheat != CoinCheatMode.None)
                {
                    Kingdom.Economy.CoinCheat = CoinCheatMode.None;
                    LoaderMod.Instance?.PersistCoinCheat(CoinCheatMode.None);
                    n++;
                }
            }
            catch (System.Exception e) { Log($"Coin cheat reset failed â€” {e.Message}"); }
            try
            {
                if (Kingdom.Players.InfiniteStamina)
                {
                    Kingdom.Players.InfiniteStamina = false;
                    LoaderMod.Instance?.PersistInfiniteStamina(false);
                    n++;
                }
            }
            catch (System.Exception e) { Log($"Stamina reset failed â€” {e.Message}"); }

            Log($"Reset {n} control(s) to off.");
        }

        private void EnsureStyles()
        {
            _boldLabel ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _titleLabel ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _cursorStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }

        private void DrawSoftwareCursor()
        {
            if (Event.current == null) return;
            var p = Event.current.mousePosition;
            GUI.Label(new Rect(p.x + 1f, p.y + 1f, 24f, 24f), ">", _cursorStyle);
        }

        private void ReserveConsoleMouseRegion()
        {
            var e = Event.current;
            if (e == null) return;
            if (!_window.Contains(e.mousePosition)) return;
            if (e.type == EventType.MouseDown || e.type == EventType.MouseUp ||
                e.type == EventType.MouseDrag || e.type == EventType.ScrollWheel)
                e.Use();
        }

        // Draw the status line with every game-state getter guarded, so the
        // panel survives the main menu / scene transitions where the managers
        // are null. Each value falls back to "-" instead of throwing.
        private void DrawStatusLine()
        {
            string ready   = Safe(() => Kingdom.IsReady.ToString());
            string island  = Safe(() => Kingdom.Game.CurrentLand.ToString());
            string day     = Safe(() => Kingdom.Time.DaysInReign.ToString());
            string season  = Safe(() => Kingdom.Time.CurrentSeason.ToString());
            string coins   = Safe(() => Kingdom.Economy.Coins.ToString());
            string gems    = Safe(() => Kingdom.Economy.Gems.ToString());
            string night   = Safe(() => Kingdom.Time.IsNight.ToString());
            GUILayout.BeginHorizontal();
            DrawStatusPair("Ready:", ready, 54, 52);
            DrawStatusPair("Island:", island, 56, 70);
            DrawStatusPair("Day:", day, 36, 58);
            DrawStatusPair("Season:", season, 60, 86);
            DrawStatusPair("Coins:", coins, 52, 48);
            DrawStatusPair("Gems:", gems, 48, 48);
            DrawStatusPair("Night:", night, 48, 52);
            GUILayout.EndHorizontal();
        }

        private void DrawStatusPair(string label, string value, float labelWidth, float valueWidth)
        {
            GUILayout.Label(label, _boldLabel, GUILayout.Width(labelWidth));
            GUILayout.Label(value, GUILayout.Width(valueWidth));
        }

        private static string Safe(System.Func<string> get)
        {
            try { return get(); } catch { return "-"; }
        }

        // Bottom-of-window cheat-sheet: every hotkey mods registered through
        // Kingdom.Mods.RegisterHotkey, ordered F1..F8. Only keys whose owning
        // mod is actually loaded appear, so the guide never lies about what's
        // installed.
        private void DrawShortcutsGuide()
        {
            var hotkeys = Kingdom.Mods.Hotkeys;
            if (hotkeys.Count == 0) return;

            GUILayout.Space(6);
            GUILayout.Label("Shortcuts");

            var sorted = new List<ModHotkey>(hotkeys);
            sorted.Sort((a, b) => FirstKeyNumber(a.Key).CompareTo(FirstKeyNumber(b.Key)));

            // Lay the shortcuts out as a single full-width line, each entry
            // "Key: Description" separated by " | ". GUILayout.Label spans the
            // window width and word-wraps, so it fills the bottom of the panel
            // instead of one short row per key.
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0) sb.Append("   |   ");
                sb.Append(sorted[i].Key).Append(": ").Append(sorted[i].Description);
            }
            GUILayout.Label(sb.ToString(), GUILayout.ExpandWidth(true));
        }

        // Sort key: the first integer in a key label ("F5 / F6 ..." -> 5).
        // Keys with no digit sort last.
        private static int FirstKeyNumber(string key)
        {
            int i = 0;
            while (i < key.Length && !char.IsDigit(key[i])) i++;
            if (i >= key.Length) return int.MaxValue;
            int n = 0;
            while (i < key.Length && char.IsDigit(key[i])) { n = n * 10 + (key[i] - '0'); i++; }
            return n;
        }

        // Generic radio row for a mod-published ModChoice. Click an option to
        // select it; clicking the already-selected option is a no-op (radio
        // semantic â€” never accidentally clear back to nothing).
        private void DrawModChoice(ModChoice ch)
        {
            int cur = -1;
            try { cur = ch.Get(); } catch { /* mod's getter threw â€” render as nothing selected */ }
            string tip = string.IsNullOrEmpty(ch.Tooltip) ? ch.Label : ch.Tooltip;
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("  " + ch.Label + ":", tip), GUILayout.Width(80));
            for (int i = 0; i < ch.Options.Length; i++)
            {
                bool isOn = (cur == i);
                bool next = GUILayout.Toggle(isOn, new GUIContent(ch.Options[i], tip), "Button", GUILayout.Width(60));
                if (next && !isOn)
                {
                    Log($"{ch.Label}: {ch.Options[i]}.");
                    try { ch.Set(i); } catch (System.Exception e) { Log($"{ch.Label}: set failed â€” {e.Message}"); }
                }
            }
            GUILayout.EndHorizontal();
        }

        // Radio-style: clicking a toggle that's already on is a no-op; clicking
        // another sets that one and clears the others. Implemented manually
        // since GUILayout.Toggle with the "Button" style + equality checks
        // gives us nicer mid-row labels than SelectionGrid.
        private void DrawCoinCheatRadio()
        {
            var cur = Kingdom.Economy.CoinCheat;
            GUILayout.BeginHorizontal();
            DrawCoinCheatOption("None",      CoinCheatMode.None,     cur, "No coin cheat; vanilla economy.");
            DrawCoinCheatOption("No drops",  CoinCheatMode.NoTax,    cur, "Coins do not drop from your purse or get shed by overflow; held gems, souls, and skulls appear beside the coin counter.");
            DrawCoinCheatOption("Infinite",  CoinCheatMode.Infinite, cur, "Purchases and item costs do not reduce wallet currencies such as coins, gems, souls, or skulls.");
            GUILayout.EndHorizontal();
        }

        // Infinite stamina as an Off/On radio (matching the coin-cheat row),
        // rather than a single checkbox. Clicking the already-active option is a
        // no-op. Persists on change so the choice survives the next launch.
        private static void DrawInfiniteStaminaRadio()
        {
            bool cur = Kingdom.Players.InfiniteStamina;
            GUILayout.BeginHorizontal();
            DrawInfiniteStaminaOption("Off", false, cur);
            DrawInfiniteStaminaOption("On",  true,  cur);
            GUILayout.EndHorizontal();
        }

        private static void DrawInfiniteStaminaOption(string label, bool value, bool current)
        {
            bool isOn = (current == value);
            string tip = value
                ? "On: the monarch (and mount) never lose stamina when sprinting."
                : "Off: vanilla stamina - sprinting drains the stamina bar.";
            bool next = GUILayout.Toggle(isOn, new GUIContent(label, tip), "Button", GUILayout.Width(64));
            if (next && !isOn)
            {
                Kingdom.Players.InfiniteStamina = value;
                LoaderMod.Instance?.PersistInfiniteStamina(value);
            }
        }

        private static void DrawCrownPickupFixRadio()
        {
            bool cur = LoaderMod.Instance == null || LoaderMod.Instance.CrownPickupFixEnabled;
            GUILayout.BeginHorizontal();
            DrawCrownPickupFixOption("Off", false, cur);
            DrawCrownPickupFixOption("On",  true,  cur);
            GUILayout.EndHorizontal();
        }

        private static void DrawCrownPickupFixOption(string label, bool value, bool current)
        {
            bool isOn = (current == value);
            string tip = value
                ? "Repairs stuck dropped crowns after 10s near a player; if the dropped crown object disappears, returns the crown after 10s."
                : "Vanilla crown pickup behavior.";
            bool next = GUILayout.Toggle(isOn, new GUIContent(label, tip), "Button", GUILayout.Width(64));
            if (next && !isOn)
            {
                LoaderMod.Instance?.PersistCrownPickupFix(value);
                LoaderMod.Instance?.LogToConsole($"Crown pickup fix: {(value ? "On" : "Off")}.");
            }
        }

        private static void DrawBoarVanishFixRadio()
        {
            bool cur = LoaderMod.Instance == null || LoaderMod.Instance.BoarVanishFixEnabled;
            GUILayout.BeginHorizontal();
            DrawBoarVanishFixOption("Off", false, cur);
            DrawBoarVanishFixOption("On",  true,  cur);
            GUILayout.EndHorizontal();
        }

        private static void DrawBoarVanishFixOption(string label, bool value, bool current)
        {
            bool isOn = (current == value);
            string tip = value
                ? "If a winter boar vanishes away from its nest, runs the boar coin reward and blocks stale nest respawn."
                : "Vanilla boar behavior.";
            bool next = GUILayout.Toggle(isOn, new GUIContent(label, tip), "Button", GUILayout.Width(64));
            if (next && !isOn)
            {
                LoaderMod.Instance?.PersistBoarVanishFix(value);
                LoaderMod.Instance?.LogToConsole($"Boar vanish fix: {(value ? "On" : "Off")}.");
            }
        }

        private static void DrawCoinCheatOption(string label, CoinCheatMode mode, CoinCheatMode current, string tip)
        {
            bool isOn = (current == mode);
            // Toggle returns the new state; only commit on rising edge to keep
            // the "radio" semantic (clicks on the already-active option do
            // nothing â€” never clear back to None implicitly).
            bool next = GUILayout.Toggle(isOn, new GUIContent(label, tip), "Button", GUILayout.Width(64));
            if (next && !isOn)
            {
                Kingdom.Economy.CoinCheat = mode;
                LoaderMod.Instance?.PersistCoinCheat(mode);
            }
        }

        private void DrawGiftSliderRow(string label, ref int amount, int min, int max, System.Action<int> give)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {amount}", GUILayout.Width(90));
            amount = Mathf.Clamp(Mathf.RoundToInt(GUILayout.HorizontalSlider(amount, min, max, GUILayout.Width(120))), min, max);
            if (GUILayout.Button(new GUIContent($"Give {amount}", $"Add {amount} {label.ToLower()} to Player {_giftTarget + 1}."), GUILayout.Width(90))) give(amount);
            GUILayout.EndHorizontal();
        }

        private void GiveCoinsToTarget(int amount)
        {
            var p = FindPlayer(_giftTarget);
            if (p == null || p.wallet == null)
            {
                Log($"Player {_giftTarget + 1} wallet not in scene.");
                return;
            }
            p.wallet.Coins = System.Math.Max(0, p.wallet.Coins + amount);
            Log($"Player {_giftTarget + 1}: +{amount} coins.");
        }

        private void GiveGemsToTarget(int amount)
        {
            var p = FindPlayer(_giftTarget);
            if (p == null || p.wallet == null)
            {
                Log($"Player {_giftTarget + 1} wallet not in scene.");
                return;
            }
            p.wallet.Gems = System.Math.Max(0, p.wallet.Gems + amount);
            Log($"Player {_giftTarget + 1}: +{amount} gems.");
        }

        private void SpawnBeggarForTarget()
        {
            var p = FindPlayer(_giftTarget);
            if (p == null)
            {
                Log($"Player {_giftTarget + 1} not in scene.");
                return;
            }

            Beggar source = null;
            foreach (var b in Resources.FindObjectsOfTypeAll<Beggar>())
            {
                if (b == null) continue;
                if (b.gameObject != null && b.gameObject.scene.handle == 0) { source = b; break; }
                if (source == null) source = b;
            }
            if (source == null)
            {
                Log("No Beggar prefab/instance is loaded yet.");
                return;
            }

            try
            {
                var pos = p.transform.position + new Vector3(1.25f, 0f, 0f);
                var beggar = Object.Instantiate(source, pos, Quaternion.identity);
                if (beggar == null)
                {
                    Log("Beggar spawn failed.");
                    return;
                }
                beggar.name = "Gifted Beggar";
                beggar.gameObject.SetActive(true);
                try { KingdomMod.Internal.GameRefs.Kingdom?.AddBeggar(beggar); } catch { }
                Log($"Spawned beggar beside Player {_giftTarget + 1}.");
            }
            catch (System.Exception e)
            {
                Log($"Beggar spawn failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static Player FindPlayer(int playerId)
        {
            foreach (var p in Kingdom.Players.All)
            {
                if (p != null && p.playerId == playerId) return p;
            }
            return null;
        }

        private void DrawMountSection()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(_showMounts ? "Mount [hide]" : "Mount [show]",
                    "Show/hide the mount picker - swap any player onto any mount in the build."), GUILayout.Width(110)))
            {
                _showMounts = !_showMounts;
                if (_showMounts) RebuildMountOptions();
            }
            if (_showMounts)
            {
                GUILayout.Space(8);
                GUILayout.Label(new GUIContent("Target:", "Which player the chosen mount is given to."), GUILayout.Width(50));
                if (GUILayout.Toggle(_mountTarget == 0, new GUIContent("P1", "Give the mount to Player 1."), "Button", GUILayout.Width(36))) _mountTarget = 0;
                if (GUILayout.Toggle(_mountTarget == 1, new GUIContent("P2", "Give the mount to Player 2."), "Button", GUILayout.Width(36))) _mountTarget = 1;
                GUILayout.Space(8);
                if (GUILayout.Button(new GUIContent("Refresh", "Rescan the build for available mount prefabs."), GUILayout.Width(70))) RebuildMountOptions();
            }
            else
            {
                GUILayout.Label("Swap a player's mount at any time.");
            }
            GUILayout.EndHorizontal();

            if (!_showMounts) return;

            _mountScroll = GUILayout.BeginScrollView(_mountScroll, GUILayout.Height(138));
            if (_mountOptions.Count == 0)
            {
                GUILayout.Label("(No mounts loaded yet - enter a run, then Refresh.)");
            }
            else
            {
                for (int i = 0; i < _mountOptions.Count; i += 2)
                {
                    GUILayout.BeginHorizontal();
                    DrawMountButton(_mountOptions[i]);
                    if (i + 1 < _mountOptions.Count) DrawMountButton(_mountOptions[i + 1]);
                    else GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawMountButton(Steed steed)
        {
            if (steed == null)
            {
                GUILayout.FlexibleSpace();
                return;
            }
            if (GUILayout.Button($"{steed.steedType}   ({steed.name})", GUILayout.Height(30), GUILayout.Width((_window.width - 32f) * 0.5f)))
                RidePlayer(_mountTarget, steed);
        }

        private void RebuildMountOptions()
        {
            _mountOptions.Clear();
            var seen = new HashSet<string>();
            var seenTypes = new HashSet<SteedType>();
            int total = 0, sceneInstances = 0, prefabs = 0, filtered = 0, duplicates = 0;
            foreach (var steed in Resources.FindObjectsOfTypeAll<Steed>())
            {
                if (steed == null) continue;
                total++;
                // Skip scene instances â€” those are the live mount(s) currently
                // in the world. Passing one of those to Player.Ride yanks it
                // out of its existing parent and the player + mount vanish.
                // Prefabs have scene.handle == 0.
                bool isPrefab = steed.gameObject.scene.handle == 0;
                if (isPrefab) prefabs++; else { sceneInstances++; continue; }
                TryAddMountOption(steed, seen, seenTypes, ref filtered, ref duplicates);
            }

            _mountOptions.Sort((a, b) =>
            {
                int byType = a.steedType.CompareTo(b.steedType);
                return byType != 0 ? byType : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });

            var missing = new List<string>();
            for (int i = 0; i < (int)SteedType.Total; i++)
            {
                var type = (SteedType)i;
                if (type == SteedType.Trap || type == SteedType.Barrier) continue;
                if (!seenTypes.Contains(type)) missing.Add(type.ToString());
            }
            Log($"Mount list: {_mountOptions.Count} options ({total} resources, {prefabs} prefabs, {sceneInstances} scene-instances, {filtered} filtered, {duplicates} duplicates).");
            if (missing.Count > 0) Log("Unavailable mount types: " + string.Join(", ", missing));
        }

        private void TryAddMountOption(Steed steed, HashSet<string> seen, HashSet<SteedType> seenTypes, ref int filtered, ref int duplicates)
        {
            if (steed == null) return;
            if (steed.steedType == SteedType.INVALID
                || steed.steedType == SteedType.Trap
                || steed.steedType == SteedType.Barrier)
            {
                filtered++;
                return;
            }
            var key = steed.steedType + "|" + steed.name;
            if (!seen.Add(key))
            {
                duplicates++;
                return;
            }
            seenTypes.Add(steed.steedType);
            _mountOptions.Add(steed);
        }

        private void RidePlayer(int playerId, Steed steedPrefab)
        {
            Player target = null;
            foreach (var p in Kingdom.Players.All)
            {
                if (p != null && p.playerId == playerId) { target = p; break; }
            }
            if (target == null)
            {
                Log($"Player {playerId + 1} not in scene - load into a run first.");
                return;
            }
            if (steedPrefab == null)
            {
                Log("Mount prefab missing.");
                return;
            }

            // Player.Ride needs a live scene instance, not a prefab â€” prefabs
            // never ran Awake/Start so their internal references are null and
            // Steed.SetMode(SteedMode, Player) NRE's. Instantiate the prefab
            // first, drop it on the player's current position, then ride that.
            Steed instance;
            try
            {
                instance = UnityEngine.Object.Instantiate(steedPrefab);
            }
            catch (System.Exception e)
            {
                Log($"Instantiate({steedPrefab.steedType}) failed: {e.GetType().Name}: {e.Message}");
                return;
            }

            instance.name = steedPrefab.name; // strip the "(Clone)" suffix Unity appends
            var tr = instance.transform;
            tr.position = target.transform.position;
            instance.gameObject.SetActive(true);

            try
            {
                target.Ride(instance, replace: true, applyToCampaign: true);
                Log($"Player {playerId + 1} now riding {steedPrefab.steedType}.");
            }
            catch (System.Exception e)
            {
                Log($"Ride failed: {e.GetType().Name}: {e.Message}");
                // Clean up the instance we created, otherwise it'd just sit
                // there orphaned at the player's position.
                UnityEngine.Object.Destroy(instance.gameObject);
            }
        }
    }
}


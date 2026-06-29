// One-time in-game popup shown on the first run after a fresh install: warns
// that mods can desync co-op and interfere with cloud saves. The owning loader
// passes an acknowledge callback that persists the "shown" pref — so if the
// player quits without dismissing, the popup returns next launch.
//
// IMGUI constraint (same as InGameConsole): never wrap GUILayout calls in
// try/catch (Layout/Repaint must commit matching control counts) and don't use
// stripped APIs like GUI.DoTextField. This popup only uses Label + Button.

using UnityEngine;

namespace KingdomMod.Loader.Console
{
    internal sealed class MultiplayerWarningPopup
    {
        private bool _visible;
        private Rect _window;
        private bool _positioned;
        private string _backupPath;
        private readonly System.Action _onAcknowledged;

        // Opaque window background. The default IMGUI window skin is semi-
        // transparent; we replace it with a solid colour via a 1x1 texture so
        // the dialog is fully readable. Built lazily during OnGUI (GUI.skin is
        // only valid then); falls back to the default skin if construction fails.
        private GUIStyle _windowStyle;
        private Texture2D _bg;
        private bool _styleFailed;

        private const float Width = 540f;
        private const float Height = 280f;

        public bool Visible => _visible;

        public MultiplayerWarningPopup(System.Action onAcknowledged)
        {
            _onAcknowledged = onAcknowledged;
        }

        public void Show() => _visible = true;

        // backupPath: where the loader copied the saves this run (may be empty
        // if the backup was skipped or failed); shown verbatim in the dialog.
        public void Show(string backupPath)
        {
            _backupPath = backupPath;
            _visible = true;
        }

        public void OnGUI()
        {
            if (!_visible) return;

            // The game hides/locks and can replace the hardware cursor during
            // play; force a known arrow so the player can click the button.
            UiCursor.Apply();

            if (!_positioned) { PositionCenter(); _positioned = true; }

            // NB: no dim-background layer - GUI.DrawTexture / Texture2D.whiteTexture
            // are stripped from this Unity build ("Method unstripping failed") and
            // throw every frame. We instead make the window itself opaque.
            EnsureStyle();
            if (_windowStyle != null)
                _window = GUILayout.Window(0x4D50_5750, _window, (GUI.WindowFunction)Draw, "KingdomMod  —  Heads up", _windowStyle);
            else
                _window = GUILayout.Window(0x4D50_5750, _window, (GUI.WindowFunction)Draw, "KingdomMod  —  Heads up");
        }

        private void EnsureStyle()
        {
            if (_windowStyle != null || _styleFailed) return;
            try
            {
                _bg = new Texture2D(1, 1);
                _bg.SetPixel(0, 0, new Color(0.11f, 0.11f, 0.13f, 1f)); // solid, alpha = 1
                _bg.Apply();

                var s = new GUIStyle(GUI.skin.window);
                s.normal.background   = _bg;
                s.onNormal.background = _bg;
                s.border = new RectOffset(0, 0, 0, 0); // 1x1 fill, no 9-slice
                _windowStyle = s;
            }
            catch { _styleFailed = true; }
        }

        private void PositionCenter()
        {
            int sw = Screen.width  > 0 ? Screen.width  : 1920;
            int sh = Screen.height > 0 ? Screen.height : 1080;
            // Horizontally centered, but raised 20% of screen height above center.
            float y = (sh - Height) / 2f - sh * 0.20f;
            _window = new Rect((sw - Width) / 2f, y, Width, Height);
        }

        private void Draw(int id)
        {
            GUILayout.Space(8);
            string backup = string.IsNullOrEmpty(_backupPath)
                ? "Your local save was backed up on launch."
                : "Your local save was backed up to:\n" + _backupPath;
            GUILayout.Label(
                "Mods can DESYNC co-op partners and may interfere with cloud saves.\n\n" +
                "Treat this session as single-player / offline unless every player runs " +
                "the exact same mods.\n\n" + backup);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("I understand", GUILayout.Width(150), GUILayout.Height(30)))
            {
                _visible = false;
                UiCursor.Release();
                if (_onAcknowledged != null) _onAcknowledged();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
    }
}

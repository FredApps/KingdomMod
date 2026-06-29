// Shown once after a mod (re)install causes a fresh save backup: tells the
// player a new backup was taken and where it lives. Distinct from the first-run
// MultiplayerWarningPopup (that one only fires on a brand-new install and
// already lists the path) - this one re-fires on every later mod update so the
// player always knows the latest backup location.
//
// IMGUI constraint (same as the other popups): never wrap GUILayout calls in
// try/catch and don't touch stripped APIs (GUI.DoTextField, GUI.DrawTexture).
// Label + Button only.

using UnityEngine;

namespace KingdomMod.Loader.Console
{
    internal sealed class BackupNoticePopup
    {
        private bool _visible;
        private Rect _window;
        private bool _positioned;
        private string _backupPath;

        // Opaque window background - see MultiplayerWarningPopup for the rationale
        // (the default skin is semi-transparent; the 1x1 texture makes it solid).
        private GUIStyle _windowStyle;
        private Texture2D _bg;
        private bool _styleFailed;

        private const float Width = 540f;
        private const float Height = 220f;

        public bool Visible => _visible;

        // backupPath: where the loader copied the saves for this install.
        public void Show(string backupPath)
        {
            _backupPath = backupPath;
            _visible = true;
        }

        public void OnGUI()
        {
            if (!_visible) return;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (!_positioned) { PositionCenter(); _positioned = true; }

            EnsureStyle();
            if (_windowStyle != null)
                _window = GUILayout.Window(0x4D50_4255, _window, (GUI.WindowFunction)Draw, "KingdomMod  —  Save backed up", _windowStyle);
            else
                _window = GUILayout.Window(0x4D50_4255, _window, (GUI.WindowFunction)Draw, "KingdomMod  —  Save backed up");
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
                s.border = new RectOffset(0, 0, 0, 0);
                _windowStyle = s;
            }
            catch { _styleFailed = true; }
        }

        private void PositionCenter()
        {
            int sw = Screen.width  > 0 ? Screen.width  : 1920;
            int sh = Screen.height > 0 ? Screen.height : 1080;
            float y = (sh - Height) / 2f - sh * 0.20f;
            _window = new Rect((sw - Width) / 2f, y, Width, Height);
        }

        private void Draw(int id)
        {
            GUILayout.Space(8);
            string where = string.IsNullOrEmpty(_backupPath)
                ? "Your save was backed up on launch."
                : "Your save was backed up to:\n" + _backupPath;
            GUILayout.Label(
                "A new mod install was detected, so KingdomMod made a fresh copy of " +
                "your save files first.\n\n" + where);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("OK", GUILayout.Width(150), GUILayout.Height(30)))
                _visible = false;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
    }
}

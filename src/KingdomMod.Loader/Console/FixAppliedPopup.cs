using UnityEngine;

namespace KingdomMod.Loader.Console
{
    internal sealed class FixAppliedPopup
    {
        private bool _visible;
        private Rect _window;
        private bool _positioned;
        private string _title = "Fix applied";
        private string _message = "";
        private GUIStyle _windowStyle;
        private Texture2D _bg;
        private bool _styleFailed;

        private const float Width = 520f;
        private const float Height = 180f;

        public bool Visible => _visible;

        public void Show(string title, string message)
        {
            _title = string.IsNullOrWhiteSpace(title) ? "Fix applied" : title;
            _message = message ?? "";
            _positioned = false;
            _visible = true;
        }

        public void OnGUI()
        {
            if (!_visible) return;

            UiCursor.Apply();

            if (!_positioned) { Position(); _positioned = true; }

            EnsureStyle();
            string heading = "KingdomMod  -  " + _title;
            if (_windowStyle != null)
                _window = GUILayout.Window(0x4B4D_4649, _window, (GUI.WindowFunction)Draw, heading, _windowStyle);
            else
                _window = GUILayout.Window(0x4B4D_4649, _window, (GUI.WindowFunction)Draw, heading);
        }

        private void EnsureStyle()
        {
            if (_windowStyle != null || _styleFailed) return;
            try
            {
                _bg = new Texture2D(1, 1);
                _bg.SetPixel(0, 0, new Color(0.10f, 0.12f, 0.14f, 1f));
                _bg.Apply();

                var s = new GUIStyle(GUI.skin.window);
                s.normal.background = _bg;
                s.onNormal.background = _bg;
                s.border = new RectOffset(0, 0, 0, 0);
                _windowStyle = s;
            }
            catch { _styleFailed = true; }
        }

        private void Position()
        {
            int sw = Screen.width > 0 ? Screen.width : 1920;
            int sh = Screen.height > 0 ? Screen.height : 1080;
            float y = (sh - Height) / 2f + sh * 0.18f;
            _window = new Rect((sw - Width) / 2f, y, Width, Height);
        }

        private void Draw(int id)
        {
            GUILayout.Space(8);
            GUILayout.Label(_message);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("OK", GUILayout.Width(140), GUILayout.Height(28)))
            {
                _visible = false;
                UiCursor.Release();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
    }
}

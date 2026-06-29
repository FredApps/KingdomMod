// CoinCounterOverlay — when a coin cheat is active we suppress the visible
// coin pouch. The vanilla bag is the player's only indication of how much
// they're carrying, so we replace it with a small glyph drawn in the top-
// right of the screen (where KTC's HUD coin display sits):
//
//   No tax    → the live coin count.
//   Infinite  → an infinity symbol (∞).
//   None      → overlay draws nothing (vanilla bag is back).
//
// World-space anchoring above the player was tried first but didn't render
// reliably — Camera.main is unreliable on KTC (the gameplay camera isn't
// always tagged MainCamera), and even when it was, the LocalWallet /
// GetComponentInParent<Player>() lookup raced scene transitions. Fixed
// screen coords sidestep all of that and match where the user expects the
// readout to be.

using UnityEngine;

namespace KingdomMod.Loader.Console
{
    internal sealed class CoinCounterOverlay
    {
        private GUIStyle _style;
        private GUIStyle _shadow;

        // Distance from the screen edges. Matches roughly where KTC's own
        // currency HUD would sit; tweak in one place.
        private const float MarginRight = 56f;
        private const float MarginTop   = 48f;
        private const float BoxWidth    = 160f;
        private const float BoxHeight   = 56f;

        public void OnGUI()
        {
            var mode = Kingdom.Economy.CoinCheat;
            if (mode == CoinCheatMode.None) return;

            EnsureStyles();

            string text = (mode == CoinCheatMode.Infinite) ? "∞" : Kingdom.Economy.Coins.ToString();

            // Right-aligned anchor — box edge sits MarginRight from the screen
            // right edge. Drop-shadow sits 2 px down-right so the glyph reads
            // against bright backgrounds (snow, daylight forest).
            float x = Screen.width  - MarginRight - BoxWidth;
            float y = MarginTop;
            var rect   = new Rect(x,        y,        BoxWidth, BoxHeight);
            var shadow = new Rect(x + 2f,   y + 2f,   BoxWidth, BoxHeight);

            GUI.Label(shadow, text, _shadow);
            GUI.Label(rect,   text, _style);
        }

        private void EnsureStyles()
        {
            if (_style != null) return;
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 32,
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.84f, 0.0f, 1f) }, // coin yellow
            };
            _shadow = new GUIStyle(_style) { normal = { textColor = new Color(0f, 0f, 0f, 0.85f) } };
        }
    }
}

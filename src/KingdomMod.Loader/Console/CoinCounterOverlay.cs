// CoinCounterOverlay - when a coin cheat is active we suppress the visible
// coin pouch. The vanilla bag is the player's only indication of how much
// they're carrying, so we replace it with a small glyph drawn in the top-
// right of the screen (where KTC's HUD coin display sits):
//
//   No drops  -> the live coin count, plus nonzero item counters.
//   Infinite  -> infinity markers for held wallet currencies.
//   None      -> overlay draws nothing (vanilla bag is back).
//
// World-space anchoring above the player was tried first but didn't render
// reliably: Camera.main is unreliable on KTC, and the LocalWallet lookup can
// race scene transitions. Fixed screen coords sidestep that and match where
// the user expects the readout to be.

using Il2Cpp;
using UnityEngine;

namespace KingdomMod.Loader.Console
{
    internal sealed class CoinCounterOverlay
    {
        private GUIStyle _coinStyle;
        private GUIStyle _coinShadow;
        private GUIStyle _itemStyle;
        private GUIStyle _itemShadow;

        // Distance from the screen edges. Matches roughly where KTC's own
        // currency HUD would sit; tweak in one place.
        private const float MarginRight = 56f;
        private const float MarginTop = 48f;
        private const float BoxWidth = 160f;
        private const float BoxHeight = 56f;
        private const float ItemBoxWidth = 68f;
        private const float ItemSpacing = 8f;

        private static readonly CurrencyCounter[] ItemCounters =
        {
            new("GEM", CurrencyType.Gems, new Color(0.55f, 0.95f, 1f, 1f)),
            new("SOUL", CurrencyType.Shades, new Color(0.72f, 0.82f, 1f, 1f)),
            new("SK", CurrencyType.Skulls, new Color(1f, 0.36f, 0.36f, 1f)),
            new("ITEM", CurrencyType.Merchandise, new Color(1f, 0.72f, 0.35f, 1f)),
            new("CND", CurrencyType.Candle, new Color(1f, 0.92f, 0.62f, 1f)),
            new("EGG", CurrencyType.Egg, new Color(0.78f, 1f, 0.64f, 1f)),
        };

        public void OnGUI()
        {
            var mode = Kingdom.Economy.CoinCheat;
            if (mode == CoinCheatMode.None) return;

            EnsureStyles();

            string text = (mode == CoinCheatMode.Infinite) ? "\u221e" : Kingdom.Economy.Coins.ToString();

            // Right-aligned anchor: box edge sits MarginRight from the screen
            // right edge. Drop-shadow sits 2 px down-right so the glyph reads
            // against bright backgrounds (snow, daylight forest).
            float x = Screen.width - MarginRight - BoxWidth;
            float y = MarginTop;
            var rect = new Rect(x, y, BoxWidth, BoxHeight);
            var shadow = new Rect(x + 2f, y + 2f, BoxWidth, BoxHeight);

            DrawItemCounters(mode, x, y);

            GUI.Label(shadow, text, _coinShadow);
            GUI.Label(rect, text, _coinStyle);
        }

        private void EnsureStyles()
        {
            if (_coinStyle != null) return;

            _coinStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 32,
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.84f, 0.0f, 1f) },
            };
            _coinShadow = new GUIStyle(_coinStyle)
            {
                normal = { textColor = new Color(0f, 0f, 0f, 0.85f) },
            };
            _itemStyle = new GUIStyle(_coinStyle)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleRight,
            };
            _itemShadow = new GUIStyle(_itemStyle)
            {
                normal = { textColor = new Color(0f, 0f, 0f, 0.85f) },
            };
        }

        private void DrawItemCounters(CoinCheatMode mode, float coinX, float y)
        {
            var wallet = Kingdom.Economy.LocalWallet;
            if (wallet == null) return;

            float x = coinX - ItemSpacing - ItemBoxWidth;
            for (int i = 0; i < ItemCounters.Length; i++)
            {
                var counter = ItemCounters[i];
                int value = ReadCurrency(wallet, counter.Type);
                if (value <= 0) continue;

                string text = mode == CoinCheatMode.Infinite
                    ? $"{counter.Label} \u221e"
                    : $"{counter.Label} {value}";

                _itemStyle.normal.textColor = counter.Color;
                var rect = new Rect(x, y + 2f, ItemBoxWidth, BoxHeight);
                var shadow = new Rect(x + 2f, y + 4f, ItemBoxWidth, BoxHeight);

                GUI.Label(shadow, text, _itemShadow);
                GUI.Label(rect, text, _itemStyle);

                x -= ItemBoxWidth + ItemSpacing;
            }
        }

        private static int ReadCurrency(Wallet wallet, CurrencyType type)
        {
            try { return wallet.GetCurrency(type); }
            catch { return 0; }
        }

        private readonly struct CurrencyCounter
        {
            public CurrencyCounter(string label, CurrencyType type, Color color)
            {
                Label = label;
                Type = type;
                Color = color;
            }

            public string Label { get; }
            public CurrencyType Type { get; }
            public Color Color { get; }
        }
    }
}

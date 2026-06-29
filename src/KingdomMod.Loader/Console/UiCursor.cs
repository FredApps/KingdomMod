using UnityEngine;

namespace KingdomMod.Loader.Console
{
    internal static class UiCursor
    {
        private static Texture2D _arrowTexture;

        public static void Apply()
        {
            Cursor.SetCursor(ArrowTexture, Vector2.zero, CursorMode.Auto);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public static void Release()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        private static Texture2D ArrowTexture
        {
            get
            {
                if (_arrowTexture == null) _arrowTexture = CreateArrowTexture();
                return _arrowTexture;
            }
        }

        private static Texture2D CreateArrowTexture()
        {
            string[] rows =
            {
                "X...............",
                "XX..............",
                "XOX.............",
                "XOOX............",
                "XOOOX...........",
                "XOOOOX..........",
                "XOOOOOX.........",
                "XOOOOOOX........",
                "XOOOOOOOX.......",
                "XOOOOOOOOX......",
                "XOOOOOOOOOX.....",
                "XOOOOOXXXXX.....",
                "XOOXOOX.........",
                "XOX.XOOX........",
                "XX..XOOX........",
                "X....XOOX.......",
                ".....XOOX.......",
                "......XX........"
            };

            int height = rows.Length;
            int width = rows[0].Length;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var clear = new Color(0f, 0f, 0f, 0f);
            var outline = Color.black;
            var fill = Color.white;

            for (int y = 0; y < height; y++)
            {
                string row = rows[y];
                for (int x = 0; x < width; x++)
                {
                    char pixel = row[x];
                    Color color = pixel == 'X' ? outline : pixel == 'O' ? fill : clear;
                    texture.SetPixel(x, height - y - 1, color);
                }
            }

            texture.Apply(false, false);
            return texture;
        }
    }
}

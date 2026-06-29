// Example: ReskinPack — Tier-4 sprite replacement.
//
// This mod reads a "pack" folder at <mod>/pack/sprites/.  When the user drops
// PNGs named after a SpriteRenderer's current sprite (e.g. banner.png), this mod
// swaps that sprite's texture at runtime — no game asset is shipped or extracted.
//
// IMPORTANT: KingdomMod ships NO game art.  Pack authors must supply their own
// drawings; ripping the originals and redistributing them is not legal.

using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using KingdomMod;

[assembly: MelonInfo(typeof(KingdomMod.Examples.ReskinPack.ReskinMod), "Reskin Pack", "0.1.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.ReskinPack
{
    public sealed class ReskinMod : MelonMod
    {
        private readonly Dictionary<string, Sprite> _replacements = new();

        public override void OnInitializeMelon()
        {
            var loadedAnyPack = false;
            foreach (var pack in Kingdom.Packs.DiscoverPacks(MelonEnvironment.ModsDirectory))
            {
                if (!pack.HasSprites)
                    continue;

                loadedAnyPack = true;
                foreach (var path in Directory.EnumerateFiles(pack.SpritesDirectory, "*.png"))
                {
                    var key = Path.GetFileNameWithoutExtension(path);
                    var tex = Kingdom.Packs.LoadTexture(path);
                    var sprite = Kingdom.Packs.MakeSprite(tex);
                    if (sprite != null)
                    {
                        _replacements[key] = sprite;
                        LoggerInstance.Msg($"  reskin: {pack.Name}/{key}.png");
                    }
                }
            }

            if (!loadedAnyPack)
            {
                var packDir = Path.Combine(MelonEnvironment.ModsDirectory, "ReskinPack", "pack", "sprites");
                Directory.CreateDirectory(packDir);
                LoggerInstance.Msg($"Drop PNGs named like the in-game sprite into: {packDir}");
                return;
            }

            LoggerInstance.Msg($"Loaded {_replacements.Count} sprite replacement(s).");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // Defer one frame so all SpriteRenderers in the new scene are awake.
            MelonCoroutines.Start(ApplyAfterFrame());
        }

        private System.Collections.IEnumerator ApplyAfterFrame()
        {
            yield return null;   // skip a frame
            if (_replacements.Count == 0) yield break;
            int swapped = 0;
            foreach (var r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (r == null || r.sprite == null) continue;
                if (_replacements.TryGetValue(r.sprite.name, out var rep))
                {
                    r.sprite = rep;
                    swapped++;
                }
            }
            if (swapped > 0) LoggerInstance.Msg($"Reskinned {swapped} SpriteRenderer(s) in '{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}'.");
        }
    }
}

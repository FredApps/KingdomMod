using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using KingdomMod;
using KingdomMod.Examples.ReskinPack;
using MelonLoader.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        _checks++;
        Console.WriteLine($"PASS: {description}");
    }

    private static Sprite Frame(string name, float ppu = 24, float pivotX = 0.25f)
    {
        var texture = new Texture2D(32, 16, TextureFormat.RGBA32, false);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 16), new Vector2(pivotX, 0), ppu, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        return sprite;
    }

    public static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "KingdomMod-ReskinTests-" + Guid.NewGuid());
        try
        {
            var sprites = Path.Combine(root, "ReskinPack", "pack", "sprites");
            Directory.CreateDirectory(sprites);
            foreach (var name in new[] { "player_armour_king_walk_0.png", "player_armour_king_walk_1.PNG", "never_seen.png" })
                File.WriteAllBytes(Path.Combine(sprites, name), new byte[] { 32, 16 });
            File.WriteAllBytes(Path.Combine(sprites, "invalid.png"), new byte[] { 0 });
            MelonEnvironment.ModsDirectory = root;
            MelonEnvironment.UserDataDirectory = Path.Combine(root, "UserData");
            var mod = new ReskinMod();
            mod.OnInitializeMelon();
            Check(mod.LoggerInstance.Messages.Any(m => m.StartsWith("Loaded 3 ")), "Valid files counted independently of runtime matches; uppercase PNG accepted");
            Check(mod.LoggerInstance.Messages.Any(m => m.Contains("Could not decode")), "Invalid image reports a failure without aborting pack load");
            var idle = Frame("player_armour_king_idle_0");
            var walk0 = Frame("player_armour_king_walk_0");
            var walk1 = Frame("player_armour_king_walk_1");
            var ruler = new SpriteRenderer { sprite = idle };
            mod.OnSceneWasInitialized(1, "Test island");
            mod.OnLateUpdate();
            Check(ruler.sprite == idle, "Unmatched idle frame remains vanilla");
            ruler.sprite = walk0;
            Time.unscaledTime = 0.1f;
            mod.OnLateUpdate();
            var replacement0 = ruler.sprite;
            Check(replacement0 != walk0 && replacement0.name == walk0.name, "Walking frame replaces after initial idle scene scan");
            Check(replacement0.pixelsPerUnit == 24 && replacement0.pivot.x == 8 && replacement0.pivot.y == 0, "Original PPU and normalized pivot preserved");
            Check(replacement0.hideFlags == HideFlags.HideAndDontSave && replacement0.texture.hideFlags == HideFlags.HideAndDontSave, "Both replacement sprite and loaded texture survive scene unloading");
            ruler.sprite = walk1;
            mod.OnLateUpdate();
            Check(ruler.sprite != walk1 && ruler.sprite.name == walk1.name, "Next animation frame selects its own replacement");
            ruler.sprite = walk0;
            mod.OnLateUpdate();
            Check(ruler.sprite == replacement0, "Animation overwrites reuse the cached replacement");
            mod.OnLateUpdate();
            Check(ruler.sprite == replacement0, "Already replaced sprite is stable without recursive replacement");
            Check(Object.DiscoveryCalls == 1, "Repeated frames do not perform a scene-wide renderer search");
            var second = new SpriteRenderer { sprite = walk0 };
            Time.unscaledTime = 0.6f;
            mod.OnLateUpdate();
            Check(second.sprite == replacement0, "Late-spawned renderer discovered and reskinned");
            var disabled = new SpriteRenderer { sprite = walk0, enabled = false };
            Time.unscaledTime = 1.2f;
            mod.OnLateUpdate();
            Check(disabled.sprite == walk0, "Disabled renderer left untouched");
            disabled.enabled = true;
            mod.OnLateUpdate();
            Check(disabled.sprite == replacement0, "Re-enabled cached renderer picks up replacement immediately");
            var variant = Frame(walk0.name, 32, 0.75f);
            second.sprite = variant;
            mod.OnLateUpdate();
            Check(second.sprite != replacement0 && second.sprite.pixelsPerUnit == 32 && second.sprite.pivot.x == 24, "Same-named source sprites retain independent scale and pivots");
            Time.unscaledTime = 6;
            mod.OnLateUpdate();
            mod.OnUpdate();
            Check(mod.LoggerInstance.Messages.Any(m => m.Contains("Not observed yet") && m.Contains("never_seen")), "Unmatched diagnostic identifies missing runtime names");
            Check(mod.LoggerInstance.Messages.Count(m => m.StartsWith("Matched sprite")) == 2, "Runtime match logging is bounded to once per name");
            Object.Destroy(ruler);
            var nextScene = new SpriteRenderer { sprite = walk0 };
            mod.OnSceneWasInitialized(2, "Next island");
            mod.OnLateUpdate();
            Check(nextScene.sprite == replacement0, "Scene transition resets discovery and reuses surviving assets");

            var clone = Frame(" PLAYER_ARMOUR_KING_WALK_0 (Clone)(Clone) ");
            nextScene.sprite = clone;
            mod.OnLateUpdate();
            Check(nextScene.sprite != clone && nextScene.sprite.texture == replacement0.texture,
                "Atlas clone suffix, case and whitespace differences match the original filename");
            nextScene.sprite = walk1; // Simulate a game LateUpdate that ran after the mod.
            ReskinMod.BeforeCameraRender();
            Check(nextScene.sprite != walk1 && nextScene.sprite.name == walk1.name,
                "Camera callback repairs sprite assignments made after mod LateUpdate");
            var renamed = Frame("temporary runtime name");
            nextScene.sprite = renamed;
            mod.OnLateUpdate();
            renamed.name = walk0.name;
            Time.unscaledTime = 7;
            mod.OnLateUpdate();
            Check(nextScene.sprite != renamed, "Negative cache expires when a runtime sprite is named later");
            var biome = Frame("player_armour_king_walk_norselands_0");
            nextScene.sprite = biome;
            mod.OnLateUpdate();
            Check(nextScene.sprite == biome, "No guessed biome or animation-family aliases are applied");
            mod.OnApplicationQuit();
            Check(!Object.Live.Contains(replacement0), "Generated replacement sprites released on shutdown");
            nextScene.sprite = walk0;
            ReskinMod.BeforeCameraRender();
            Check(nextScene.sprite == walk0 && HarmonyLib.Harmony.Unpatched,
                "Render hook released on shutdown");
            var logPath = Path.Combine(MelonEnvironment.UserDataDirectory, "KingdomMod", "logs", "reskin-latest.jsonl");
            var entries = File.ReadAllLines(logPath).Select(line => JsonDocument.Parse(line)).ToArray();
            Check(entries.Any(e => e.RootElement.GetProperty("entry").GetProperty("kind").GetString() == "observed"),
                "Diagnostic JSONL records the real sprite names seen by the replacement loop");
            Check(entries.Any(e => e.RootElement.GetProperty("entry").TryGetProperty("sprite", out var s)
                && s.GetString() == "player_armour_king_walk_norselands_0"), "Diagnostics capture unmatched biome names");
            foreach (var entry in entries) entry.Dispose();
            var nested = Path.Combine(sprites, "monarch");
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(Path.Combine(nested, "player_armour_king_walk_0(Clone).png"), new byte[] { 16, 16 });
            var restarted = new ReskinMod();
            restarted.OnInitializeMelon();
            Check(restarted.LoggerInstance.Messages.Any(m => m.StartsWith("Loaded 4 ")),
                "Replacement images in nested sprite folders are loaded");
            var exactClone = Frame("player_armour_king_walk_0(Clone)");
            nextScene.sprite = exactClone;
            restarted.OnLateUpdate();
            Check(nextScene.sprite != exactClone && nextScene.sprite.texture.width == 16,
                "Explicit clone filename takes precedence over normalized base filename");
            restarted.OnApplicationQuit();
            Check(!File.ReadAllText(logPath).Contains("Test island"), "Diagnostic file is overwritten on restart");
            Console.WriteLine($"All {_checks} regression checks passed (simulated Unity boundary).");
        }
        finally
        {
            Kingdom.Packs.ClearCache();
            Directory.Delete(root, true);
        }
    }
}

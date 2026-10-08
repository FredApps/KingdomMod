// ReskinPack replaces individual sprite frames without changing vanilla animation.
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using KingdomMod;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(KingdomMod.Examples.ReskinPack.ReskinMod), "Reskin Pack", "0.1.1", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.ReskinPack
{
    public sealed class ReskinMod : MelonMod
    {
        private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
        private readonly Dictionary<int, ReplacementFrame> _frames = new();
        private readonly Dictionary<int, Sprite> _unmatchedFrames = new();
        private readonly HashSet<int> _replacementIds = new();
        private readonly HashSet<string> _matchedNames = new(StringComparer.Ordinal);
        private readonly List<SpriteRenderer> _renderers = new();
        private float _nextDiscovery;
        private float _nextErrorLog;
        private float _reportAt;
        private bool _reportPending;
        private string _sceneName = "startup";

        public override void OnInitializeMelon()
        {
            var loadedAnyPack = false;
            foreach (var pack in Kingdom.Packs.DiscoverPacks(MelonEnvironment.ModsDirectory))
            {
                if (!pack.HasSprites)
                    continue;

                loadedAnyPack = true;
                foreach (var path in Directory.EnumerateFiles(pack.SpritesDirectory))
                {
                    if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var key = Path.GetFileNameWithoutExtension(path);
                    try
                    {
                        var texture = Kingdom.Packs.LoadTexture(path);
                        if (texture == null)
                        {
                            LoggerInstance.Warning($"Could not decode replacement PNG: {path}");
                            continue;
                        }
                        if (_textures.ContainsKey(key))
                            LoggerInstance.Warning($"Duplicate replacement '{key}'; using {path}.");
                        _textures[key] = texture;
                        LoggerInstance.Msg($"  reskin: {pack.Name}/{key}.png");
                    }
                    catch (Exception ex)
                    {
                        LoggerInstance.Warning($"Could not load replacement '{path}': {ex.Message}");
                    }
                }
            }

            if (!loadedAnyPack)
            {
                var packDir = Path.Combine(MelonEnvironment.ModsDirectory, "ReskinPack", "pack", "sprites");
                Directory.CreateDirectory(packDir);
                LoggerInstance.Msg($"Drop PNGs named like the in-game sprite into: {packDir}");
            }
            LoggerInstance.Msg($"Loaded {_textures.Count} sprite replacement(s). This is the file count; 'Matched sprite' messages confirm in-game replacement.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            _sceneName = sceneName;
            _renderers.Clear();
            _unmatchedFrames.Clear();
            _nextDiscovery = 0;
            _reportAt = Time.unscaledTime + 5f;
            _reportPending = true;
        }

        public override void OnLateUpdate()
        {
            if (_textures.Count == 0) return;
            var now = Time.unscaledTime;
            try
            {
                // Cache discovery, but swap the current frame after animation every frame.
                if (now >= _nextDiscovery)
                {
                    _renderers.Clear();
                    foreach (var renderer in Object.FindObjectsByType<SpriteRenderer>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None))
                        _renderers.Add(renderer);
                    _nextDiscovery = now + 0.5f;
                }
                foreach (var renderer in _renderers)
                {
                    try
                    {
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                            continue;
                        ApplyFrame(renderer);
                    }
                    catch (Exception ex)
                    {
                        ReportFailure(now, ex);
                    }
                }
                if (_reportPending && now >= _reportAt)
                {
                    _reportPending = false;
                    var unmatched = new List<string>();
                    foreach (var name in _textures.Keys)
                        if (!_matchedNames.Contains(name)) unmatched.Add(name);
                    LoggerInstance.Msg($"Reskin matches so far: {_matchedNames.Count}/{_textures.Count} names; scene '{_sceneName}'.");
                    if (unmatched.Count > 0)
                        LoggerInstance.Msg($"Not observed yet (may appear later during animation or another monarch/scene): {string.Join(", ", unmatched)}");
                }
            }
            catch (Exception ex)
            {
                ReportFailure(now, ex);
            }
        }

        private void ReportFailure(float now, Exception ex)
        {
            if (now < _nextErrorLog) return;
            LoggerInstance.Warning($"Sprite replacement failed in '{_sceneName}': {ex}");
            _nextErrorLog = now + 10f;
        }

        private void ApplyFrame(SpriteRenderer renderer)
        {
            var original = renderer.sprite;
            if (original == null) return;
            var id = original.GetInstanceID();
            if (_replacementIds.Contains(id)) return;
            if (_unmatchedFrames.TryGetValue(id, out var unmatched) && unmatched == original) return;

            Sprite replacement;
            if (_frames.TryGetValue(id, out var frame) && frame.Original == original && frame.Replacement != null)
            {
                replacement = frame.Replacement;
            }
            else
            {
                var name = original.name;
                if (!_textures.TryGetValue(name, out var texture))
                {
                    _unmatchedFrames[id] = original;
                    return;
                }
                var rect = original.rect;
                if (rect.width <= 0 || rect.height <= 0) return;
                var pivot = new Vector2(original.pivot.x / rect.width, original.pivot.y / rect.height);
                replacement = Kingdom.Packs.MakeSprite(texture, original.pixelsPerUnit, pivot);
                if (replacement == null) return;
                replacement.name = name;
                if (frame != null && frame.Replacement != null)
                {
                    _replacementIds.Remove(frame.Replacement.GetInstanceID());
                    Object.Destroy(frame.Replacement);
                }
                _frames[id] = new ReplacementFrame(original, replacement);
                _replacementIds.Add(replacement.GetInstanceID());
                if (texture.width != rect.width || texture.height != rect.height)
                    LoggerInstance.Warning($"Replacement '{name}' is {texture.width}x{texture.height}; original frame is {rect.width}x{rect.height}. Match frame dimensions/padding to preserve size and alignment.");
            }
            renderer.sprite = replacement;
            if (_matchedNames.Add(replacement.name))
                LoggerInstance.Msg($"Matched sprite '{replacement.name}' on '{renderer.gameObject.name}' in scene '{_sceneName}'. Animation frames are reapplied in LateUpdate.");
        }

        public override void OnApplicationQuit()
        {
            foreach (var frame in _frames.Values)
                if (frame.Replacement != null) Object.Destroy(frame.Replacement);
            _frames.Clear();
            _unmatchedFrames.Clear();
            _replacementIds.Clear();
            _renderers.Clear();
        }

        private sealed class ReplacementFrame
        {
            public readonly Sprite Original;
            public readonly Sprite Replacement;

            public ReplacementFrame(Sprite original, Sprite replacement)
            {
                Original = original;
                Replacement = replacement;
            }
        }
    }
}

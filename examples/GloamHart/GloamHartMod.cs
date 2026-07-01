using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(KingdomMod.Examples.GloamHart.GloamHartMod), "Gloam Hart", "0.1.0", "KingdomMod contributors")]
[assembly: MelonGame("noio", "KingdomTwoCrowns")]

namespace KingdomMod.Examples.GloamHart
{
    public sealed class GloamHartMod : MelonMod
    {
        private const float PixelsPerUnit = 16f;
        private static readonly Dictionary<string, Sprite[]> Frames = new();
        private static readonly Dictionary<IntPtr, GloamHartVisual> Visuals = new();

        public override void OnInitializeMelon()
        {
            LoadSprites();
            Kingdom.CustomMounts.Register(
                "gloam_hart",
                "Gloam Hart",
                "A luminous forest hart: stag handling, gentle stamina, deer attraction, and original animated sprites.",
                CreateMount,
                "Reindeer/Stag");
            LoggerInstance.Msg($"Gloam Hart registered with {CountFrames()} embedded sprite frames.");
        }

        public override void OnUpdate()
        {
            if (Visuals.Count == 0) return;
            var stale = new List<IntPtr>();
            foreach (var pair in Visuals)
            {
                if (!pair.Value.Update())
                    stale.Add(pair.Key);
            }
            for (int i = 0; i < stale.Count; i++)
                Visuals.Remove(stale[i]);
        }

        private static Steed CreateMount(Player player, Action<string> log)
        {
            EnsureSprites(log);
            var basePrefab = FindBasePrefab(log);
            if (basePrefab == null) return null;

            Steed steed;
            try
            {
                steed = Object.Instantiate(basePrefab);
            }
            catch (Exception e)
            {
                log?.Invoke($"Gloam Hart: Instantiate failed: {e.GetType().Name}: {e.Message}");
                return null;
            }

            steed.name = "Gloam Hart";
            steed.transform.position = player.transform.position;
            steed.gameObject.SetActive(true);
            ApplyStats(steed);
            AttachVisual(steed, log);
            return steed;
        }

        private static void ApplyStats(Steed steed)
        {
            steed.walkSpeed = 1.75f;
            steed.runSpeed = 3.95f;
            steed.forestSpeedMultiplier = 1.18f;
            steed.walkStaminaRate = 0.04f;
            steed.runStaminaRate = -0.075f;
            steed.glideStaminaRate = 0f;
            steed.standStaminaRate = 0.18f;
            steed.reserveStamina = 0.3f;
            steed.reserveProbability = 0.6f;
            steed.rearingDuration = 1.0f;
            steed.rearingCooldown = 9.0f;
            steed.canEat = true;
            steed.eatDelay = 0.5f;
            steed.eatFullStaminaDelay = 5.0f;
            steed.eatDuration = 3.0f;
            steed.onlyEatsAtNight = false;
            steed.wellFedDuration = 50.0f;
            steed.tiredDuration = 22.0f;
            steed.eatAmbientThreshold = 0f;
            steed.attractsDeer = true;
            steed.wanderRange = 5f;
            steed.recolorToCoatOfArms = false;
            steed.hasBirthAnim = false;
            steed.disableSteedSwitching = false;
            steed.resumesGallopingAfterUsingAbility = true;
            steed.forwardAnimsToRuler = false;
        }

        private static void AttachVisual(Steed steed, Action<string> log)
        {
            // The steed prefab renders the mounted ruler (monarch) and crowns
            // through GameObjects parented under the steed itself (riderAnchor,
            // _riderObjectPairs, _crowns). Those renderers must stay enabled, or
            // the monarch turns invisible while riding. Only the steed's own body
            // renderers get hidden so the custom overlay can replace them.
            var keep = new HashSet<IntPtr>();
            CollectRulerRenderers(steed, keep);

            SpriteRenderer reference = null;
            foreach (var renderer in steed.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer == null) continue;
                if (renderer.Pointer != IntPtr.Zero && keep.Contains(renderer.Pointer)) continue;
                if (reference == null && renderer.sprite != null)
                    reference = renderer;
                renderer.enabled = false;
            }

            var visualObject = new GameObject("GloamHartVisual");
            // Parent under the body renderer's own parent so its local position
            // and scale share the body's coordinate space; falling back to the
            // steed root keeps a sane placement when no body renderer is found.
            var anchor = reference != null ? reference.transform.parent : steed.transform;
            visualObject.transform.SetParent(anchor != null ? anchor : steed.transform, false);
            visualObject.transform.localPosition = reference != null
                ? reference.transform.localPosition
                : new Vector3(0f, 0.3f, 0f);
            visualObject.transform.localScale = Vector3.one;

            var renderer2 = visualObject.AddComponent<SpriteRenderer>();
            if (reference != null)
            {
                renderer2.sortingLayerID = reference.sortingLayerID;
                renderer2.sortingOrder = reference.sortingOrder + 1;
            }
            else
            {
                // No body renderer resolved: render on top of the default layer
                // instead of leaving the overlay at sortingOrder 0 behind terrain.
                renderer2.sortingOrder = 100;
            }

            renderer2.sprite = Frame("idle", 0);
            var pointer = steed.Pointer;
            if (pointer != IntPtr.Zero)
                Visuals[pointer] = new GloamHartVisual(steed, renderer2);

            log?.Invoke("Gloam Hart: custom visual attached; vanilla body renderers hidden (ruler kept).");
        }

        // Renderers that belong to the mounted ruler (monarch) and crowns, which
        // live under the steed hierarchy and must not be disabled.
        private static void CollectRulerRenderers(Steed steed, HashSet<IntPtr> keep)
        {
            try
            {
                var anchor = steed.riderAnchor;
                if (anchor != null)
                    foreach (var r in anchor.GetComponentsInChildren<SpriteRenderer>(true))
                        if (r != null && r.Pointer != IntPtr.Zero) keep.Add(r.Pointer);
            }
            catch { }

            try
            {
                var pairs = steed._riderObjectPairs;
                if (pairs != null)
                {
                    foreach (var value in pairs.Values)
                    {
                        if (value == null) continue;
                        foreach (var r in value.GetComponentsInChildren<SpriteRenderer>(true))
                            if (r != null && r.Pointer != IntPtr.Zero) keep.Add(r.Pointer);
                    }
                }
            }
            catch { }

            try
            {
                var crowns = steed._crowns;
                if (crowns != null)
                    for (int i = 0; i < crowns.Count; i++)
                        if (crowns[i] != null && crowns[i].Pointer != IntPtr.Zero) keep.Add(crowns[i].Pointer);
            }
            catch { }
        }

        private static Steed FindBasePrefab(Action<string> log)
        {
            var preferences = new[] { "Reindeer", "Stag", "P1Stag", "P2Stag", "P1Default", "P2Default" };
            foreach (var wanted in preferences)
            {
                foreach (var steed in Resources.FindObjectsOfTypeAll<Steed>())
                {
                    if (!IsPrefab(steed)) continue;
                    if (!string.Equals(steed.steedType.ToString(), wanted, StringComparison.OrdinalIgnoreCase)) continue;
                    log?.Invoke($"Gloam Hart: cloning {steed.steedType} ({steed.name}).");
                    return steed;
                }
            }

            log?.Invoke("Gloam Hart: no Reindeer/Stag/default horse prefab is loaded yet.");
            return null;
        }

        private static bool IsPrefab(Steed steed)
        {
            try
            {
                return steed != null
                    && steed.gameObject != null
                    && steed.gameObject.scene.handle == 0
                    && steed.steedType != SteedType.INVALID
                    && steed.steedType != SteedType.Trap
                    && steed.steedType != SteedType.Barrier;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureSprites(Action<string> log)
        {
            if (Frames.Count > 0) return;
            LoadSprites();
            if (Frames.Count == 0)
                log?.Invoke("Gloam Hart: no embedded sprites loaded.");
        }

        private static void LoadSprites()
        {
            if (Frames.Count > 0) return;
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.Contains(".assets.gloam_hart.", StringComparison.OrdinalIgnoreCase)
                    || !resourceName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    continue;

                var file = resourceName.Substring(resourceName.LastIndexOf(".assets.gloam_hart.", StringComparison.OrdinalIgnoreCase) + ".assets.gloam_hart.".Length);
                file = file.Substring(0, file.Length - ".png".Length);
                var lastUnderscore = file.LastIndexOf('_');
                if (lastUnderscore <= 0) continue;
                var group = file.Substring(0, lastUnderscore);
                if (!int.TryParse(file.Substring(lastUnderscore + 1), out var index)) continue;

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                if (!ImageConversion.LoadImage(texture, ms.ToArray(), markNonReadable: false))
                    continue;
                texture.name = "gloam_hart_" + file;
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.2f),
                    PixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect);
                sprite.name = "gloam_hart_" + file;
                AddFrame(group, index, sprite);
            }
        }

        private static void AddFrame(string group, int index, Sprite sprite)
        {
            if (!Frames.TryGetValue(group, out var frames))
            {
                frames = new Sprite[Math.Max(index + 1, 1)];
                Frames[group] = frames;
            }
            if (index >= frames.Length)
            {
                var expanded = new Sprite[index + 1];
                Array.Copy(frames, expanded, frames.Length);
                frames = expanded;
                Frames[group] = frames;
            }
            frames[index] = sprite;
        }

        private static Sprite Frame(string group, int index)
        {
            if (!Frames.TryGetValue(group, out var frames) || frames.Length == 0) return null;
            return frames[Mathf.Abs(index) % frames.Length] ?? frames[0];
        }

        private static int CountFrames()
        {
            int count = 0;
            foreach (var frames in Frames.Values)
                for (int i = 0; i < frames.Length; i++)
                    if (frames[i] != null) count++;
            return count;
        }

        private sealed class GloamHartVisual
        {
            private readonly Steed _steed;
            private readonly SpriteRenderer _renderer;
            private Vector3 _lastPosition;
            private string _state = "idle";
            private int _frame;
            private float _frameTimer;
            private float _idleSeconds;
            private float _runSeconds;
            private float _eatUntil;
            private float _nextEatTime;
            private float _tiredUntil;
            private bool _facingLeft;

            public GloamHartVisual(Steed steed, SpriteRenderer renderer)
            {
                _steed = steed;
                _renderer = renderer;
                _lastPosition = steed.transform.position;
                _nextEatTime = Time.time + 8f;
            }

            public bool Update()
            {
                if (_steed == null || _renderer == null || _steed.gameObject == null)
                    return false;
                if (!_steed.gameObject.activeInHierarchy)
                    return true;

                var dt = Mathf.Max(Time.deltaTime, 0.016f);
                var pos = _steed.transform.position;
                var dx = pos.x - _lastPosition.x;
                var speed = Mathf.Abs(dx) / dt;
                _lastPosition = pos;

                if (dx > 0.01f) _facingLeft = false;
                else if (dx < -0.01f) _facingLeft = true;
                _renderer.flipX = _facingLeft;

                var next = ChooseState(speed, dt);
                if (next != _state)
                {
                    _state = next;
                    _frame = 0;
                    _frameTimer = 0f;
                    _renderer.sprite = Frame(_state, _frame);
                }

                _frameTimer += dt;
                var frameDuration = FrameDuration(_state);
                if (_frameTimer >= frameDuration)
                {
                    _frameTimer = 0f;
                    _frame++;
                    _renderer.sprite = Frame(_state, _frame);
                }
                return true;
            }

            private string ChooseState(float speed, float dt)
            {
                var now = Time.time;
                if (speed > 3.1f)
                {
                    _runSeconds += dt;
                    _idleSeconds = 0f;
                    if (_runSeconds > 9f)
                        _tiredUntil = now + 1.4f;
                    return "run";
                }

                if (speed > 0.15f)
                {
                    _runSeconds = Mathf.Max(0f, _runSeconds - dt * 2f);
                    _idleSeconds = 0f;
                    return "walk";
                }

                _idleSeconds += dt;
                _runSeconds = Mathf.Max(0f, _runSeconds - dt);
                if (now < _tiredUntil)
                    return "tired";
                if (now < _eatUntil)
                    return "eat";
                if (_idleSeconds > 4f && now >= _nextEatTime)
                {
                    _eatUntil = now + 1.2f;
                    _nextEatTime = now + 16f;
                    return "eat";
                }
                if (_idleSeconds > 1.2f && _idleSeconds < 1.7f)
                    return "rear";
                return "idle";
            }

            private static float FrameDuration(string state)
            {
                return state switch
                {
                    "run" => 0.08f,
                    "walk" => 0.12f,
                    "eat" => 0.18f,
                    "rear" => 0.13f,
                    "tired" => 0.22f,
                    _ => 0.16f,
                };
            }
        }
    }
}

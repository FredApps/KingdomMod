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
        // 96x64 source frames. At 32 px/unit the mount is ~3.0 x 2.0 world units,
        // in line with a vanilla steed (16 made it a ~6 x 4 giant).
        private const float PixelsPerUnit = 32f;
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
            // Keep the mounted ruler (monarch) and crown renderers enabled — they
            // live under the steed (riderAnchor, _riderObjectPairs, _crowns) and
            // the monarch turns invisible if they are disabled.
            var keep = new HashSet<IntPtr>();
            CollectRulerRenderers(steed, keep);

            // Drive the steed's OWN body renderer instead of bolting on a foreign
            // SpriteRenderer. The body draws through the game's SpriteRendererFX
            // custom shader (recolor / ambient / outline); a separate renderer
            // using that material renders transparent, and one using the default
            // material renders in the wrong sorting/lighting context — which is
            // why an overlay stayed invisible. Reusing the real renderer inherits
            // the correct material, sorting layer, and day/night ambient response,
            // so the mount is guaranteed to render like any vanilla steed.
            var body = ResolveBodyRenderer(steed, keep);
            if (body == null)
            {
                log?.Invoke("Gloam Hart: no body renderer found; visual not attached.");
                MelonLogger.Warning("[GloamHart] No body SpriteRenderer resolved on the cloned steed.");
                return;
            }
            var bodyPtr = body.Pointer;

            // Stop the animators that would otherwise overwrite our sprite each
            // frame with vanilla stag/reindeer art.
            DisableBodyAnimators(steed);

            // Hide the steed's other body sub-renderers (extra parts / FX layers)
            // so only our driven renderer shows; never touch ruler/crown renderers.
            foreach (var renderer in steed.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer == null) continue;
                var ptr = renderer.Pointer;
                if (ptr != IntPtr.Zero && (ptr == bodyPtr || keep.Contains(ptr))) continue;
                renderer.enabled = false;
            }

            body.enabled = true;
            body.sprite = Frame("idle", 0);
            // Force the renderer fully opaque: the vanilla steed drives visibility
            // through the FX component's alpha/fade, which we bypass, so the raw
            // SpriteRenderer color alpha can sit at 0 and hide our sprite.
            ForceOpaque(steed, body);

            var pointer = steed.Pointer;
            if (pointer != IntPtr.Zero)
                Visuals[pointer] = new GloamHartVisual(steed, body);

            MelonLogger.Msg($"[GloamHart] Driving body renderer '{SafeName(body)}'; animators disabled.");
            log?.Invoke("Gloam Hart: custom visual attached (driving body renderer; ruler kept).");
        }

        // The steed normally fades in via SpriteRendererFX.alpha and may leave the
        // raw SpriteRenderer.color alpha at 0; force both opaque so our sprite shows.
        private static void ForceOpaque(Steed steed, SpriteRenderer body)
        {
            try { var c = body.color; c.a = 1f; body.color = c; } catch { }
            try { if (steed.SpriteFX != null) steed.SpriteFX.alpha = 1f; } catch { }
        }

        // Disable the animators that drive the steed body sprite so our per-frame
        // sprite assignment is not overwritten. Movement/stamina are code-driven
        // and unaffected; the ruler animation is a separate system.
        private static void DisableBodyAnimators(Steed steed)
        {
            try { var a = steed.Animator; if (a != null) a.enabled = false; } catch { }
            try { var a = steed.hierarchyAnimator; if (a != null) a.enabled = false; } catch { }
        }

        private static string SafeName(UnityEngine.Object o)
        {
            try { return o != null ? o.name : "(null)"; } catch { return "(err)"; }
        }

        // Resolve the steed's own body SpriteRenderer, independent of whether its
        // sprite has been assigned yet. Prefer the typed SpriteFX renderer; fall
        // back to the first non-ruler child renderer.
        private static SpriteRenderer ResolveBodyRenderer(Steed steed, HashSet<IntPtr> keep)
        {
            try
            {
                var fx = steed.SpriteFX;
                if (fx != null)
                {
                    var r = fx.GetComponent<SpriteRenderer>();
                    if (r != null) return r;
                }
            }
            catch { }

            try
            {
                foreach (var renderer in steed.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer == null) continue;
                    if (renderer.Pointer != IntPtr.Zero && keep.Contains(renderer.Pointer)) continue;
                    return renderer; // first steed-body renderer, sprite or not
                }
            }
            catch { }

            return null;
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
                // Runtime-created textures/sprites are destroyed by Unity on the
                // scene load into a run (this mod loads them in the boot scene),
                // which left Frame() returning null by the time a mount was built.
                // HideAndDontSave keeps them alive for the whole session.
                texture.hideFlags = HideFlags.HideAndDontSave;
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.2f),
                    PixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect);
                sprite.name = "gloam_hart_" + file;
                sprite.hideFlags = HideFlags.HideAndDontSave;
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

                // The ride / player-model setup path can re-enable the body
                // animator after we attached; re-assert our control each frame so
                // vanilla frames never bleed back in and our renderer stays on.
                DisableBodyAnimators(_steed);
                if (!_renderer.enabled) _renderer.enabled = true;
                ForceOpaque(_steed, _renderer);

                var dt = Mathf.Max(Time.deltaTime, 0.016f);
                var pos = _steed.transform.position;
                var dx = pos.x - _lastPosition.x;
                var speed = Mathf.Abs(dx) / dt;
                _lastPosition = pos;

                // Face the same way as the mounted monarch. Mirror the rider's
                // actual on-screen facing (which the game may express via the
                // sprite's flipX or a negative transform scale), so the mount
                // turns with the monarch even while standing still. Fall back to
                // our own horizontal movement when no rider sprite is found.
                if (TryGetRiderFacingLeft(out var riderLeft))
                    _facingLeft = riderLeft;
                else if (dx > 0.01f) _facingLeft = false;
                else if (dx < -0.01f) _facingLeft = true;
                // Combine with the mount renderer's own mirroring so the resulting
                // on-screen facing matches, regardless of parent scale sign.
                bool mountMirrored = _renderer.transform.lossyScale.x < 0f;
                _renderer.flipX = _facingLeft ^ mountMirrored;

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

            // The monarch's on-screen facing, read from its largest active rider
            // sprite under the steed's rider anchor. Combines flipX with transform
            // mirroring so it is correct however the game expresses the flip.
            private bool TryGetRiderFacingLeft(out bool facingLeft)
            {
                facingLeft = false;
                try
                {
                    var anchor = _steed.riderAnchor;
                    if (anchor == null) return false;
                    SpriteRenderer best = null;
                    float bestSize = 0f;
                    // includeInactive: false -> only the active monarch's sprites.
                    foreach (var r in anchor.GetComponentsInChildren<SpriteRenderer>(false))
                    {
                        if (r == null || r.sprite == null) continue;
                        float sx = r.bounds.size.x; // body sprite is the largest
                        if (sx > bestSize) { bestSize = sx; best = r; }
                    }
                    if (best == null) return false;
                    facingLeft = best.flipX ^ (best.transform.lossyScale.x < 0f);
                    return true;
                }
                catch { return false; }
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

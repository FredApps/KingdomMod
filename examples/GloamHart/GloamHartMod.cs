using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
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
        // Rear "kick-off" shown at the start of the rush before the gallop cycle.
        private const float RearKickoffSeconds = 0.4f;
        // Actual movement boost. The game recomputes locomotion from a live Mover
        // multiplier each frame, so writing steed.runSpeed alone does nothing -
        // Mover.SetSpeedMultiplier is the lever the game's own speed boost uses.
        private const float RushSpeedMultiplier = 1.9f;
        private static readonly Dictionary<string, Sprite[]> Frames = new();
        private static readonly Dictionary<IntPtr, GloamHartVisual> Visuals = new();

        // Rush duration/cooldown modeled on an in-game mount ability. Resolved at
        // runtime from the cat chariot (SteedType.CatCart) SteedAbility, since the
        // serialized values are only on the prefab, not in any static dump.
        private static float _abilityDuration = -1f;
        private static float _abilityCooldown = -1f;
        private static string _abilitySource;

        // Resolve rush timings from the cat chariot when its prefab is loaded
        // (Norse Lands), retrying until found; falls back to sane values otherwise.
        private static void EnsureAbilityValues()
        {
            if (_abilitySource == "catcart") return; // best source already locked in

            float dur = 0f, cd = 0f;
            string src = null;

            try
            {
                foreach (var steed in Resources.FindObjectsOfTypeAll<Steed>())
                {
                    if (steed == null || steed.steedType != SteedType.CatCart) continue;
                    if (TryReadAbility(steed, out dur, out cd)) { src = "catcart"; break; }
                }
            }
            catch { }

            if (src == null)
            {
                try
                {
                    foreach (var ab in Resources.FindObjectsOfTypeAll<SteedAbility>())
                    {
                        if (ab == null) continue;
                        float d; try { d = ab._duration; } catch { continue; }
                        if (d > 0f) { dur = d; try { cd = ab._cooldown; } catch { cd = 0f; } src = "scanned"; break; }
                    }
                }
                catch { }
            }

            if (src == null || dur <= 0f) { dur = 2.5f; cd = 8f; src = "fallback"; }
            if (cd <= 0f) cd = Mathf.Max(dur * 2.5f, 6f);

            // Only overwrite once (or when we finally lock onto the cat chariot).
            if (_abilitySource == null || src == "catcart")
            {
                _abilityDuration = dur;
                _abilityCooldown = cd;
                _abilitySource = src;
                GloamLog.Event("ability_values", ("source", src), ("duration", dur), ("cooldown", cd));
                MelonLogger.Msg($"[GloamHart] Rush values: {src} duration={dur:0.##}s cooldown={cd:0.##}s.");
            }
        }

        private static bool TryReadAbility(Steed steed, out float duration, out float cooldown)
        {
            duration = 0f;
            cooldown = 0f;
            try
            {
                var abilities = steed.steedAbilities;
                if (abilities == null) return false;
                foreach (var ab in abilities)
                {
                    if (ab == null) continue;
                    float d = ab._duration;
                    if (d > 0f) { duration = d; cooldown = ab._cooldown; return true; }
                }
            }
            catch { }
            return false;
        }

        public override void OnInitializeMelon()
        {
            GloamLog.Initialize();
            LoadSprites();
            Kingdom.CustomMounts.Register(
                "gloam_hart",
                "Gloam Hart",
                "A luminous forest hart: stag handling, gentle stamina, deer attraction, original animated sprites, and Shift-activated Gloam Rush.",
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
                {
                    pair.Value.Cleanup();
                    stale.Add(pair.Key);
                }
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

        private static Sprite _haloSprite;

        // Soft radial cyan glow used by the Gloam Rush halo, built once.
        private static Sprite HaloSprite()
        {
            if (_haloSprite != null) return _haloSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a *= a; // soft falloff toward the edge
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(updateMipmaps: false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            _haloSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 20f, 0, SpriteMeshType.FullRect);
            _haloSprite.name = "gloam_rush_halo";
            _haloSprite.hideFlags = HideFlags.HideAndDontSave;
            return _haloSprite;
        }

        // Gloam-specific structured diagnostics. The loader's RuntimeInteractionLogger
        // is internal, so this example writes its own single-session JSONL for
        // debugging the Gloam Rush ability.
        private static class GloamLog
        {
            private static readonly object Sync = new object();
            private static StreamWriter _writer;

            public static void Initialize()
            {
                lock (Sync)
                {
                    try
                    {
                        var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "KingdomMod", "logs");
                        Directory.CreateDirectory(dir);
                        var path = Path.Combine(dir, "gloam-hart-latest.jsonl");
                        _writer?.Dispose();
                        var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                        _writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
                    }
                    catch (Exception e)
                    {
                        _writer = null;
                        try { MelonLogger.Warning($"[GloamHart] log init failed: {e.Message}"); } catch { }
                    }
                }
            }

            public static void Event(string action, params (string Key, object Value)[] fields)
            {
                lock (Sync)
                {
                    if (_writer == null) return;
                    try
                    {
                        var sb = new StringBuilder(256);
                        sb.Append('{');
                        Append(sb, "t", DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), first: true);
                        Append(sb, "action", action, first: false);
                        if (fields != null)
                            foreach (var f in fields)
                                Append(sb, f.Key, f.Value, first: false);
                        sb.Append('}');
                        _writer.WriteLine(sb.ToString());
                    }
                    catch { }
                }
            }

            private static void Append(StringBuilder sb, string key, object value, bool first)
            {
                if (!first) sb.Append(',');
                sb.Append('"').Append(key).Append("\":");
                switch (value)
                {
                    case null: sb.Append("null"); break;
                    case bool b: sb.Append(b ? "true" : "false"); break;
                    case float f: sb.Append(f.ToString("0.###", CultureInfo.InvariantCulture)); break;
                    case double d: sb.Append(d.ToString("0.###", CultureInfo.InvariantCulture)); break;
                    case int or long: sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
                    default: AppendString(sb, value.ToString()); break;
                }
            }

            private static void AppendString(StringBuilder sb, string value)
            {
                sb.Append('"');
                foreach (var ch in value)
                {
                    switch (ch)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '"': sb.Append("\\\""); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default: sb.Append(ch); break;
                    }
                }
                sb.Append('"');
            }
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
            private bool _rushActive;
            private bool _rushStatsApplied;
            private float _rushEndsAt;
            private float _nextRushAt;
            private float _rearUntil;
            private float _baseRunSpeed;
            private float _baseForestSpeedMultiplier;
            private float _baseRunStaminaRate;
            private float _baseGlideStaminaRate;
            private float _baseStandStaminaRate;
            private float _nextTickLog;
            private GameObject _halo;
            private SpriteRenderer _haloRenderer;

            public GloamHartVisual(Steed steed, SpriteRenderer renderer)
            {
                _steed = steed;
                _renderer = renderer;
                _lastPosition = steed.transform.position;
                _nextEatTime = Time.time + 8f;
                CaptureBaseStats();
            }

            public bool Update()
            {
                if (_steed == null || _renderer == null || _steed.gameObject == null)
                    return false;
                if (!_steed.gameObject.activeInHierarchy)
                {
                    RestoreRushStats();
                    return true;
                }

                // The ride / player-model setup path can re-enable the body
                // animator after we attached; re-assert our control each frame so
                // vanilla frames never bleed back in and our renderer stays on.
                DisableBodyAnimators(_steed);
                if (!_renderer.enabled) _renderer.enabled = true;
                ForceOpaque(_steed, _renderer);

                var dt = Mathf.Max(Time.deltaTime, 0.016f);
                UpdateRush(dt);
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

                // While the ability is active, force a rear "kick-off" then a
                // gallop, regardless of actual movement, so the rush is animated.
                string next;
                if (_rushActive)
                    next = (Time.time < _rearUntil) ? "rear" : "run";
                else
                    next = ChooseState(speed, dt);
                if (next != _state)
                {
                    _state = next;
                    _frame = 0;
                    _frameTimer = 0f;
                    _renderer.sprite = Frame(_state, _frame);
                }

                _frameTimer += dt;
                var frameDuration = FrameDuration(_state);
                if (_rushActive && _state == "run") frameDuration = 0.05f; // fast gallop
                if (_frameTimer >= frameDuration)
                {
                    _frameTimer = 0f;
                    _frame++;
                    _renderer.sprite = Frame(_state, _frame);
                }
                ApplyRushVisual();
                return true;
            }

            public void Cleanup()
            {
                RestoreRushStats();
                try { if (_halo != null) Object.Destroy(_halo); } catch { }
                _halo = null;
                _haloRenderer = null;
            }

            private void CaptureBaseStats()
            {
                try
                {
                    _baseRunSpeed = _steed.runSpeed;
                    _baseForestSpeedMultiplier = _steed.forestSpeedMultiplier;
                    _baseRunStaminaRate = _steed.runStaminaRate;
                    _baseGlideStaminaRate = _steed.glideStaminaRate;
                    _baseStandStaminaRate = _steed.standStaminaRate;
                }
                catch { }
            }

            private void UpdateRush(float dt)
            {
                var now = Time.time;
                var rider = FindCurrentRider();
                if (rider == null)
                {
                    if (_rushActive || _rushStatsApplied) GloamLog.Event("no_rider");
                    RestoreRushStats();
                    return;
                }

                if (_rushActive && now >= _rushEndsAt)
                {
                    GloamLog.Event("expired", StateFields(rider));
                    RestoreRushStats();
                    GloamLog.Event("restored", StateFields(rider));
                }

                if (CheckAbilityButtonDown(rider, out var which))
                {
                    float remaining = Mathf.Max(0f, _nextRushAt - now);
                    GloamLog.Event("button_press", ("input", which), ("rushActive", _rushActive),
                        ("cooldownRemaining", remaining));
                    if (_rushActive)
                        GloamLog.Event("blocked_active");
                    else if (now < _nextRushAt)
                        GloamLog.Event("blocked_cooldown", ("remaining", remaining));
                    else
                        ActivateRush(now, rider);
                }

                if (_rushActive)
                {
                    try { _steed.Stamina = Mathf.Min(1f, _steed.Stamina + dt * 0.5f); } catch { }
                    if (now >= _nextTickLog)
                    {
                        _nextTickLog = now + 0.25f; // ~4/s
                        GloamLog.Event("tick", StateFields(rider));
                    }
                }
            }

            // Run direction for SetActionState: the rider's last active direction
            // when known, else the sign of its speed, else the mount's facing.
            private int RunDirection(Player rider)
            {
                try { int d = rider._previousActiveDirection; if (d != 0) return d > 0 ? 1 : -1; } catch { }
                try { float s = rider.currentSpeed; if (Mathf.Abs(s) > 0.01f) return s > 0 ? 1 : -1; } catch { }
                return _facingLeft ? -1 : 1;
            }

            // Common state snapshot for the Gloam diagnostics log.
            private (string, object)[] StateFields(Player rider)
            {
                return new (string, object)[]
                {
                    ("playerId", SafeI(() => rider != null ? rider.playerId : -1)),
                    ("actionState", SafeS(() => rider != null ? rider.actionState.ToString() : "n/a")),
                    ("currentSpeed", SafeF(() => rider != null ? rider.currentSpeed : 0f)),
                    ("steedMode", SafeS(() => _steed.CurrentMode.ToString())),
                    ("stamina", SafeF(() => _steed.Stamina)),
                    ("runSpeed", SafeF(() => _steed.runSpeed)),
                    ("forestMult", SafeF(() => _steed.forestSpeedMultiplier)),
                    ("runStamRate", SafeF(() => _steed.runStaminaRate)),
                    ("riderMatch", SafeB(() => IsRidingThis(rider))),
                    ("rushActive", _rushActive),
                    ("color", SafeS(() => _renderer != null ? ColorText(_renderer.color) : "n/a")),
                    ("sprite", SafeS(() => _renderer != null && _renderer.sprite != null ? _renderer.sprite.name : "n/a")),
                };
            }

            private static int SafeI(Func<int> f) { try { return f(); } catch { return -999; } }
            private static float SafeF(Func<float> f) { try { return f(); } catch { return float.NaN; } }
            private static bool SafeB(Func<bool> f) { try { return f(); } catch { return false; } }
            private static string SafeS(Func<string> f) { try { return f(); } catch { return "err"; } }
            private static string ColorText(Color c) => $"{c.r:0.0},{c.g:0.0},{c.b:0.0},{c.a:0.0}";

            private Player FindCurrentRider()
            {
                try
                {
                    var rider = _steed.Rider;
                    if (IsRidingThis(rider)) return rider;
                }
                catch { }

                try
                {
                    foreach (var player in Kingdom.Players.All)
                        if (IsRidingThis(player)) return player;
                }
                catch { }

                return null;
            }

            private bool IsRidingThis(Player player)
            {
                try
                {
                    return player != null
                        && player.steed != null
                        && player.steed.Pointer == _steed.Pointer;
                }
                catch
                {
                    return false;
                }
            }

            private static bool CheckAbilityButtonDown(Player rider, out string which)
            {
                which = null;
                try
                {
                    var rewiredPlayer = Il2CppRewired.ReInput.players.GetPlayer(rider.playerId);
                    if (rewiredPlayer != null)
                    {
                        if (rewiredPlayer.GetButtonDown(RewiredAxis.Gallop)) { which = "Gallop"; return true; }
                        if (rewiredPlayer.GetButtonDown(RewiredAxis.ActivateRulerAbility)) { which = "ActivateRulerAbility"; return true; }
                    }
                }
                catch { }

                try
                {
                    if (UnityEngine.Input.GetKeyDown(KeyCode.LeftShift)) { which = "LeftShift"; return true; }
                    if (UnityEngine.Input.GetKeyDown(KeyCode.RightShift)) { which = "RightShift"; return true; }
                }
                catch { }

                return false;
            }

            private void ActivateRush(float now, Player rider)
            {
                EnsureAbilityValues();
                CaptureBaseStats();
                _rushActive = true;
                _rushEndsAt = now + _abilityDuration;
                _nextRushAt = now + _abilityDuration + _abilityCooldown;
                _rearUntil = now + RearKickoffSeconds;
                ApplyRushStats();

                // Make the rush immediate and visible: full stamina, force the
                // game's own gallop, and push the rider into Run so the higher
                // runSpeed takes effect at once instead of on the next input.
                try { _steed.Stamina = 1f; } catch (Exception e) { GloamLog.Event("stamina_error", ("msg", e.Message)); }
                try { _steed.ForceGallopAnimation(); } catch (Exception e) { GloamLog.Event("force_gallop_error", ("msg", e.Message)); }
                try
                {
                    if (rider != null && rider.actionState != Player.ActionState.Run)
                        rider.SetActionState(Player.ActionState.Run, RunDirection(rider), false);
                }
                catch (Exception e) { GloamLog.Event("set_run_error", ("msg", e.Message)); }

                // The real speed change: apply a live Mover multiplier (auto-expires
                // after the ability duration). Writing steed.runSpeed alone is
                // recomputed away each frame by the game's locomotion.
                ApplySpeedMultiplier(rider);

                MelonLogger.Msg("[GloamHart] Gloam Rush activated.");
                GloamLog.Event("activated", StateFields(rider));
            }

            private void ApplyRushStats()
            {
                if (_rushStatsApplied) return;
                try
                {
                    _steed.runSpeed = Mathf.Max(_baseRunSpeed + 1.8f, _baseRunSpeed * 1.75f);
                    _steed.forestSpeedMultiplier = Mathf.Max(_baseForestSpeedMultiplier, 1.5f);
                    _steed.runStaminaRate = Mathf.Max(_baseRunStaminaRate, 0.10f); // positive => regen
                    _rushStatsApplied = true;
                }
                catch { }
            }

            private void RestoreRushStats()
            {
                if (!_rushActive && !_rushStatsApplied)
                {
                    ApplyNormalTint();
                    return;
                }

                try
                {
                    _steed.runSpeed = _baseRunSpeed;
                    _steed.forestSpeedMultiplier = _baseForestSpeedMultiplier;
                    _steed.runStaminaRate = _baseRunStaminaRate;
                    _steed.glideStaminaRate = _baseGlideStaminaRate;
                    _steed.standStaminaRate = _baseStandStaminaRate;
                }
                catch { }

                ResetSpeedMultiplier();

                _rushActive = false;
                _rushStatsApplied = false;
                ApplyNormalTint();
            }

            // Apply the live movement boost via the rider's (and steed's) Mover,
            // timed out to the ability duration so it self-clears.
            private void ApplySpeedMultiplier(Player rider)
            {
                float applied = 0f;
                try
                {
                    var mover = rider != null ? rider.mover : null;
                    if (mover != null) { mover.SetSpeedMultiplier(RushSpeedMultiplier, _abilityDuration); applied = RushSpeedMultiplier; }
                }
                catch (Exception e) { GloamLog.Event("mover_error", ("msg", e.Message)); }
                try
                {
                    var sm = _steed._mover;
                    if (sm != null) sm.SetSpeedMultiplier(RushSpeedMultiplier, _abilityDuration);
                }
                catch { }
                GloamLog.Event("speed_multiplier", ("multiplier", applied), ("timeout", _abilityDuration));
            }

            private void ResetSpeedMultiplier()
            {
                try
                {
                    var rider = FindCurrentRider();
                    var mover = rider != null ? rider.mover : null;
                    if (mover != null) mover.ResetSpeedMultiplier();
                }
                catch { }
                try { var sm = _steed._mover; if (sm != null) sm.ResetSpeedMultiplier(); }
                catch { }
            }

            private void ApplyRushVisual()
            {
                if (!_rushActive)
                {
                    ApplyNormalTint();
                    return;
                }

                try
                {
                    var pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 18f);
                    // Strong body-only cyan pulse (SpriteRenderer.color affects only
                    // this renderer, so the mounted monarch is not tinted). Do not
                    // scale this transform - it is the steed root and would scale the
                    // rider too; the halo child carries the size pulse instead.
                    _renderer.color = new Color(0.35f + 0.15f * pulse, 1f, 1f, 1f);
                    if (_steed.SpriteFX != null)
                        _steed.SpriteFX.alpha = 1f;

                    EnsureHalo();
                    if (_halo != null)
                    {
                        if (!_halo.activeSelf) _halo.SetActive(true);
                        if (_haloRenderer != null)
                            _haloRenderer.color = new Color(0.3f, 1f, 1f, 0.30f + 0.30f * pulse);
                        _halo.transform.localScale = Vector3.one * (1.15f + 0.20f * pulse);
                    }
                }
                catch { }
            }

            private void ApplyNormalTint()
            {
                try
                {
                    if (_renderer != null)
                        _renderer.color = Color.white;
                    if (_halo != null && _halo.activeSelf)
                        _halo.SetActive(false);
                }
                catch { }
            }

            // Lazily build a cyan glow child that rings the mount body. It copies
            // the body renderer's sorting layer and draws just behind it, and its
            // sprite is HideAndDontSave so it survives scene loads.
            private void EnsureHalo()
            {
                if (_halo != null) return;
                try
                {
                    _halo = new GameObject("GloamRushHalo");
                    _halo.transform.SetParent(_renderer.transform, false);
                    _halo.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                    _haloRenderer = _halo.AddComponent<SpriteRenderer>();
                    _haloRenderer.sprite = HaloSprite();
                    _haloRenderer.sortingLayerID = _renderer.sortingLayerID;
                    _haloRenderer.sortingOrder = _renderer.sortingOrder - 1; // glow behind body
                    _haloRenderer.color = new Color(0.3f, 1f, 1f, 0.4f);
                }
                catch { _halo = null; _haloRenderer = null; }
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

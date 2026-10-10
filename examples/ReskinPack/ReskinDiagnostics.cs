using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader.Utils;
using UnityEngine;

namespace KingdomMod.Examples.ReskinPack
{
    internal sealed class ReskinDiagnostics : IDisposable
    {
        private const int MaxRecords = 8192;
        private readonly HashSet<string> _observed = new(StringComparer.Ordinal);
        private StreamWriter _writer;
        private Action<string> _warning;
        private int _records;
        private float _nextFlush;
        public string Path { get; private set; }

        public void Initialize(Action<string> warning)
        {
            _warning = warning;
            Path = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "KingdomMod", "logs", "reskin-latest.jsonl");
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                _writer = new StreamWriter(Path, append: false);
                Write(new { kind = "startup", version = "0.1.2", assembly = typeof(ReskinMod).Assembly.Location,
                    mods = MelonEnvironment.ModsDirectory });
                _writer.Flush();
            }
            catch (Exception ex) { Disable(ex); }
        }

        public void Observe(SpriteRenderer renderer, Sprite sprite, string scene, string key, bool matched)
        {
            if (_writer == null || _records >= MaxRecords) return;
            try
            {
                var identity = scene + "/" + renderer.GetInstanceID() + "/" + sprite.name;
                if (!_observed.Add(identity)) return;
                var rect = sprite.rect;
                var color = renderer.color;
                var material = renderer.sharedMaterial;
                Write(new { kind = "observed", scene, renderer = Hierarchy(renderer.transform),
                    sprite = sprite.name, suggestedFile = key + ".png", matched,
                    texture = sprite.texture != null ? sprite.texture.name : null,
                    width = rect.width, height = rect.height, ppu = sprite.pixelsPerUnit,
                    pivotX = sprite.pivot.x, pivotY = sprite.pivot.y,
                    shader = material != null && material.shader != null ? material.shader.name : null,
                    alpha = color.a, sortingLayer = renderer.sortingLayerID, sortingOrder = renderer.sortingOrder });
            }
            catch (Exception ex)
            {
                // Diagnostics must never prevent a renderer assignment.
                Write(new { kind = "observation_error", error = ex.Message });
            }
        }

        private static string Hierarchy(Transform transform)
        {
            var parts = new List<string>();
            for (var depth = 0; transform != null && depth < 32; depth++, transform = transform.parent)
                parts.Add(transform.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        public void Write(object entry)
        {
            if (_writer == null || _records >= MaxRecords) return;
            try
            {
                _writer.WriteLine(JsonSerializer.Serialize(new { timestamp = DateTime.UtcNow, entry }));
                _records++;
                if (_records == MaxRecords)
                {
                    _writer.WriteLine(JsonSerializer.Serialize(new { entry = new { kind = "limit", maxRecords = MaxRecords } }));
                    _writer.Flush();
                }
            }
            catch (Exception ex) { Disable(ex); }
        }

        public void FlushIfDue(float now)
        {
            if (_writer == null || now < _nextFlush) return;
            try { _writer.Flush(); _nextFlush = now + 1f; }
            catch (Exception ex) { Disable(ex); }
        }

        private void Disable(Exception ex)
        {
            Dispose();
            _warning?.Invoke($"Reskin diagnostics unavailable: {ex.Message}");
        }

        public void Dispose()
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }
}

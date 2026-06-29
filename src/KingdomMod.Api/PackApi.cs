// PackApi — JSON balance overrides + sprite/audio asset replacement from a "pack"
// folder on disk.  This is the no-code modding path: a user drops files into
//   Mods/<modname>/pack/kingdommod.pack.json
//   Mods/<modname>/pack/balance.json
//   Mods/<modname>/pack/sprites/<spriteName>.png
//   Mods/<modname>/pack/audio/<clipName>.wav
// and the SDK swaps them in at load time.
//
// CRITICAL: KingdomMod ships NO game art or audio.  This loader takes user-supplied
// files only.  Anyone wanting to ship a reskin must distribute their own assets,
// never extracted game ones.

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace KingdomMod
{
    /// <summary>Loads no-code data/asset packs that mods can use to retexture or rebalance.</summary>
    public sealed class PackApi
    {
        internal static PackApi Instance { get; } = new PackApi();
        private PackApi() { }

        private readonly Dictionary<string, Texture2D> _textureCache = new();
        private readonly Dictionary<string, AudioClip> _audioCache   = new();
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        /// <summary>Standard manifest filename for a KingdomMod data pack.</summary>
        public const string ManifestFileName = "kingdommod.pack.json";

        /// <summary>Standard balance filename for no-code numeric/config overrides.</summary>
        public const string BalanceFileName = "balance.json";

        /// <summary>
        /// Discover pack directories below a root such as MelonLoader's Mods folder.
        /// Recognises both <c>Mods/MyMod/pack</c> and direct pack folders.
        /// </summary>
        public IReadOnlyList<PackInfo> DiscoverPacks(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory) || !Directory.Exists(rootDirectory))
                return Array.Empty<PackInfo>();

            var found = new List<PackInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddPackIfPresent(rootDirectory, found, seen);

            var directPack = Path.Combine(rootDirectory, "pack");
            AddPackIfPresent(directPack, found, seen);

            foreach (var child in Directory.EnumerateDirectories(rootDirectory))
            {
                AddPackIfPresent(child, found, seen);
                AddPackIfPresent(Path.Combine(child, "pack"), found, seen);
            }

            return new ReadOnlyCollection<PackInfo>(found);
        }

        /// <summary>Load and deserialize a JSON file. Returns default if the file is missing or invalid.</summary>
        public T LoadJson<T>(string absolutePath)
        {
            return TryLoadJson<T>(absolutePath, out var value) ? value : default;
        }

        /// <summary>Try to deserialize a JSON file using pack-friendly options: comments, trailing commas, case-insensitive names.</summary>
        public bool TryLoadJson<T>(string absolutePath, out T value)
        {
            value = default;
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                return false;

            try
            {
                value = JsonSerializer.Deserialize<T>(File.ReadAllText(absolutePath), _jsonOptions);
                return value != null;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Load a PNG/JPG from disk and turn it into a Texture2D suitable for use as
        /// a Sprite source.  Cached by path.  Returns null if the file is missing.
        /// </summary>
        public Texture2D LoadTexture(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                return null;
            if (_textureCache.TryGetValue(absolutePath, out var cached) && cached != null)
                return cached;

            var bytes = File.ReadAllBytes(absolutePath);
            // 2px placeholder; LoadImage resizes to fit the file.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,   // KTC is pixel-art; nearest sampling matters.
                wrapMode   = TextureWrapMode.Clamp,
            };
            if (!ImageConversion.LoadImage(tex, bytes, markNonReadable: false))
                return null;
            _textureCache[absolutePath] = tex;
            return tex;
        }

        /// <summary>Create a Sprite from a Texture2D at the given pixels-per-unit.</summary>
        public Sprite MakeSprite(Texture2D tex, float pixelsPerUnit = 16f, Vector2? pivot = null)
        {
            if (tex == null) return null;
            return Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                pivot ?? new Vector2(0.5f, 0.5f),
                pixelsPerUnit,
                0,
                SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Load a .wav from disk as an AudioClip.  Only PCM 16-bit WAV is decoded
        /// (deliberately minimal — keeps the SDK dependency-free).  MP3/OGG can be
        /// added later via UnityWebRequest or NAudio if a mod needs them.
        /// </summary>
        public AudioClip LoadWav(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                return null;
            if (_audioCache.TryGetValue(absolutePath, out var cached) && cached != null)
                return cached;

            try
            {
                var bytes = File.ReadAllBytes(absolutePath);
                var clip = WavDecoder.Decode(bytes, Path.GetFileNameWithoutExtension(absolutePath));
                if (clip != null) _audioCache[absolutePath] = clip;
                return clip;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Drop cached textures/audio. Use after a pack reload.</summary>
        public void ClearCache()
        {
            _textureCache.Clear();
            _audioCache.Clear();
        }

        private void AddPackIfPresent(string packDirectory, List<PackInfo> found, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(packDirectory) || !Directory.Exists(packDirectory))
                return;

            var fullPath = Path.GetFullPath(packDirectory);
            if (!seen.Add(fullPath) || !LooksLikePack(fullPath))
                return;

            var manifestPath = Path.Combine(fullPath, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                var legacyManifest = Path.Combine(fullPath, "manifest.json");
                manifestPath = File.Exists(legacyManifest) ? legacyManifest : null;
            }

            PackManifest manifest = null;
            if (manifestPath != null)
                TryLoadJson(manifestPath, out manifest);

            var directoryName = new DirectoryInfo(fullPath).Name;
            var parentName = new DirectoryInfo(fullPath).Parent?.Name;
            var fallbackId = directoryName.Equals("pack", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(parentName)
                ? parentName
                : directoryName;

            found.Add(new PackInfo(
                id: string.IsNullOrWhiteSpace(manifest?.Id) ? fallbackId : manifest.Id,
                name: string.IsNullOrWhiteSpace(manifest?.Name) ? fallbackId : manifest.Name,
                version: string.IsNullOrWhiteSpace(manifest?.Version) ? "0.0.0" : manifest.Version,
                author: manifest?.Author ?? string.Empty,
                description: manifest?.Description ?? string.Empty,
                directory: fullPath,
                manifestPath: manifestPath,
                balancePath: Path.Combine(fullPath, BalanceFileName),
                spritesDirectory: Path.Combine(fullPath, "sprites"),
                audioDirectory: Path.Combine(fullPath, "audio")));
        }

        private static bool LooksLikePack(string directory)
        {
            return File.Exists(Path.Combine(directory, ManifestFileName))
                   || File.Exists(Path.Combine(directory, "manifest.json"))
                   || File.Exists(Path.Combine(directory, BalanceFileName))
                   || Directory.Exists(Path.Combine(directory, "sprites"))
                   || Directory.Exists(Path.Combine(directory, "audio"));
        }
    }

    /// <summary>Metadata read from <c>kingdommod.pack.json</c>.</summary>
    public sealed class PackManifest
    {
        /// <summary>Stable identifier (e.g. <c>"mycoolpack"</c>) — should be lowercase, no spaces.</summary>
        public string Id { get; set; }
        /// <summary>Human-readable display name.</summary>
        public string Name { get; set; }
        /// <summary>Pack version string (free-form, recommend semver).</summary>
        public string Version { get; set; }
        /// <summary>Author or collaborator credit.</summary>
        public string Author { get; set; }
        /// <summary>One-line description.</summary>
        public string Description { get; set; }
    }

    /// <summary>Resolved, absolute paths for a discovered pack.</summary>
    public sealed class PackInfo
    {
        internal PackInfo(
            string id,
            string name,
            string version,
            string author,
            string description,
            string directory,
            string manifestPath,
            string balancePath,
            string spritesDirectory,
            string audioDirectory)
        {
            Id = id;
            Name = name;
            Version = version;
            Author = author;
            Description = description;
            Directory = directory;
            ManifestPath = manifestPath;
            BalancePath = balancePath;
            SpritesDirectory = spritesDirectory;
            AudioDirectory = audioDirectory;
        }

        /// <summary>Stable identifier from the manifest (falls back to folder name).</summary>
        public string Id { get; }
        /// <summary>Display name from the manifest.</summary>
        public string Name { get; }
        /// <summary>Version string from the manifest.</summary>
        public string Version { get; }
        /// <summary>Author credit from the manifest.</summary>
        public string Author { get; }
        /// <summary>Description from the manifest.</summary>
        public string Description { get; }
        /// <summary>Absolute path of the pack's root directory.</summary>
        public string Directory { get; }
        /// <summary>Absolute path of the manifest file, or null if the pack has none.</summary>
        public string ManifestPath { get; }
        /// <summary>Absolute path of <c>balance.json</c>, or null if absent.</summary>
        public string BalancePath { get; }
        /// <summary>Absolute path of the <c>sprites/</c> subdirectory, or null if absent.</summary>
        public string SpritesDirectory { get; }
        /// <summary>Absolute path of the <c>audio/</c> subdirectory, or null if absent.</summary>
        public string AudioDirectory { get; }
        /// <summary>True if a <c>balance.json</c> exists on disk.</summary>
        public bool HasBalance => File.Exists(BalancePath);
        /// <summary>True if a <c>sprites/</c> directory exists on disk.</summary>
        public bool HasSprites => System.IO.Directory.Exists(SpritesDirectory);
        /// <summary>True if an <c>audio/</c> directory exists on disk.</summary>
        public bool HasAudio => System.IO.Directory.Exists(AudioDirectory);
    }

    // Minimal RIFF/WAVE PCM decoder.  Not exhaustive; intended for KingdomMod packs.
    internal static class WavDecoder
    {
        public static AudioClip Decode(byte[] data, string clipName)
        {
            if (data.Length < 44) return null;
            // Header: "RIFF"…"WAVE"
            if (data[0] != 0x52 || data[1] != 0x49 || data[2] != 0x46 || data[3] != 0x46) return null;
            if (data[8] != 0x57 || data[9] != 0x41 || data[10] != 0x56 || data[11] != 0x45) return null;

            int channels = data[22] | (data[23] << 8);
            int sampleRate = data[24] | (data[25] << 8) | (data[26] << 16) | (data[27] << 24);
            int bitsPerSample = data[34] | (data[35] << 8);

            // Walk RIFF chunks to find "data".
            int pos = 12, dataStart = -1, dataLen = 0;
            while (pos + 8 <= data.Length)
            {
                int chunkId = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24);
                int chunkSize = data[pos + 4] | (data[pos + 5] << 8) | (data[pos + 6] << 16) | (data[pos + 7] << 24);
                if (chunkId == 0x61746164) // 'd''a''t''a' little-endian
                {
                    dataStart = pos + 8;
                    dataLen = chunkSize;
                    break;
                }
                pos += 8 + chunkSize + (chunkSize & 1);
            }
            if (dataStart < 0 || bitsPerSample != 16) return null;

            int sampleCount = dataLen / 2;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short s = (short)(data[dataStart + 2 * i] | (data[dataStart + 2 * i + 1] << 8));
                samples[i] = s / 32768f;
            }

            var clip = AudioClip.Create(clipName, sampleCount / channels, channels, sampleRate, stream: false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}

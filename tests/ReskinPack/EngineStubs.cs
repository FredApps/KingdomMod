// Minimal engine boundary for running the real pack and reskin code without Unity.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MelonLoader
{
    public class MelonMod
    {
        public readonly TestLogger LoggerInstance = new();
        public virtual void OnInitializeMelon() { }
        public virtual void OnSceneWasInitialized(int index, string name) { }
        public virtual void OnLateUpdate() { }
        public virtual void OnApplicationQuit() { }
    }
    public sealed class TestLogger
    {
        public readonly List<string> Messages = new();
        public void Msg(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
    }
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonInfoAttribute : Attribute
    {
        public MelonInfoAttribute(Type type, string name, string version, string author) { }
    }
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonGameAttribute : Attribute
    {
        public MelonGameAttribute(string developer, string name) { }
    }
}
namespace MelonLoader.Utils
{
    public static class MelonEnvironment { public static string ModsDirectory; }
}
namespace KingdomMod
{
    public static class Kingdom { public static PackApi Packs => PackApi.Instance; }
}
namespace UnityEngine
{
    public enum HideFlags { None, HideAndDontSave }
    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Point }
    public enum TextureWrapMode { Clamp }
    public enum SpriteMeshType { FullRect }
    public enum FindObjectsInactive { Include }
    public enum FindObjectsSortMode { None }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public struct Rect
    {
        public float width, height;
        public Rect(float x, float y, float width, float height) { this.width = width; this.height = height; }
    }
    public class Object
    {
        private static int _nextId;
        public static readonly List<Object> Live = new();
        public static int DiscoveryCalls;
        private readonly int _id = ++_nextId;
        public string name;
        public HideFlags hideFlags;
        protected Object() => Live.Add(this);
        public int GetInstanceID() => _id;
        public static void Destroy(Object value) => Live.Remove(value);
        public static T[] FindObjectsByType<T>(FindObjectsInactive inactive, FindObjectsSortMode mode) where T : Object
        {
            DiscoveryCalls++;
            return Live.OfType<T>().ToArray();
        }
    }
    public sealed class GameObject : Object { public bool activeInHierarchy = true; }
    public sealed class SpriteRenderer : Object
    {
        public Sprite sprite;
        public bool enabled = true;
        public readonly GameObject gameObject = new() { name = "Test ruler" };
    }
    public sealed class Texture2D : Object
    {
        public int width, height;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        { this.width = width; this.height = height; }
    }
    public static class ImageConversion
    {
        // Two bytes encode fixture dimensions; PNG decoding itself belongs to Unity.
        public static bool LoadImage(Texture2D texture, byte[] bytes, bool markNonReadable)
        {
            if (bytes.Length != 2) return false;
            texture.width = bytes[0]; texture.height = bytes[1]; return true;
        }
    }
    public sealed class Sprite : Object
    {
        public Texture2D texture;
        public Rect rect;
        public Vector2 pivot;
        public float pixelsPerUnit;
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float ppu, uint extrude, SpriteMeshType type)
            => new() { texture = texture, rect = rect, pivot = new Vector2(pivot.x * rect.width, pivot.y * rect.height), pixelsPerUnit = ppu };
    }
    public sealed class AudioClip : Object
    {
        public static AudioClip Create(string name, int samples, int channels, int frequency, bool stream) => new();
        public void SetData(float[] samples, int offset) { }
    }
    public static class Time { public static float unscaledTime; }
}

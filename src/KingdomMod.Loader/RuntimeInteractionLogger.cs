using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KingdomMod.Loader
{
    internal static class RuntimeInteractionLogger
    {
        private const int MaxLinesPerSession = 200000;
        private static readonly object Sync = new();
        private static StreamWriter _writer;
        private static string _path;
        private static int _lines;
        private static int _dropped;
        private static bool _capReported;
        private static bool _finalCapReported;
        private static float _nextFlush;
        private static RuntimeLogLevel _lastMode = RuntimeLogLevel.None;

        public static string Path => _path;

        public static RuntimeLogLevel Level => LoaderMod.Instance?.ExtendedRuntimeLogging ?? RuntimeLogLevel.None;

        public static void Initialize()
        {
            lock (Sync)
            {
                try
                {
                    var dir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "KingdomMod", "logs");
                    Directory.CreateDirectory(dir);
                    _path = System.IO.Path.Combine(dir, "runtime-latest.jsonl");
                    _writer?.Dispose();
                    _writer = new StreamWriter(_path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                    {
                        AutoFlush = false
                    };
                    _lines = 0;
                    _dropped = 0;
                    _capReported = false;
                    _finalCapReported = false;
                    _lastMode = Level;
                }
                catch (Exception e)
                {
                    _writer = null;
                    try { MelonLogger.Warning($"[KingdomMod.RuntimeLog] init failed: {e.Message}"); } catch { }
                    try { LoaderMod.Instance?.LogToConsole($"Runtime log init failed: {e.Message}"); } catch { }
                }
            }
        }

        public static void Tick()
        {
            if (Time.unscaledTime < _nextFlush) return;
            _nextFlush = Time.unscaledTime + 1f;
            Flush();
        }

        public static void ModeChanged(RuntimeLogLevel level)
        {
            _lastMode = level;
            try { MelonLogger.Msg($"[KingdomMod.RuntimeLog] Extended logging: {Label(level)} -> {_path}"); } catch { }
            try { LoaderMod.Instance?.LogToConsole($"Extended logging: {Label(level)}."); } catch { }
            Event(RuntimeLogLevel.None, "logging", "mode_changed",
                data: Fields(("mode", Label(level)), ("path", _path)));
            Flush();
        }

        public static void Event(RuntimeLogLevel required, string category, string action,
            UnityEngine.Object subject = null,
            UnityEngine.Object target = null,
            IEnumerable<KeyValuePair<string, object>> data = null,
            string before = null,
            string after = null,
            Exception exception = null)
        {
            var level = Level;
            if (required != RuntimeLogLevel.None && level < required) return;

            lock (Sync)
            {
                if (_writer == null) return;
                if (_lines >= MaxLinesPerSession)
                {
                    _dropped++;
                    if (!_capReported)
                    {
                        _capReported = true;
                        WriteLineUnlocked(RuntimeLogLevel.None, "logging", "line_cap_reached", null, null,
                            Fields(("maxLines", MaxLinesPerSession), ("droppedEvents", _dropped)), null, null, null);
                    }
                    return;
                }

                WriteLineUnlocked(level, category, action, subject, target, data, before, after, exception);
            }
        }

        public static IReadOnlyList<KeyValuePair<string, object>> Fields(params (string Key, object Value)[] fields)
        {
            var list = new List<KeyValuePair<string, object>>(fields.Length);
            for (int i = 0; i < fields.Length; i++)
                list.Add(new KeyValuePair<string, object>(fields[i].Key, fields[i].Value));
            return list;
        }

        public static void Flush()
        {
            lock (Sync)
            {
                try { _writer?.Flush(); }
                catch { }
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                try
                {
                    WriteFinalCapReportUnlocked();
                    _writer?.Flush();
                    _writer?.Dispose();
                }
                catch { }
                finally
                {
                    _writer = null;
                }
            }
        }

        public static string Label(RuntimeLogLevel level)
        {
            return level switch
            {
                RuntimeLogLevel.BugFocused => "Bug-focused",
                RuntimeLogLevel.EventHeavy => "Event-heavy",
                RuntimeLogLevel.MaximumRaw => "Maximum raw",
                _ => "None"
            };
        }

        private static void WriteLineUnlocked(RuntimeLogLevel level, string category, string action,
            UnityEngine.Object subject,
            UnityEngine.Object target,
            IEnumerable<KeyValuePair<string, object>> data,
            string before,
            string after,
            Exception exception)
        {
            try
            {
                var sb = new StringBuilder(1024);
                var first = true;
                sb.Append('{');
                Add(sb, ref first, "timestamp", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
                Add(sb, ref first, "frame", Safe(() => Time.frameCount));
                Add(sb, ref first, "time", Safe(() => Time.time));
                Add(sb, ref first, "level", Label(level));
                Add(sb, ref first, "category", category);
                Add(sb, ref first, "action", action);
                Add(sb, ref first, "scene", Safe(() => SceneManager.GetActiveScene().name));
                Add(sb, ref first, "day", SafeNullable(() => Kingdom.Time.DaysInReign));
                Add(sb, ref first, "island", SafeNullable(() => Kingdom.Game.CurrentLand));
                Add(sb, ref first, "season", SafeNullable(() => Kingdom.Time.CurrentSeason.ToString()));
                AddObject(sb, ref first, "subject", subject);
                AddObject(sb, ref first, "target", target);
                if (data != null) AddFieldsObject(sb, ref first, "data", data);
                if (before != null) Add(sb, ref first, "before", before);
                if (after != null) Add(sb, ref first, "after", after);
                if (exception != null) AddException(sb, ref first, exception);
                sb.Append('}');
                _writer.WriteLine(sb.ToString());
                _lines++;
            }
            catch (Exception e)
            {
                try { MelonLogger.Warning($"[KingdomMod.RuntimeLog] write failed: {e.Message}"); } catch { }
            }
        }

        private static void WriteFinalCapReportUnlocked()
        {
            if (_writer == null) return;
            if (!_capReported || _finalCapReported) return;
            _finalCapReported = true;
            WriteLineUnlocked(RuntimeLogLevel.None, "logging", "line_cap_final", null, null,
                Fields(("maxLines", MaxLinesPerSession), ("droppedEvents", _dropped)), null, null, null);
        }

        private static void AddObject(StringBuilder sb, ref bool first, string key, UnityEngine.Object obj)
        {
            if (obj == null) return;
            AddName(sb, ref first, key);
            sb.Append('{');
            var inner = true;
            Add(sb, ref inner, "name", Safe(() => obj.name));
            Add(sb, ref inner, "type", Safe(() => obj.GetIl2CppType().Name));
            if (obj is Component component)
                AddGameObjectDetails(sb, ref inner, component.gameObject);
            else if (obj is GameObject go)
                AddGameObjectDetails(sb, ref inner, go);
            sb.Append('}');
        }

        private static void AddGameObjectDetails(StringBuilder sb, ref bool first, GameObject go)
        {
            if (go == null) return;
            Add(sb, ref first, "path", Safe(() => TransformPath(go)));
            Add(sb, ref first, "activeSelf", Safe(() => go.activeSelf));
            Add(sb, ref first, "activeInHierarchy", Safe(() => go.activeInHierarchy));
            Add(sb, ref first, "sceneHandle", Safe(() => go.scene.handle));
            Add(sb, ref first, "sceneName", Safe(() => go.scene.name));
            Add(sb, ref first, "position", Safe(() => Vector3Text(go.transform.position)));
        }

        private static void AddFieldsObject(StringBuilder sb, ref bool first, string key, IEnumerable<KeyValuePair<string, object>> fields)
        {
            AddName(sb, ref first, key);
            sb.Append('{');
            var inner = true;
            foreach (var field in fields)
                Add(sb, ref inner, field.Key, field.Value);
            sb.Append('}');
        }

        private static void AddException(StringBuilder sb, ref bool first, Exception exception)
        {
            AddName(sb, ref first, "exception");
            sb.Append('{');
            var inner = true;
            Add(sb, ref inner, "type", exception.GetType().Name);
            Add(sb, ref inner, "message", exception.Message);
            sb.Append('}');
        }

        private static void Add(StringBuilder sb, ref bool first, string key, object value)
        {
            AddName(sb, ref first, key);
            AddValue(sb, value);
        }

        private static void AddName(StringBuilder sb, ref bool first, string key)
        {
            if (!first) sb.Append(',');
            first = false;
            AddString(sb, key);
            sb.Append(':');
        }

        private static void AddValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(f.ToString("0.######", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("0.######", CultureInfo.InvariantCulture));
                    break;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    AddString(sb, value.ToString());
                    break;
            }
        }

        private static void AddString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

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
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }

        private static string TransformPath(GameObject go)
        {
            var t = go.transform;
            var path = go.name;
            while (t != null && t.parent != null)
            {
                t = t.parent;
                path = t.gameObject.name + "/" + path;
            }
            return path;
        }

        private static string Vector3Text(Vector3 v)
        {
            return v.x.ToString("0.###", CultureInfo.InvariantCulture) + ","
                   + v.y.ToString("0.###", CultureInfo.InvariantCulture) + ","
                   + v.z.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static T Safe<T>(Func<T> getter)
        {
            try { return getter(); }
            catch { return default; }
        }

        private static object SafeNullable<T>(Func<T> getter)
        {
            try { return getter(); }
            catch { return null; }
        }
    }
}

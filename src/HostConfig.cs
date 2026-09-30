using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Echopunks
{
    /// <summary>
    /// Minimal user configuration: a flat {"dotted.key": "value"} JSON file at
    /// %LOCALAPPDATA%\Echopunks\settings.json — the same on-disk shape as WrathAccess's settings
    /// persistence, so the full Setting-tree port can adopt the file unchanged later. Read once,
    /// lazily; absent or broken file just means defaults. Host-side (the speech stack reads it
    /// before any module exists); the module reads and writes it too (InternalsVisibleTo) — Set
    /// rewrites the whole file, values as strings. Parsing is in-box JavaScriptSerializer — no
    /// shipped JSON library.
    /// </summary>
    internal static class HostConfig
    {
        private static Dictionary<string, string> _values;
        private static bool _unreadable; // the file exists but failed to parse — Set won't clobber it
        private static readonly object Gate = new object();

        internal static string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Echopunks", "settings.json");

        /// <summary>Where the file lived while the mod was named ExaAccess (releases up to 0.1.2).
        /// Copied to the default SettingsPath once, when no new file exists yet, so the rename
        /// never silently resets a user's speech.output choice. Tests redirect SettingsPath and
        /// are never migrated.</summary>
        private static readonly string LegacySettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExaAccess", "settings.json");
        private static readonly string DefaultSettingsPath = SettingsPath;

        public static string Get(string key, string fallback)
        {
            lock (Gate)
            {
                if (_values == null) _values = LoadFile(SettingsPath);
                string v;
                return _values.TryGetValue(key, out v) && !string.IsNullOrEmpty(v) ? v : fallback;
            }
        }

        public static bool GetBool(string key, bool fallback)
        {
            bool b;
            return bool.TryParse(Get(key, null), out b) ? b : fallback;
        }

        /// <summary>Store a value and rewrite the file (every other key kept). Written to a temp file
        /// then swapped in, so a crash mid-write never leaves a truncated settings.json. Failure is a
        /// log line — the in-memory value still holds for this session.</summary>
        public static void Set(string key, string value)
        {
            lock (Gate)
            {
                if (_values == null) _values = LoadFile(SettingsPath);
                _values[key] = value;
                if (_unreadable)
                {
                    // Rewriting would replace a hand-edited file (a typo away from valid) with
                    // just the keys set this session — keep the user's file, lose only persistence.
                    Log.Warning("[config] " + SettingsPath + " is unreadable; not overwriting it (" + key + " holds for this session only).");
                    return;
                }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                    string tmp = SettingsPath + ".tmp";
                    File.WriteAllText(tmp, Serialize(_values));
                    if (File.Exists(SettingsPath)) File.Replace(tmp, SettingsPath, null);
                    else File.Move(tmp, SettingsPath);
                }
                catch (Exception ex)
                {
                    Log.Warning("[config] could not write " + SettingsPath + " (" + ex.Message + ")");
                }
            }
        }

        public static void SetBool(string key, bool value) => Set(key, value ? "true" : "false");

        // One key per line, sorted, so the file stays hand-editable.
        private static string Serialize(Dictionary<string, string> values)
        {
            var json = new JavaScriptSerializer();
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder("{\n");
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append("  ").Append(json.Serialize(keys[i])).Append(": ").Append(json.Serialize(values[keys[i]]));
                sb.Append(i < keys.Count - 1 ? ",\n" : "\n");
            }
            return sb.Append("}\n").ToString();
        }

        /// <summary>Test seam: forget the cache so the next Get re-reads SettingsPath.</summary>
        internal static void ResetForTests()
        {
            lock (Gate) { _values = null; _unreadable = false; }
        }

        private static Dictionary<string, string> LoadFile(string path)
        {
            _unreadable = false;
            try
            {
                if (!File.Exists(path) && path == DefaultSettingsPath && File.Exists(LegacySettingsPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.Copy(LegacySettingsPath, path);
                    Log.Info("[config] migrated settings from " + LegacySettingsPath);
                }
                if (!File.Exists(path)) return new Dictionary<string, string>();
                var raw = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path))
                          ?? new Dictionary<string, object>();
                var result = new Dictionary<string, string>();
                foreach (var kv in raw)
                    result[kv.Key] = kv.Value?.ToString();
                Log.Info("[config] loaded " + result.Count + " setting(s) from " + path);
                return result;
            }
            catch (Exception ex)
            {
                Log.Warning("[config] could not read " + path + " (" + ex.Message + ") — using defaults.");
                _unreadable = File.Exists(path);
                return new Dictionary<string, string>();
            }
        }
    }
}

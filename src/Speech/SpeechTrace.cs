using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Echopunks.Speech
{
    /// <summary>
    /// Speech diagnostics: every utterance gets a sequence number, and every log line written while
    /// it is in flight (manager decisions, handler calls, native results) carries it — so a log reader
    /// can follow one string from its call site to the engine(s) that received it. Written for the
    /// "NVDA and SAPI both speak, only in cutscenes" reports (the Prism 0.16 AVX-512 UTF-8 bug).
    /// The DETAILED trace (<see cref="Line"/>, <see cref="Verbose"/>: every utterance's text, caller,
    /// handler list, native results, timings) is OFF unless asked for — `"speech.trace": "true"` in
    /// settings.json, or ECHOPUNKS_SPEECH_TRACE=1 (env wins; "0" forces off) — in every build,
    /// Debug included. Rare, load-bearing events (<see cref="Notable"/>/<see cref="Warn"/>: rejected
    /// utterances, fallbacks, non-Ok native results, engine loads) always log.
    /// The in-flight number is per thread: utterances serialize under SpeechManager's gate, but the
    /// SAPI completion events arrive on their own threads and carry their number explicitly.
    /// </summary>
    internal static class SpeechTrace
    {
        private static long _seq;
        [ThreadStatic] private static long _current;
        private static bool? _enabled;

        public const string SettingKey = "speech.trace";
        public const string EnvVar = "ECHOPUNKS_SPEECH_TRACE";

        /// <summary>Whether the detailed trace is on. Read once per process (a restart applies a change).</summary>
        public static bool Enabled
        {
            get
            {
                if (_enabled == null)
                {
                    bool on = false;
                    try { on = ResolveEnabled(Environment.GetEnvironmentVariable(EnvVar), HostConfig.GetBool(SettingKey, false)); }
                    catch { }
                    _enabled = on;
                }
                return _enabled.Value;
            }
        }

        /// <summary>The env var, when it parses, overrides the setting. Internal for the unit tests.</summary>
        internal static bool ResolveEnabled(string env, bool setting)
        {
            if (!string.IsNullOrEmpty(env))
            {
                string v = env.Trim();
                if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
                if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return setting;
        }

        /// <summary>Test seam: force the flag (null = re-read on next use).</summary>
        internal static void SetEnabledForTests(bool? value) { _enabled = value; }

        public static long Begin() { _current = Interlocked.Increment(ref _seq); return _current; }
        public static void End() { _current = 0; }
        public static long Current => _current;

        public static string Tag => _current > 0 ? "[speech #" + _current + "]" : "[speech]";

        /// <summary>Detailed trace, under the in-flight utterance's tag. Off unless the flag is set.</summary>
        public static void Line(string message) { if (Enabled) Write(Tag + " " + message); }

        /// <summary>Detailed trace, caller supplies the whole line. Off unless the flag is set.</summary>
        public static void Verbose(string message) { if (Enabled) Write(message); }

        /// <summary>Always logged, under the in-flight utterance's tag: rare events worth a line in every log.</summary>
        public static void Notable(string message) => Write(Tag + " " + message);

        /// <summary>Always logged as a warning, under the in-flight utterance's tag.</summary>
        public static void Warn(string message)
        {
            try { Log.Warning(Tag + " " + message); } catch { }
        }

        private static void Write(string message)
        {
            try { Log.Info(message); } catch { }
        }

        /// <summary>The text as one log line: quoted, control characters escaped.</summary>
        public static string Quote(string s)
        {
            if (s == null) return "<null>";
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c == '"') sb.Append("\\\"");
                else if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("X4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>The managed call chain that asked for speech: the first few frames outside the
        /// speech stack, innermost first ("Navigator.Speak <- GraphNavigator.Announce <- …").</summary>
        public static string Caller(int depth = 4)
        {
            try
            {
                var frames = new StackTrace(1, false).GetFrames();
                if (frames == null) return "<unknown>";
                var sb = new StringBuilder();
                int taken = 0;
                foreach (var f in frames)
                {
                    var m = f.GetMethod();
                    if (m == null) continue;
                    var t = m.DeclaringType;
                    if (t != null && t.Namespace == typeof(SpeechTrace).Namespace) continue;
                    if (taken > 0) sb.Append(" <- ");
                    sb.Append(t == null ? "?" : t.FullName).Append('.').Append(m.Name);
                    if (++taken >= depth) break;
                }
                return taken == 0 ? "<speech stack>" : sb.ToString();
            }
            catch (Exception ex) { return "<stack unavailable: " + ex.Message + ">"; }
        }

        public static string ThreadInfo()
        {
            var t = Thread.CurrentThread;
            return t.ManagedThreadId + (string.IsNullOrEmpty(t.Name) ? "" : " '" + t.Name + "'")
                + (t.IsThreadPoolThread ? " pool" : "");
        }

        public static long Ms(Stopwatch sw) => sw.ElapsedMilliseconds;
    }
}

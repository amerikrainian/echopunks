using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Echopunks.Speech
{
    /// <summary>
    /// The call-site speech facade — the mod's single output chokepoint (hard rule). Cleans
    /// game-sourced whitespace, mirrors every line to the DEBUG dev tap, and routes through
    /// <see cref="SpeechManager"/>'s handler chain (Prism → SAPI → clipboard, or the user's
    /// configured output). Never interrupts by default (the SayTheSpire house preference).
    /// Host-side: the engines it fronts hold native/OS resources that survive module reloads.
    /// Thread-safe — the engine gate lives in SpeechManager.
    /// </summary>
    public static class Tts
    {
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        public static bool Ready => SpeechManager.HasLoadedHandler;

#if DEBUG
        /// <summary>Dev-only tap: every spoken string is mirrored here so the dev server's /speech log
        /// can read back what was said (we can't hear the TTS). Null in a normal run.</summary>
        public static Action<string> Observer;
        private static void Tap(string text) { if (!string.IsNullOrEmpty(text)) { try { Observer?.Invoke(text); } catch { } } }
#else
        private static void Tap(string text) { }
#endif

        /// <summary>Warm up the configured output now so boot can announce (and log) the outcome.
        /// Safe to call once at boot; false means no engine loaded — Speak stays a silent no-op.</summary>
        public static bool Init()
        {
            SpeechTrace.Notable(SpeechTrace.Enabled
                ? "detailed speech trace ON (" + SpeechTrace.SettingKey + " / " + SpeechTrace.EnvVar + ")."
                : "detailed speech trace off; set \"" + SpeechTrace.SettingKey + "\": \"true\" in settings.json to enable.");
            if (SpeechTrace.Enabled)
            {
                SpeechTrace.Verbose("[speech] init: process " + (Environment.Is64BitProcess ? "x64" : "x86")
                    + ", OS " + Environment.OSVersion + ", CLR " + Environment.Version
                    + ", cwd " + Environment.CurrentDirectory);
                SpeechTrace.Verbose("[speech] screen readers running: " + RunningScreenReaders());
            }
            return SpeechManager.WarmUp();
        }

        private static string RunningScreenReaders()
        {
            var found = new System.Collections.Generic.List<string>();
            foreach (var name in new[] { "nvda", "jfw", "Narrator", "zdsrmain", "WindowEyes", "snova", "dolphin" })
            {
                try
                {
                    var procs = System.Diagnostics.Process.GetProcessesByName(name);
                    if (procs.Length > 0) found.Add(name + " x" + procs.Length);
                    foreach (var p in procs) p.Dispose();
                }
                catch { }
            }
            return found.Count == 0 ? "none found" : string.Join(", ", found);
        }

        /// <summary>Speak (and braille, where the engine supports it). Queued by default.</summary>
        public static void Speak(string text, bool interrupt = false)
        {
            string raw = text;
            if (!string.IsNullOrEmpty(text)) text = Clean(text);
            if (string.IsNullOrEmpty(text))
            {
                if (SpeechTrace.Enabled)
                    SpeechTrace.Verbose("[speech] dropped an empty utterance (raw " + SpeechTrace.Quote(raw)
                        + ", interrupt=" + interrupt + ") from " + SpeechTrace.Caller());
                return;
            }
            Tap(text);
            SpeechManager.Output(text, interrupt, raw);
        }

        public static void Stop()
        {
            if (SpeechTrace.Enabled) SpeechTrace.Verbose("[speech] Stop requested by " + SpeechTrace.Caller());
            SpeechManager.Silence();
        }

        public static void Shutdown() => SpeechManager.Shutdown();

        // EXAPUNKS labels aren't TMP rich text (that was WotR/Unity), but many strings carry embedded
        // newlines/tabs and runs of spaces — collapse them so speech doesn't stutter.
        // Internal for the unit tests.
        internal static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsControl(c) ? ' ' : c);
            return Whitespace.Replace(sb.ToString(), " ").Trim();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Echopunks.Speech
{
    /// <summary>
    /// Handler registry + selection, ported from WrathAccess: a fixed priority list (Prism first —
    /// the user's own screen reader — then SAPI, then clipboard), lazy Detect/Load per handler, and
    /// an auto chain that walks the list until something loads. The user picks an output with the
    /// `speech.output` key in %LOCALAPPDATA%\Echopunks\settings.json ("auto" | "prism" | "sapi" |
    /// "clipboard"; the ECHOPUNKS_SPEECH env var overrides for dev runs); anything unknown or broken
    /// resolves back through auto — never strand a blind user with no voice.
    /// Host-side and stateful across module reloads. All engine calls serialize under one gate: the
    /// game thread and the dev server's HTTP thread both speak.
    /// With the speech trace on (<see cref="SpeechTrace"/>), every call is traced to the mod log: the
    /// text, its caller, the handler list with priorities and states, the choice, and each engine's
    /// verdict. Rejections and fallbacks log always.
    /// </summary>
    internal static class SpeechManager
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<ISpeechHandler> Loaded = new HashSet<ISpeechHandler>();
        // Last Detect/Load outcome per handler, for the per-call handler listing.
        private static readonly Dictionary<ISpeechHandler, string> State = new Dictionary<ISpeechHandler, string>();

        // Priority order IS the auto chain. Internal-settable so tests can inject fakes.
        internal static IList<ISpeechHandler> Handlers = new List<ISpeechHandler>
        {
            new PrismHandler(),
            new SapiHandler(),
            new ClipboardHandler(),
        };

        public static string OutputKey
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("ECHOPUNKS_SPEECH");
                if (!string.IsNullOrEmpty(env)) return env;
                return HostConfig.Get("speech.output", "auto");
            }
        }

        /// <summary>Where <see cref="OutputKey"/> came from, for the log.</summary>
        private static string OutputKeySource()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ECHOPUNKS_SPEECH"))) return "env ECHOPUNKS_SPEECH";
            return HostConfig.Get("speech.output", null) != null ? "settings.json speech.output" : "default";
        }

        public static bool HasLoadedHandler { get { lock (Gate) return Loaded.Count > 0; } }

        /// <summary>Resolve OutputKey to a loaded handler now (boot warm-up), so the first real
        /// utterance doesn't pay the load cost and boot can log/announce the outcome.</summary>
        public static bool WarmUp()
        {
            lock (Gate)
            {
                if (SpeechTrace.Enabled) SpeechTrace.Verbose("[speech] warm-up; " + DescribeHandlers());
                var handler = ResolveHandler(OutputKey);
                if (SpeechTrace.Enabled) SpeechTrace.Verbose("[speech] warm-up chose " + (handler == null ? "NOTHING" : handler.Key) + "; " + DescribeHandlers());
                return handler != null;
            }
        }

        public static void Output(string text, bool interrupt) => Output(text, interrupt, text);

        /// <param name="raw">The caller's text before <see cref="Tts.Clean"/>, logged when it differs.</param>
        public static void Output(string text, bool interrupt, string raw)
        {
            bool trace = SpeechTrace.Enabled;
            string caller = trace ? SpeechTrace.Caller() : null;
            lock (Gate)
            {
                SpeechTrace.Begin();
                var sw = Stopwatch.StartNew();
                try
                {
                    if (trace)
                    {
                        SpeechTrace.Line("speak interrupt=" + interrupt + " thread=" + SpeechTrace.ThreadInfo() + " caller=" + caller);
                        SpeechTrace.Line("text (" + text.Length + " chars): " + SpeechTrace.Quote(text));
                        if (raw != null && raw != text) SpeechTrace.Line("raw before cleaning: " + SpeechTrace.Quote(raw));
                        SpeechTrace.Line(DescribeHandlers());
                    }

                    var handler = ResolveHandler(OutputKey);
                    if (handler == null) { SpeechTrace.Warn("no handler available, utterance dropped: " + SpeechTrace.Quote(text)); return; }
                    if (trace) SpeechTrace.Line("chose " + handler.Key + " (priority " + Priority(handler) + ")");
                    try
                    {
                        if (Attempt(handler, text, interrupt)) return;
                        // The chosen engine rejected the utterance (device lost, backend died): retry down
                        // the priority chain PAST the failing handler rather than going silent.
                        SpeechTrace.Warn(handler.Key + " rejected " + SpeechTrace.Quote(text)
                            + " (interrupt=" + interrupt + "), walking the fallback chain.");
                        foreach (var h in Handlers)
                        {
                            if (ReferenceEquals(h, handler)) continue;
                            if (!EnsureLoaded(h)) { SpeechTrace.Notable("fallback " + h.Key + " unavailable (" + StateOf(h) + ")."); continue; }
                            SpeechTrace.Notable("fallback to " + h.Key + " (priority " + Priority(h) + ")");
                            if (Attempt(h, text, interrupt)) { SpeechTrace.Notable(h.Key + " accepted the fallback."); return; }
                            SpeechTrace.Notable(h.Key + " rejected the fallback too.");
                        }
                        Log.Warning(SpeechTrace.Tag + " every handler rejected an utterance.");
                    }
                    catch (Exception ex) { Log.Error(SpeechTrace.Tag + " output failed: " + ex); }
                }
                finally
                {
                    if (trace) SpeechTrace.Line("done in " + SpeechTrace.Ms(sw) + " ms; " + DescribeHandlers());
                    SpeechTrace.End();
                }
            }
        }

        private static bool Attempt(ISpeechHandler handler, string text, bool interrupt)
        {
            var sw = Stopwatch.StartNew();
            bool ok;
            try { ok = handler.Output(text, interrupt); }
            catch (Exception ex)
            {
                SpeechTrace.Warn(handler.Key + ".Output threw after " + SpeechTrace.Ms(sw) + " ms: " + ex);
                return false;
            }
            SpeechTrace.Line(handler.Key + ".Output -> " + (ok ? "accepted" : "REJECTED") + " in " + SpeechTrace.Ms(sw) + " ms");
            return ok;
        }

        public static void Silence()
        {
            lock (Gate)
            {
                if (SpeechTrace.Enabled) SpeechTrace.Verbose("[speech] silence: stopping loaded handlers; " + DescribeHandlers());
                foreach (var h in Loaded)
                    try { h.Silence(); } catch (Exception ex) { SpeechTrace.Notable(h.Key + ".Silence threw: " + ex.Message); }
            }
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                if (SpeechTrace.Enabled) SpeechTrace.Verbose("[speech] shutdown: unloading; " + DescribeHandlers());
                foreach (var h in Loaded)
                {
                    try { h.Unload(); } catch (Exception ex) { SpeechTrace.Notable(h.Key + ".Unload threw: " + ex.Message); }
                    State[h] = "unloaded";
                }
                Loaded.Clear();
            }
        }

        /// <summary>"auto"/null/"" walks the priority list to the first loadable handler; a known key
        /// loads that handler or falls back to auto; an unknown key logs and goes auto. Call under
        /// <see cref="Gate"/>.</summary>
        internal static ISpeechHandler ResolveHandler(string key)
        {
            if (string.IsNullOrEmpty(key) || key == "auto")
            {
                foreach (var handler in Handlers)
                    if (EnsureLoaded(handler)) return handler;
                Log.Error(SpeechTrace.Tag + " no speech handler could be loaded!");
                return null;
            }

            foreach (var handler in Handlers)
                if (handler.Key == key)
                {
                    if (EnsureLoaded(handler)) return handler;
                    SpeechTrace.Notable("configured output '" + key + "' unavailable (" + StateOf(handler) + "), resolving auto.");
                    return ResolveHandler("auto");
                }

            Log.Error(SpeechTrace.Tag + " unknown speech output '" + key + "' — using auto.");
            return ResolveHandler("auto");
        }

        private static bool EnsureLoaded(ISpeechHandler handler)
        {
            if (Loaded.Contains(handler)) return true;
            SpeechTrace.Line("loading handler " + handler.Key + " (priority " + Priority(handler) + ")");
            try
            {
                if (!handler.Detect())
                {
                    State[handler] = "not detected";
                    Log.Info(SpeechTrace.Tag + " " + handler.Key + ": not detected on this machine.");
                    return false;
                }
                if (!handler.Load())
                {
                    State[handler] = "load failed";
                    Log.Info(SpeechTrace.Tag + " " + handler.Key + ": detected but failed to load.");
                    return false;
                }
                Loaded.Add(handler);
                State[handler] = "loaded";
                Log.Info(SpeechTrace.Tag + " handler loaded: " + handler.Key);
                return true;
            }
            catch (Exception ex)
            {
                State[handler] = "load threw";
                Log.Error(SpeechTrace.Tag + " handler " + handler.Key + " failed: " + ex.Message);
            }
            return false;
        }

        private static int Priority(ISpeechHandler handler) => Handlers.IndexOf(handler) + 1;

        private static string StateOf(ISpeechHandler handler)
        {
            if (Loaded.Contains(handler)) return "loaded";
            string s;
            return State.TryGetValue(handler, out s) ? s : "untried";
        }

        /// <summary>"output key 'auto' (default); handlers by priority: 1 prism [loaded: …], …"</summary>
        private static string DescribeHandlers()
        {
            var sb = new StringBuilder();
            string key;
            try { key = OutputKey; } catch (Exception ex) { key = "<error " + ex.Message + ">"; }
            string source;
            try { source = OutputKeySource(); } catch { source = "?"; }
            sb.Append("output key '").Append(key).Append("' (").Append(source).Append("); handlers by priority: ");
            for (int i = 0; i < Handlers.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var h = Handlers[i];
                sb.Append(i + 1).Append(' ').Append(h.Key).Append(" [").Append(StateOf(h));
                string detail = null;
                if (Loaded.Contains(h))
                    try { detail = (h as IDescribedHandler)?.Describe(); } catch { }
                if (!string.IsNullOrEmpty(detail)) sb.Append(": ").Append(detail);
                sb.Append(']');
            }
            sb.Append("; loaded count ").Append(Loaded.Count);
            return sb.ToString();
        }

        /// <summary>Test seam: forget every loaded handler without unloading (fakes own no resources).</summary>
        internal static void ResetForTests(IList<ISpeechHandler> handlers)
        {
            lock (Gate)
            {
                Loaded.Clear();
                State.Clear();
                Handlers = handlers;
            }
        }
    }

    /// <summary>Optional diagnostics on a handler: what it is driving right now (backend, voice),
    /// shown in the per-call handler listing.</summary>
    internal interface IDescribedHandler
    {
        string Describe();
    }
}

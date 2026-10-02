using System;

namespace Echopunks.Speech
{
    /// <summary>
    /// The primary handler: Prism's best-available backend (NVDA/JAWS directly, else SAPI/OneCore via
    /// Prism). This is the engine logic that used to live inline in <see cref="Tts"/>, reshaped into
    /// the WrathAccess handler contract. prism.dll sits next to the game exe (the process working dir).
    /// </summary>
    internal sealed class PrismHandler : ISpeechHandler, IDescribedHandler
    {
        private IntPtr _ctx = IntPtr.Zero;
        private IntPtr _backend = IntPtr.Zero;
        private PrismNative.BackendFeatures _features;
        private string _backendName;

        public string Key => "prism";

        public string Describe() => _backend == IntPtr.Zero ? "no backend"
            : "backend " + (_backendName ?? "<unknown>") + ", features " + _features;

        /// <summary>Every backend prism.dll knows, with its priority and whether it exists in this
        /// build — the list create_best chose from.</summary>
        private void LogRegistry()
        {
            try
            {
                ulong count = (ulong)PrismNative.RegistryCount(_ctx);
                var sb = new System.Text.StringBuilder("Prism registry (" + count + " backends): ");
                for (ulong i = 0; i < count; i++)
                {
                    ulong id = PrismNative.RegistryIdAt(_ctx, (UIntPtr)i);
                    if (i > 0) sb.Append(", ");
                    sb.Append(PrismNative.RegistryName(_ctx, id) ?? "<unnamed>")
                      .Append(" (id ").Append(id)
                      .Append(", priority ").Append(PrismNative.RegistryPriority(_ctx, id))
                      .Append(PrismNative.RegistryExists(_ctx, id) ? ", exists" : ", absent")
                      .Append(')');
                }
                SpeechTrace.Line(sb.ToString());
            }
            catch (Exception ex) { SpeechTrace.Line("Prism registry listing failed: " + ex.Message); }
        }

        public bool Detect()
        {
            // The real probe is loading; the dll may be present but backendless (no screen reader, no
            // SAPI — rare). Cheap existence check first so a missing dll doesn't throw per boot.
            try
            {
                return System.IO.File.Exists(System.IO.Path.Combine(Environment.CurrentDirectory, "prism.dll"))
                    || System.IO.File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prism.dll"));
            }
            catch { return true; } // fall through to Load, which handles failure properly
        }

        // Prism's own log, routed into ours. Rooted for the process lifetime: native code holds the
        // pointer and may call it from any thread, at any time, until exit.
        private static PrismNative.LogCallback _logCallback;

        private static void HookPrismLog()
        {
            try
            {
                if (_logCallback == null) _logCallback = OnPrismLog;
                PrismNative.SetLogHandler(new PrismNative.LogHandler
                {
                    Fn = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_logCallback),
                    Userdata = IntPtr.Zero,
                });
                PrismNative.SetLogLevel(PrismNative.LogLevel.Info);
            }
            catch (Exception ex) { SpeechTrace.Notable("Prism log hookup failed: " + ex.Message); }
        }

        private static void OnPrismLog(IntPtr userdata, PrismNative.LogLevel level, IntPtr source, IntPtr message)
        {
            try { Log.Info("[prism " + level + "] " + (PrismNative.Utf8(source) ?? "?") + ": " + PrismNative.Utf8(message)); }
            catch { }
        }

        public bool Load()
        {
            try
            {
                HookPrismLog();
                try { SpeechTrace.Notable("prism.dll version " + (PrismNative.VersionString() ?? "<unknown>")); }
                catch (EntryPointNotFoundException) { SpeechTrace.Notable("prism.dll predates prism_version_string."); }
                _ctx = PrismNative.Init(IntPtr.Zero);
                if (_ctx == IntPtr.Zero) { Log.Error("[speech] prism_init returned null (dll loaded but init failed)."); return false; }
                LogRegistry();

                _backend = PrismNative.RegistryCreateBest(_ctx);
                if (_backend == IntPtr.Zero) { Log.Error("[speech] no usable Prism backend on this machine."); Unload(); return false; }

                var err = PrismNative.BackendInitialize(_backend);
                if (err != PrismNative.PrismError.Ok && err != PrismNative.PrismError.AlreadyInitialized)
                {
                    Log.Error("[speech] Prism backend initialize failed (" + err + ").");
                    Unload();
                    return false;
                }

                _features = (PrismNative.BackendFeatures)PrismNative.BackendGetFeatures(_backend);
                _backendName = PrismNative.BackendName(_backend);
                Log.Info("[speech] Prism backend: " + (_backendName ?? "<unknown>")
                    + " (initialize -> " + err + ", features=0x" + ((ulong)_features).ToString("X") + " = " + _features + ")");
                return true;
            }
            catch (DllNotFoundException)
            {
                Log.Info("[speech] prism.dll not found (or a dependency missing, e.g. the VC++ runtime) — trying the next handler.");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("[speech] Prism load failed", ex);
                Unload();
                return false;
            }
        }

        public void Unload()
        {
            SpeechTrace.Line("prism unload (backend " + (_backendName ?? "<none>") + ")");
            _backendName = null;
            if (_backend != IntPtr.Zero)
            {
                try { PrismNative.BackendStop(_backend); } catch { }
                try { PrismNative.BackendFree(_backend); } catch { }
                _backend = IntPtr.Zero;
            }
            if (_ctx != IntPtr.Zero)
            {
                try { PrismNative.Shutdown(_ctx); } catch { }
                _ctx = IntPtr.Zero;
            }
            _features = 0;
        }

        public bool Speak(string text, bool interrupt)
        {
            if (_backend == IntPtr.Zero) { SpeechTrace.Warn("prism speak: no backend."); return false; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var err = PrismNative.BackendSpeak(_backend, text, interrupt);
            Report("prism_backend_speak", err, interrupt, sw);
            return err == PrismNative.PrismError.Ok;
        }

        public bool Output(string text, bool interrupt)
        {
            if (_backend == IntPtr.Zero) { SpeechTrace.Warn("prism output: no backend."); return false; }
            // prism_backend_output drives speech + braille when supported; else fall back to speak.
            if ((_features & PrismNative.BackendFeatures.SupportsOutput) != 0)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var err = PrismNative.BackendOutput(_backend, text, interrupt);
                Report("prism_backend_output", err, interrupt, sw);
                if (err == PrismNative.PrismError.Ok) return true;
                SpeechTrace.Notable("prism output did not return Ok, falling back to prism_backend_speak"
                    + " (the text may already have been partly delivered).");
            }
            else SpeechTrace.Line("prism backend " + _backendName + " lacks SupportsOutput, using prism_backend_speak.");
            return Speak(text, interrupt);
        }

        // A native call's verdict: Ok only in the detailed trace (with timing and is-speaking — an
        // extra round trip to the screen reader, so never paid when the trace is off); anything
        // else always logs.
        private void Report(string call, PrismNative.PrismError err, bool interrupt, System.Diagnostics.Stopwatch sw)
        {
            if (err == PrismNative.PrismError.Ok)
            {
                if (SpeechTrace.Enabled)
                    SpeechTrace.Line(call + "(" + _backendName + ", interrupt=" + interrupt + ") -> Ok in "
                        + SpeechTrace.Ms(sw) + " ms" + SpeakingState());
            }
            else SpeechTrace.Warn(call + "(" + _backendName + ", interrupt=" + interrupt + ") -> " + err
                + " in " + SpeechTrace.Ms(sw) + " ms");
        }

        // Diagnostics: the backend's own is-speaking answer right after a call, when it has one.
        private string SpeakingState()
        {
            if ((_features & PrismNative.BackendFeatures.SupportsIsSpeaking) == 0) return "";
            try { return ", is_speaking=" + PrismNative.BackendIsSpeaking(_backend); }
            catch (Exception ex) { return ", is_speaking threw " + ex.Message; }
        }

        public void Silence()
        {
            if (_backend == IntPtr.Zero) return;
            try { SpeechTrace.Line("prism_backend_stop(" + _backendName + ") -> " + PrismNative.BackendStop(_backend)); }
            catch (Exception ex) { SpeechTrace.Notable("prism_backend_stop threw " + ex.Message); }
        }
    }
}

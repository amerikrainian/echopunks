using System;
using System.Collections.Generic;
using System.Speech.Synthesis;

namespace Echopunks.Speech
{
    /// <summary>
    /// SAPI fallback for machines with no screen reader and no working Prism. WrathAccess drives SAPI
    /// through 272 lines of hand-rolled IDispatch COM because Unity's Mono can't host the interop —
    /// this is real .NET Framework 4.8, so the in-box System.Speech synthesizer replaces all of it.
    /// Async speech (never blocks the game thread); interrupt cancels the queue first.
    /// With the speech trace on, the synthesizer's own started/completed events are logged with the
    /// utterance number, so the log shows whether SAPI actually voiced a line, not just that it was
    /// handed one (errors log always).
    /// </summary>
    internal sealed class SapiHandler : ISpeechHandler, IDescribedHandler
    {
        private SpeechSynthesizer _synth;
        private readonly Dictionary<Prompt, long> _pending = new Dictionary<Prompt, long>();

        public string Key => "sapi";

        public string Describe()
        {
            if (_synth == null) return "no synthesizer";
            try { return "voice " + (_synth.Voice?.Name ?? "<default>") + ", state " + _synth.State + ", queued " + PendingCount(); }
            catch (Exception ex) { return "describe failed: " + ex.Message; }
        }

        public bool Detect() => true; // in-box on every Windows this game runs on

        public bool Load()
        {
            try
            {
                SpeechTrace.Notable("SAPI load requested by " + SpeechTrace.Caller(8));
                _synth = new SpeechSynthesizer();
                _synth.SetOutputToDefaultAudioDevice();
                _synth.SpeakStarted += OnStarted;
                _synth.SpeakCompleted += OnCompleted;
                Log.Info("[speech] SAPI voice: " + (_synth.Voice?.Name ?? "<default>"));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[speech] SAPI load failed", ex);
                Unload();
                return false;
            }
        }

        public void Unload()
        {
            SpeechTrace.Line("SAPI unload");
            try
            {
                if (_synth != null)
                {
                    _synth.SpeakStarted -= OnStarted;
                    _synth.SpeakCompleted -= OnCompleted;
                    _synth.Dispose();
                }
            }
            catch { }
            _synth = null;
            lock (_pending) _pending.Clear();
        }

        public bool Speak(string text, bool interrupt)
        {
            if (_synth == null) { SpeechTrace.Warn("SAPI speak: no synthesizer."); return false; }
            try
            {
                if (interrupt)
                {
                    SpeechTrace.Line("SAPI SpeakAsyncCancelAll (interrupt), " + PendingCount() + " queued");
                    _synth.SpeakAsyncCancelAll();
                }
                var prompt = _synth.SpeakAsync(text);
                lock (_pending) _pending[prompt] = SpeechTrace.Current;
                if (SpeechTrace.Enabled) SpeechTrace.Line("SAPI SpeakAsync queued (voice " + (_synth.Voice?.Name ?? "<default>")
                    + ", state " + _synth.State + ", " + PendingCount() + " queued)");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(SpeechTrace.Tag + " SAPI speak failed: " + ex.Message);
                return false;
            }
        }

        public bool Output(string text, bool interrupt) => Speak(text, interrupt); // no braille via SAPI

        public void Silence()
        {
            SpeechTrace.Line("SAPI SpeakAsyncCancelAll (silence), " + PendingCount() + " queued");
            try { _synth?.SpeakAsyncCancelAll(); } catch { }
        }

        private int PendingCount() { lock (_pending) return _pending.Count; }

        private long SeqOf(Prompt p, bool remove)
        {
            if (p == null) return 0;
            lock (_pending)
            {
                long seq;
                if (!_pending.TryGetValue(p, out seq)) return 0;
                if (remove) _pending.Remove(p);
                return seq;
            }
        }

        // Synthesizer events arrive on their own threads, outside any in-flight utterance.
        private void OnStarted(object sender, SpeakStartedEventArgs e)
        {
            try { SpeechTrace.Verbose("[speech #" + SeqOf(e.Prompt, false) + "] SAPI started speaking"); } catch { }
        }

        private void OnCompleted(object sender, SpeakCompletedEventArgs e)
        {
            try
            {
                long seq = SeqOf(e.Prompt, true);
                if (e.Error != null) { SpeechTrace.Warn("SAPI #" + seq + " failed: " + e.Error.Message); return; }
                SpeechTrace.Verbose("[speech #" + seq + "] SAPI " + (e.Cancelled ? "cancelled" : "finished speaking"));
            }
            catch { }
        }
    }
}

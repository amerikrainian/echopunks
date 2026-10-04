using System;
using System.Collections.Generic;

namespace Echopunks.Audio
{
    /// <summary>
    /// Synthesized beeps through the GAME'S OWN mixer. GClass372 is the game's software mixer: one
    /// SDL device (44.1 kHz, S16, stereo) whose callback sums every active voice; a GClass5 is a
    /// voice source made of interleaved stereo samples in memory (the game fills it from WAVs, but
    /// nothing needs a file). We synthesize the samples and hand the clip to the same public play
    /// call the game's Sound wrapper uses (smethod_4, which locks the device), at the SFX slider's
    /// live volume (GClass1.float_1) — exactly how the game's own effects are leveled. No native
    /// handle of ours, so nothing to keep host-side across reloads. If the game opened no audio
    /// device, its play call is a no-op and so are we.
    ///
    /// ONE VOICE: a new beep cuts the previous one. Fast mode can finish tests every frame; cut
    /// beeps make a rising sweep instead of a pile of overlapping tones.
    /// </summary>
    internal static class Tones
    {
        private const int Rate = 44100;      // GClass372.int_0
        private const double Level = 0.25;   // a gentle peak, before the SFX slider
        private const int FadeMs = 5;        // ramps in/out so a sine never clicks

        private static readonly Dictionary<long, GClass5> _clips = new Dictionary<long, GClass5>();
        private static GClass30 _voice;

        /// <summary>Beeps handed to the mixer this module generation (dev probes read it — we can't hear).</summary>
        internal static int Played;
        internal static double LastHz;

        /// <summary>NVDA's progress-bar beep: 110 Hz at 0 rising four octaves to 1760 Hz at 1
        /// (f = 110 * 2^(4p)), 40 ms.</summary>
        public static void Progress(double fraction)
        {
            fraction = Math.Max(0.0, Math.Min(1.0, fraction));
            Beep(110.0 * Math.Pow(2.0, 4.0 * fraction), 40);
        }

        public static void Beep(double hz, int ms)
        {
            try
            {
                if (GClass372.uint_0 == 0) return; // the game has no audio device
                var clip = Clip(hz, ms);
                if (_voice != null) GClass372.smethod_5(_voice);
                _voice = GClass372.smethod_4(clip, GClass1.float_1, false);
                Played++;
                LastHz = hz;
            }
            catch (Exception ex) { Log.Error("[tones] beep failed", ex); }
        }

        private static GClass5 Clip(double hz, int ms)
        {
            long key = ((long)Math.Round(hz) << 20) | (uint)ms;
            GClass5 clip;
            if (_clips.TryGetValue(key, out clip)) return clip;
            int frames = Rate * ms / 1000;
            int fade = Math.Max(1, Rate * FadeMs / 1000);
            var pcm = new short[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                double env = Math.Min(1.0, Math.Min(i, frames - 1 - i) / (double)fade);
                short v = (short)(Math.Sin(2.0 * Math.PI * hz * i / Rate) * env * Level * short.MaxValue);
                pcm[2 * i] = v;
                pcm[2 * i + 1] = v;
            }
            if (_clips.Count > 256) _clips.Clear(); // progress pitches are 100 at most; a guard only
            clip = new GClass5 { short_0 = pcm };
            _clips[key] = clip;
            return clip;
        }
    }
}

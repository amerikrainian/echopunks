using System;
using System.Reflection;
using Echopunks.Game;
using HarmonyLib;
using SDL2;

namespace Echopunks.Patches
{
    /// <summary>
    /// ONE PRESS = THE NEXT LINE in both cutscene players. The mod speaks every line the moment
    /// the line index changes, but the players pace presses for sighted reading: the first press
    /// only completes the typewriter, a press within 0.25s of completion is dropped, and nothing
    /// is read during door animations (the visual novel), slide-ins, 0.3s pre-delays, the 5s
    /// blackout (EMBER), or a screen transition. So a line already heard took two or more
    /// presses, some silently eaten (user report, 2026-09-30).
    ///
    /// A prefix on each player's per-frame imethod_1 watches the game's OWN advance predicates
    /// (click, Space, Tab, Enter — the exact set the players read, so suppression and the choice
    /// menu see the same thing the game does). On a press it collapses whatever is blocking —
    /// timers to zero, the typewriter past its end — and forces the advance key for the rest of
    /// that frame through <see cref="GameKeySuppression.ForcedKey"/>, so the game's own advance
    /// path runs (sounds, save flags, the zine push, the credits push, voice-clip stop). A press
    /// the scene cannot take yet (a screen transition; the frame ending EMBER's blackout, which
    /// runs its music change) is HELD and applied on the first frame that can — one held press,
    /// expiring after ~2s. Escape (skip) is untouched.
    ///
    /// EMBER specifics: a single scripted player reply is a bubble the game accepts ONLY by click
    /// or the digit 1 (its Enter does nothing there) — the press forces the 1. A real multi-option
    /// choice belongs to the mod's choice menu: a press is dropped there.
    /// </summary>
    internal static class CutsceneAdvance
    {
        // Visual novel (deob GClass255, obfuscated live — Deobf bridges the T rows).
        private static readonly FieldInfo VnDoor = Deobf.Field(typeof(GClass255), "float_5");       // door animation timer
        private static readonly FieldInfo VnFade = Deobf.Field(typeof(GClass255), "float_4");       // door overlay alpha
        private static readonly FieldInfo VnOpen = Deobf.Field(typeof(GClass255), "bool_0");        // door open (overlay target 0)
        private static readonly FieldInfo VnTyped = Deobf.Field(typeof(GClass255), "float_6");      // typewriter clock

        // EMBER comic player (Ember2CutsceneScreen, name-preserved).
        private static readonly FieldInfo EmBlackout = Deobf.Field(typeof(Ember2CutsceneScreen), "float_1");
        private static readonly FieldInfo EmDelay = Deobf.Field(typeof(Ember2CutsceneScreen), "float_2");
        private static readonly FieldInfo EmTyped = Deobf.Field(typeof(Ember2CutsceneScreen), "float_3");
        private static readonly FieldInfo EmSlideIn = Deobf.Field(typeof(Ember2CutsceneScreen), "maybe_1");
        private static readonly FieldInfo EmSlideOut = Deobf.Field(typeof(Ember2CutsceneScreen), "maybe_2");
        private static readonly FieldInfo EmPart = Deobf.Field(typeof(Ember2CutsceneScreen), "int_0");
        private static readonly FieldInfo EmLine = Deobf.Field(typeof(Ember2CutsceneScreen), "int_1");
        private static readonly FieldInfo EmChosen = Deobf.Field(typeof(Ember2CutsceneScreen), "bool_1");
        private static readonly FieldInfo EmFullscreen = Deobf.Field(typeof(Ember2CutsceneScreen), "bool_2");
        private static readonly FieldInfo EmVignette = Deobf.Field(typeof(Ember2CutsceneScreen), "vignette_0");

        private const int KeyReturn = 13, KeyDigit1 = 49;
        private const int HoldFrames = 120; // a held press expires (~2s at 60fps)

        private static object _scene; // the scene a held press belongs to
        private static bool _pending;
        private static int _heldFrames;

        public static void Apply(Harmony harmony)
        {
            try
            {
                var finalizer = new HarmonyMethod(typeof(CutsceneAdvance), nameof(Finalizer));
                harmony.Patch(Expr.MethodOf(() => default(GClass255).imethod_1(0f)),
                    prefix: new HarmonyMethod(typeof(CutsceneAdvance), nameof(NovelPrefix)), finalizer: finalizer);
                harmony.Patch(Expr.MethodOf(() => default(Ember2CutsceneScreen).imethod_1(0f)),
                    prefix: new HarmonyMethod(typeof(CutsceneAdvance), nameof(EmberPrefix)), finalizer: finalizer);
                Log.Info("[patch] cutscene one-press advance armed");
            }
            catch (Exception ex) { Log.Error("[patch] cutscene advance failed to apply", ex); }
        }

        // The players' own advance test (GClass255 ~250, Ember2CutsceneScreen ~374/~550).
        private static bool AdvancePressed()
        {
            return GClass64.smethod_29((GEnum156)1) || GClass64.smethod_17((SDL.GEnum195)32)
                || GClass64.smethod_17((SDL.GEnum195)9) || GClass64.smethod_16();
        }

        /// <summary>True while an advance press waits for this scene (a new press, or a held one).</summary>
        private static bool TakePress(object scene)
        {
            if (!FocusMode.Active || !ReferenceEquals(GameState.TopScreen(), scene)) return false;
            if (!ReferenceEquals(scene, _scene)) { _scene = scene; _pending = false; }
            if (AdvancePressed()) { _pending = true; _heldFrames = 0; }
            else if (_pending && ++_heldFrames > HoldFrames) _pending = false;
            return _pending;
        }

        private static void Advance(int keycode)
        {
            GameKeySuppression.ForcedKey = keycode;
            _pending = false;
        }

        private static void NovelPrefix(GClass255 __instance)
        {
            try
            {
                if (!TakePress(__instance)) return;
                if ((float)VnDoor.GetValue(__instance) > 0f)
                {
                    VnDoor.SetValue(__instance, 0f);
                    VnFade.SetValue(__instance, (bool)VnOpen.GetValue(__instance) ? 0f : 1f);
                }
                if (!GameLogic.gameLogic_0.method_4().method_0()) return; // mid-transition: hold it
                VnTyped.SetValue(__instance, 1e6f); // past the typewriter AND its 0.25s cooldown
                Advance(KeyReturn);
            }
            catch (Exception ex) { _pending = false; Log.Error("[cutscene] advance failed", ex); }
        }

        private static void EmberPrefix(Ember2CutsceneScreen __instance)
        {
            try
            {
                if (!TakePress(__instance)) return;
                if ((float)EmBlackout.GetValue(__instance) > 0f)
                {
                    // End the blackout through the game's own last blackout frame (it switches
                    // the music there and returns) — the held press lands next frame.
                    EmBlackout.SetValue(__instance, 1e-4f);
                    return;
                }
                var slideOut = (Maybe<float>)EmSlideOut.GetValue(__instance);
                if (slideOut.method_0())
                {
                    // Leaving (after the last line): finish the slide now — the game pops and
                    // runs its exit action this frame. Nothing more to advance.
                    EmSlideOut.SetValue(__instance, new Maybe<float>(true, 1f));
                    _pending = false;
                    return;
                }
                EmSlideIn.SetValue(__instance, (Maybe<float>)GStruct10.gstruct10_0);
                EmDelay.SetValue(__instance, 0f);

                bool chosen = (bool)EmChosen.GetValue(__instance);
                bool fullscreen = (bool)EmFullscreen.GetValue(__instance);
                var vignette = EmVignette.GetValue(__instance) as Vignette;
                int part = (int)EmPart.GetValue(__instance), line = (int)EmLine.GetValue(__instance);
                var list = vignette == null ? null : part == 0 ? vignette.list_0 : vignette.list_1;
                if (list == null || line < 0 || line >= list.Count) { _pending = false; return; }
                var cur = list[line];
                if (!chosen && !fullscreen && cur.vignetteCharacter_0 != VignetteCharacter.Ember2)
                {
                    if (cur.list_0.Count > 1) { _pending = false; return; } // a real choice: the menu's
                    Advance(KeyDigit1); // the single reply bubble: click or 1 only
                    return;
                }
                EmTyped.SetValue(__instance, 1e6f);
                Advance(KeyReturn);
            }
            catch (Exception ex) { _pending = false; Log.Error("[cutscene] EMBER advance failed", ex); }
        }

        private static Exception Finalizer(Exception __exception)
        {
            GameKeySuppression.ForcedKey = 0;
            return __exception;
        }
    }
}

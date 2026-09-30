using System;
using System.Collections.Generic;
using Echopunks.Game;
using HarmonyLib;
using SDL2;

namespace Echopunks.Patches
{
    /// <summary>
    /// The focus-mode key-suppression seam. Some game screens read the keyboard themselves through
    /// the game's input facade (deob GClass64) — the desktop's arrows/Enter/Home/End move its task
    /// selection. While focus mode is ON and a MODELED screen is focused, our navigator owns those
    /// keys, so the game's readers must see them as not-pressed or every press double-acts (an
    /// arrow would move both our focus and the game's selection).
    ///
    /// Two prefixes cover the whole navigation vocabulary: smethod_17 (just-pressed by keycode —
    /// Enter/KP-Enter, Home/End, and every composed helper such as smethod_15/16 ride it) and
    /// smethod_22 (pressed-with-repeat — the arrow keys incl. their numpad variants, PageUp/Down).
    /// Both are PURE reads of the per-frame snapshot sets (decompile-verified: no consumption, no
    /// side effects), so skipping the original is safe. Only the keys in <see cref="Keys"/> are
    /// swallowed — Escape and everything else pass through untouched (Escape closing the desktop
    /// is native behavior we keep), with ONE exception: while the focused screen reports a
    /// mod-side modal open (<see cref="Screens.Screen.ModalCapturesEscape"/>), Escape is
    /// swallowed so it closes the modal, not the task under it. Suppression never applies while the focused screen
    /// CapturesRawInput (the key-capture overlay must see raw keys), on unmodeled screens (the
    /// game must stay fully playable), or with focus mode off.
    /// </summary>
    internal static class GameKeySuppression
    {
        // SDL KEYCODES the navigator's vocabulary claims (ui.* bindings + their numpad aliases).
        private static readonly HashSet<int> Keys = new HashSet<int>
        {
            13,          // Return        (ui.activate)
            9,           // Tab           (ui.next/prev)
            8,           // Backspace     (ui.secondary)
            32,          // Space         (ui.tooltip)
            1073741912,  // KP_Enter      (ui.activate)
            1073741903,  // Right
            1073741904,  // Left
            1073741905,  // Down
            1073741906,  // Up
            1073741914,  // KP_2 (down)
            1073741916,  // KP_4 (left)
            1073741918,  // KP_6 (right)
            1073741920,  // KP_8 (up)
            1073741898,  // Home          (ui.home)
            1073741899,  // PageUp        (game task-list jump; suppressed with the rest)
            1073741901,  // End           (ui.end)
            1073741902,  // PageDown
        };

        public static void Apply(Harmony harmony)
        {
            try
            {
                var prefix = new HarmonyMethod(typeof(GameKeySuppression), nameof(KeyPrefix));
                harmony.Patch(Expr.MethodOf(() => GClass64.smethod_17(default(SDL.GEnum195))), prefix: prefix);
                harmony.Patch(Expr.MethodOf(() => GClass64.smethod_22(default(SDL.GEnum195))), prefix: prefix);
                // smethod_10 = the RAW held-key read — the third seam, added for the Redshift
                // pad (GClass313 reads its bindings through it, default START = Enter, so
                // activating a node while the sandbox sim was armed also pressed START; the
                // arcade board reads the same way). Caller-audited 2026-08-29: besides the two
                // pad readers only smethod_11 (modifier keycodes, not in Keys) and the editor's
                // labeled-button helper (F1/Show Goal, not in Keys) ride it — suppressing our
                // key list here touches nothing else. Same gate as the other seams, so the
                // sandbox's free-run PLAY MODE (CapturesRawInput) lifts it and the pad gets
                // every key back.
                harmony.Patch(Expr.MethodOf(() => GClass64.smethod_10(default(SDL.GEnum195))), prefix: prefix);
                Log.Info("[patch] game key suppression armed");
            }
            catch (Exception ex) { Log.Error("[patch] key suppression failed to apply", ex); }
        }

        private static bool Suppressing(int keycode)
        {
            try
            {
                if (!FocusMode.Active) return false;
                var cur = Screens.ScreenManager.Current;
                return cur != null && !cur.CapturesRawInput && !cur.PassKeyToGame(keycode);
            }
            catch { return false; }
        }

        // __0 = the keycode argument (positional injection — the shipping method's parameter
        // names are obfuscated, so never bind by name here).
        /// <summary>A keycode reported JUST PRESSED regardless of the keyboard — set only for the
        /// span of one game call (a prefix sets it, that call's finalizer clears it). The
        /// cutscene one-press advance uses it to run the game's own advance path.</summary>
        public static int ForcedKey;

        private static bool KeyPrefix(SDL.GEnum195 __0, ref bool __result)
        {
            try
            {
                if (ForcedKey != 0 && (int)__0 == ForcedKey)
                {
                    __result = true;
                    return false;
                }
                // Escape is never suppressed — EXCEPT while a mod-side modal is open (the file-
                // values popup): the game acting on Escape there would reset-or-leave the task
                // underneath the popup the user is merely closing.
                if ((int)__0 == 27)
                {
                    var esc = Screens.ScreenManager.Current;
                    if (FocusMode.Active && esc != null && esc.ModalCapturesEscape)
                    {
                        __result = false;
                        return false;
                    }
                    return true;
                }
                // A caret-owning editor (the EXA code editor) keeps EVERY key except Tab — its
                // widget needs arrows/Home/End/Enter/Delete/Backspace, and Tab stays ours so
                // stop-navigation is consistent everywhere (F2 is the step key there).
                if (UI.Navigation.CaretTextEntryFocused)
                {
                    if ((int)__0 == 9) { __result = false; return false; }
                    return true;
                }
                // A focused text field owns Backspace (the append widget's one keycode poll);
                // printable characters ride the TEXTINPUT channel, which is never suppressed.
                if ((int)__0 == 8 && UI.Navigation.TextEntryFocused) return true;
                if (Keys.Contains((int)__0) && Suppressing((int)__0))
                {
                    __result = false;
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}

using System;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;

namespace Echopunks.Screens
{
    /// <summary>
    /// The visual-novel cutscene player (deob GClass255 — the nivas/ghast/isadora scenes; the
    /// desktop pushes it for cutscene tasks). FULLY LINEAR: `vignette_0.list_0` is the script,
    /// `int_0` the current line, no choices anywhere. The game's own keys do everything —
    /// Space/Tab/Enter/click advance (first press completes the typewriter, second moves on),
    /// Escape skips the scene — so this screen is ANNOUNCE-ONLY with CapturesRawInput: it watches
    /// the line index and speaks each line as it appears (full text immediately; the typewriter is
    /// visual pacing). Speaker prefix mirrors the drawn name plate (the character enum name);
    /// Moss lines are the player's silent narration and draw no plate, so none is spoken. Note:
    /// non-Moss lines are VOICE-ACTED — TTS currently reads them anyway (subtitle behavior).
    /// </summary>
    public sealed class CutsceneScreen : Screen
    {
        public override string Key => "cutscene";
        public override string ScreenName => Loc.T("screen.cutscene");
        public override bool CapturesRawInput => true; // the game's advance/skip keys must see everything

        public override bool IsActive() => GameState.TopScreen() is GClass255;

        // GClass255's name is obfuscated live — Deobf bridges via the namemap's T rows.
        private static readonly FieldInfo LineIndexField = Deobf.Field(typeof(GClass255), "int_0");
        private static readonly FieldInfo VignetteField = Deobf.Field(typeof(GClass255), "vignette_0");

        private object _lastInstance;
        private int _lastLine = -1;

        public override void OnUpdate()
        {
            var scene = GameState.TopScreen() as GClass255;
            if (scene == null || LineIndexField == null || VignetteField == null) return;

            int line;
            Vignette vignette;
            try
            {
                line = (int)LineIndexField.GetValue(scene);
                vignette = VignetteField.GetValue(scene) as Vignette;
            }
            catch { return; }
            if (vignette == null || line < 0 || line >= vignette.list_0.Count) return;

            if (ReferenceEquals(scene, _lastInstance) && line == _lastLine) return;
            // Every later line arrives on a press (CutsceneAdvance: one press = the next line),
            // so it cuts the previous one off; the first queues behind the screen name.
            bool first = !ReferenceEquals(scene, _lastInstance);
            _lastInstance = scene;
            _lastLine = line;

            var entry = vignette.list_0[line];
            string text = null;
            try { text = GameText.Speech(entry.list_0[0].ToString().Trim()); }
            catch { }
            if (string.IsNullOrEmpty(text)) return;

            // Moss (the player) narrates plate-less; everyone else gets the drawn name plate.
            string spoken = entry.vignetteCharacter_0 == VignetteCharacter.Moss
                ? text
                : Loc.T("cutscene.line", new { name = entry.vignetteCharacter_0.ToString(), text });
            Speech.Tts.Speak(spoken, interrupt: !first);
        }
    }

    /// <summary>
    /// The EMBER comic cutscene player (Ember2CutsceneScreen, name-preserved) — the animated
    /// scenes with the AI, and the game's only DIALOGUE CHOICES. Each script line carries a LIST
    /// of texts: an Ember2 line speaks the variant selected by the player's previous choice; a
    /// non-Ember line renders its texts as answer bubbles, chosen by mouse OR the game's own
    /// (undocumented) number keys 1-9. Two modes: normally announce-only with CapturesRawInput
    /// (native Space/Tab/Enter advance, Escape skips when the scene allows; Ember lines speak
    /// with her name, single-text player lines bare, the picked option confirms) — but while a
    /// REAL CHOICE is pending, CapturesRawInput drops and the options become a MENU: arrow over
    /// them ("1: ...", "2: ...", re-readable at will, no position counts — user rule,
    /// 2026-08-22), Enter picks by pushing the option's digit as a synthetic SDL key so the
    /// game's own choice path runs byte-identically; the native digits still work in parallel.
    /// Covers the fullscreen story mode too (same state, single texts).
    /// </summary>
    public sealed class EmberCutsceneScreen : Screen
    {
        public EmberCutsceneScreen() { Wrap = true; }

        public override string Key => "cutscene.ember";
        public override string ScreenName => Loc.T("screen.cutscene");

        // The game keeps every key EXCEPT while a choice menu is up — there our navigator owns
        // arrows/Enter (the game ignores its advance keys during a choice anyway; digits pass).
        public override bool CapturesRawInput => !ChoicePending();

        public override bool IsActive() => GameState.TopScreen() is Ember2CutsceneScreen;

        private static readonly FieldInfo PartField = Deobf.Field(typeof(Ember2CutsceneScreen), "int_0");
        private static readonly FieldInfo LineField = Deobf.Field(typeof(Ember2CutsceneScreen), "int_1");
        private static readonly FieldInfo ChoiceField = Deobf.Field(typeof(Ember2CutsceneScreen), "int_2");
        private static readonly FieldInfo ChosenField = Deobf.Field(typeof(Ember2CutsceneScreen), "bool_1");
        private static readonly FieldInfo FullscreenField = Deobf.Field(typeof(Ember2CutsceneScreen), "bool_2");
        private static readonly FieldInfo VignetteField = Deobf.Field(typeof(Ember2CutsceneScreen), "vignette_0");

        // The game routes key events by WINDOW ID (its window map, private on GameLogic) — a
        // synthetic key with the wrong id is silently dropped before the key is even read.
        private static readonly FieldInfo WindowsField = Deobf.Field(typeof(GameLogic), "dictionary_0");

        private static uint MainWindowId()
        {
            try
            {
                var dict = WindowsField?.GetValue(GameLogic.gameLogic_0)
                    as System.Collections.Generic.Dictionary<uint, GClass243>;
                if (dict != null)
                    foreach (var id in dict.Keys) return id; // the game's single window
            }
            catch { }
            return 0;
        }

        private object _instance;
        private int _part = -1, _line = -1;
        private bool _chosen;
        private int _pendingKeyUpSym = -1; // synthetic digit awaiting its key-up
        private int _pendingKeyUpFrames;   // frames to hold it down — the up must land in a LATER
                                           // pump than the down or they cancel before the poll

        // One guarded read of the scene's whole announce-relevant state.
        private static bool TryRead(out Ember2CutsceneScreen s, out int part, out int line,
            out int choice, out bool chosen, out bool fullscreen, out System.Collections.Generic.List<GClass275> list)
        {
            part = line = choice = 0; chosen = fullscreen = false; list = null;
            s = GameState.TopScreen() as Ember2CutsceneScreen;
            if (s == null) return false;
            try
            {
                part = (int)PartField.GetValue(s);
                line = (int)LineField.GetValue(s);
                choice = (int)ChoiceField.GetValue(s);
                chosen = (bool)ChosenField.GetValue(s);
                fullscreen = (bool)FullscreenField.GetValue(s);
                var vignette = VignetteField.GetValue(s) as Vignette;
                list = part == 0 ? vignette?.list_0 : vignette?.list_1;
            }
            catch { return false; }
            return list != null && line >= 0 && line < list.Count;
        }

        /// <summary>True while the current line is a multi-option player choice (the menu state).</summary>
        private static bool ChoicePending()
        {
            Ember2CutsceneScreen s; int part, line, choice; bool chosen, fullscreen;
            System.Collections.Generic.List<GClass275> list;
            if (!TryRead(out s, out part, out line, out choice, out chosen, out fullscreen, out list)) return false;
            var cur = list[line];
            return !chosen && !fullscreen
                && cur.vignetteCharacter_0 != VignetteCharacter.Ember2
                && cur.list_0.Count > 1;
        }

        public override void Build(UI.Graph.GraphBuilder b)
        {
            Ember2CutsceneScreen s; int part, line, choice; bool chosen, fullscreen;
            System.Collections.Generic.List<GClass275> list;
            if (!TryRead(out s, out part, out line, out choice, out chosen, out fullscreen, out list)) return;
            var cur = list[line];
            if (chosen || fullscreen || cur.vignetteCharacter_0 == VignetteCharacter.Ember2
                || cur.list_0.Count <= 1) return; // no menu outside the choice state

            for (int i = 0; i < cur.list_0.Count; i++)
            {
                int n = i;
                var option = cur.list_0[i];
                b.AddItem(UI.Graph.ControlId.Structural("ember.choice." + line + "." + i), new UI.Graph.NodeVtable
                {
                    ControlType = UI.ControlTypes.Text, // bare "1: text" — no role word, no counts
                    SpeaksOwnPosition = true,
                    Announcements = new[]
                    {
                        new UI.Graph.NodeAnnouncement(
                            () => Loc.T("cutscene.choice", new { n = n + 1, text = LineText(option) }),
                            kind: UI.Graph.AnnouncementKinds.Label),
                    },
                    // Enter = push the option's digit as a synthetic SDL key: the game's OWN
                    // choice path runs byte-identically (key-up follows next frame).
                    OnActivate = () =>
                    {
                        if (SdlNative.PushKey(30 + n, 49 + n, down: true, MainWindowId()))
                        {
                            _pendingKeyUpSym = 49 + n;
                            _pendingKeyUpFrames = 2;
                        }
                        else Log.Error("[ember] synthetic choice key was not queued");
                    },
                });
            }
        }

        public override void OnUpdate()
        {
            if (_pendingKeyUpSym >= 0 && --_pendingKeyUpFrames <= 0)
            {
                SdlNative.PushKey(30 + (_pendingKeyUpSym - 49), _pendingKeyUpSym, down: false, MainWindowId());
                _pendingKeyUpSym = -1;
            }

            Ember2CutsceneScreen s; int part, line, choice; bool chosen, fullscreen;
            System.Collections.Generic.List<GClass275> list;
            if (!TryRead(out s, out part, out line, out choice, out chosen, out fullscreen, out list)) return;

            bool same = ReferenceEquals(_instance, s) && part == _part && line == _line && chosen == _chosen;
            // As in the visual novel: a later line came from a press and cuts the last one off.
            _interrupt = ReferenceEquals(_instance, s);
            _instance = s; _part = part; _line = line; _chosen = chosen;
            if (same) return;

            if (chosen)
            {
                // A choice was just made (the game advances the line index immediately): confirm
                // the picked option — the bubble left highlighted on screen.
                var choiceLine = line - 1 >= 0 ? list[line - 1] : null;
                if (choiceLine != null && choiceLine.list_0.Count > 0)
                    Say(LineText(choiceLine.list_0[Math.Min(choice, choiceLine.list_0.Count - 1)]));
                return;
            }

            var cur = list[line];
            if (fullscreen || cur.vignetteCharacter_0 == VignetteCharacter.Ember2)
            {
                // Ember's speech (or a fullscreen story line): the variant her script selected
                // from the player's previous choice.
                string text = LineText(cur.list_0.Count > 0
                    ? cur.list_0[fullscreen ? 0 : Math.Min(choice, cur.list_0.Count - 1)]
                    : null);
                if (string.IsNullOrEmpty(text)) return;
                Say(cur.vignetteCharacter_0 == VignetteCharacter.Ember2
                    ? Loc.T("cutscene.line", new { name = Loc.T("cutscene.ember.name"), text })
                    : text);
            }
            else if (cur.list_0.Count <= 1)
            {
                // The player's single scripted reply — one bubble, no real choice.
                Say(cur.list_0.Count == 1 ? LineText(cur.list_0[0]) : null);
            }
            // Multi-option lines: the choice MENU handles announcement — the graph seats focus on
            // option 1 and speaks it; arrows re-read the rest at will.
        }

        private static string LineText(LocString text)
        {
            if (text == null) return null;
            try { return GameText.Speech(Vignette.smethod_0(text.ToString()).Trim()); }
            catch { return null; }
        }

        private bool _interrupt;

        private void Say(string text)
        {
            if (!string.IsNullOrEmpty(text)) Speech.Tts.Speak(text, interrupt: _interrupt);
        }
    }

}

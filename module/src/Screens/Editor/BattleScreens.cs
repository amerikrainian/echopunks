using System;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.UI;
using Echopunks.UI.Graph;

namespace Echopunks.Screens
{
    /// <summary>The EXODUS Connection Browser (game type OpponentBrowserScreen, name-preserved) —
    /// the battle opponent picker the SELECT OPPONENT map hotspot pushes. Mouse-only in the game
    /// except Escape: rows are "name, Beaten/Not beaten, Changed date" mirroring the drawn
    /// three-column table (the NPC row's lettered badge sprite transcribed after its name), plus
    /// the art-only close X; Enter selects an ENABLED row via the screen's own callback + pop (the
    /// exact click path); Steam rows stay unavailable until the NPC is beaten (the game's dim).
    /// Escape stays native (the game pops itself).</summary>
    public sealed class OpponentBrowserGameScreen : Screen
    {
        public override string Key => "opponent.browser";
        public override string ScreenName => GameText.Speech(GameText.T("EXODUS Connection Browser"));
        public override bool IsActive() => GameState.TopScreen() is OpponentBrowserScreen;

        private static readonly FieldInfo PuzzleField = Deobf.Field(typeof(OpponentBrowserScreen), "puzzle_0");
        private static readonly FieldInfo ActionField = Deobf.Field(typeof(OpponentBrowserScreen), "action_0");
        private static readonly MethodInfo PuzzleDetailsMethod = ResolvePuzzleDetails();

        private static MethodInfo ResolvePuzzleDetails()
        {
            try
            {
                var t = typeof(Puzzle).Assembly.GetType("Puzzles");
                return t != null ? Deobf.Method(t, "smethod_2") : null;
            }
            catch { return null; }
        }

        public override void Build(GraphBuilder b)
        {
            var s = GameState.TopScreen() as OpponentBrowserScreen;
            if (s == null || PuzzleField == null) return;
            var puzzle = (Puzzle)PuzzleField.GetValue(s); // Puzzle is a struct — boxed field read
            MultiplayerOpponentInfo[] infos;
            try { infos = System.Linq.Enumerable.ToArray(GameLogic.gameLogic_0.multiplayerManager_0.method_1(puzzle)); }
            catch { return; }
            // Steam rows unlock once the NPC opponent is beaten (the game's own gate).
            bool npcBeaten = false;
            foreach (var i in infos)
                if (i.bool_0 && !i.maybe_0.method_0()) npcBeaten = true;

            b.PushContext(GameText.T("Opponent"));
            for (int r = 0; r < infos.Length; r++)
            {
                var info = infos[r];
                bool enabled = npcBeaten || !info.maybe_0.method_0();
                b.AddItem(ControlId.Structural("ob.row." + r), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => OpponentName(info, puzzle), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => RowState(info, enabled), kind: AnnouncementKinds.Value),
                    },
                    OnActivate = () => Select(s, info, enabled),
                });
            }
            b.PopContext();
            // The art-only X at the panel's top-right corner (the game's click = a plain pop;
            // Escape does the same natively).
            b.AddItem(ControlId.Structural("ob.close"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T("battle.opponent.close"), kind: AnnouncementKinds.Label),
                },
                OnActivate = () => GameApi.PopScreen(),
            });
        }

        private static string OpponentName(MultiplayerOpponentInfo info, Puzzle puzzle)
        {
            try
            {
                if (info.maybe_0.method_0())
                    return Steamworks.SteamFriends.GetFriendPersonaName(info.maybe_0.method_2());
                var meta = PuzzleDetailsMethod?.Invoke(null, new object[] { puzzle }) as GClass361;
                if (meta == null) return null;
                // The NPC row's name is followed by a lettered "NPC" badge sprite.
                return Loc.T("battle.opponent.npc", new { name = Vignette.dictionary_0[meta.vignetteCharacter_0].Replace("\\", "") });
            }
            catch { return null; }
        }

        private static string RowState(MultiplayerOpponentInfo info, bool enabled)
        {
            try
            {
                string state = Loc.T("battle.opponent.state", new
                {
                    beaten = info.bool_0 ? GameText.T("Beaten") : Loc.T("battle.notbeaten"),
                    changed = GameText.T("Changed"),
                    date = info.dateTime_0 == DateTime.MinValue
                        ? GameText.T("N/A") : info.dateTime_0.ToString("MM-dd-yy"),
                });
                return enabled ? state : state + ", " + Loc.T("value.unavailable");
            }
            catch { return null; }
        }

        private static void Select(OpponentBrowserScreen s, MultiplayerOpponentInfo info, bool enabled)
        {
            if (!enabled)
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"));
                return;
            }
            try
            {
                // The game's click: hand the row to the editor's callback, then pop.
                var action = ActionField?.GetValue(s) as Action<MultiplayerOpponentInfo>;
                if (action == null) return;
                action(info);
                GameApi.PopScreen();
            }
            catch (Exception ex) { Log.Error("[battle] opponent select failed", ex); }
        }
    }

    /// <summary>The battle result screen (game type BattleCompletionScreen, name-preserved) —
    /// pushed after the 100-round sweep. The result heading is the screen name (spoken on
    /// entry); rows mirror the drawn card: the task line, Wins/Draws/Losses, the two player
    /// names, and the upload notice when the game shows it; buttons replicate the click paths
    /// (Continue Editing / Record Solution GIF / Return to Desktop — the last enabled only on a
    /// win, like the drawn button). The rating badge is art-only (a sprite tiered by wins) —
    /// transcription deferred until its lettering is verified on a live win. Escape stays native
    /// (the game's own Continue Editing path).</summary>
    public sealed class BattleCompleteScreen : Screen
    {
        public override string Key => "battle.complete";
        public override bool IsActive() => GameState.TopScreen() is BattleCompletionScreen;

        private static readonly FieldInfo WinsField = Deobf.Field(typeof(BattleCompletionScreen), "int_0");
        private static readonly FieldInfo DrawsField = Deobf.Field(typeof(BattleCompletionScreen), "int_1");
        private static readonly FieldInfo LossesField = Deobf.Field(typeof(BattleCompletionScreen), "int_2");
        private static readonly FieldInfo WonField = Deobf.Field(typeof(BattleCompletionScreen), "bool_0");
        private static readonly FieldInfo LostField = Deobf.Field(typeof(BattleCompletionScreen), "bool_1");
        private static readonly FieldInfo OpponentField = Deobf.Field(typeof(BattleCompletionScreen), "maybe_0");
        private static readonly FieldInfo EditorField = Deobf.Field(typeof(BattleCompletionScreen), "editorScreen_0");
        private static readonly FieldInfo SolutionField = Deobf.Field(typeof(BattleCompletionScreen), "solution_0");
        private static readonly FieldInfo FadeField = Deobf.Field(typeof(BattleCompletionScreen), "maybe_2");
        private static readonly MethodInfo PuzzleDetailsMethod = ResolvePuzzleDetails();

        private static MethodInfo ResolvePuzzleDetails()
        {
            try
            {
                var t = typeof(Puzzle).Assembly.GetType("Puzzles");
                return t != null ? Deobf.Method(t, "smethod_2") : null;
            }
            catch { return null; }
        }

        /// <summary>The puzzle meta (Puzzles.smethod_2 is INTERNAL game-side — reflected).</summary>
        private static GClass361 MetaOf(BattleCompletionScreen s)
        {
            try
            {
                var solution = SolutionField?.GetValue(s) as Solution;
                if (solution == null || PuzzleDetailsMethod == null) return null;
                return PuzzleDetailsMethod.Invoke(null, new object[] { solution.method_0() }) as GClass361;
            }
            catch { return null; }
        }

        public override string ScreenName
        {
            get
            {
                var s = GameState.TopScreen() as BattleCompletionScreen;
                return s == null ? Loc.T("screen.BattleCompletionScreen") : ResultText(s);
            }
        }

        public override void Build(GraphBuilder b)
        {
            var s = GameState.TopScreen() as BattleCompletionScreen;
            if (s == null) return;
            b.PushContext(Loc.T("editor.stats"), positions: false);
            Row(b, "bc.result", () => ResultText(s), () => TitleLine(s));
            Row(b, "bc.players", () => Loc.T("battle.versus", new
            {
                you = SelfName(),
                opponent = OpponentDisplayName(s),
            }), () => null);
            Row(b, "bc.wins", () => GameText.T("Wins"), () => IntField(s, WinsField));
            Row(b, "bc.draws", () => GameText.T("Draws"), () => IntField(s, DrawsField));
            Row(b, "bc.losses", () => GameText.T("Losses"), () => IntField(s, LossesField));
            Row(b, "bc.rating", () => GameText.T("Your Rating"), () => RatingText(s));
            if (UploadNoticeShown(s))
                Row(b, "bc.upload", () => UploadNotice(s), () => null);
            Button(b, "bc.continue", () => GameText.T("Continue Editing"), () => ContinueEditing(s));
            Button(b, "bc.gif", () => GameText.T("Record Solution GIF     ").Trim(), () => RecordGif(s));
            Button(b, "bc.leave", () => GameText.T("Return to Desktop"), () => Leave(s));
            b.PopContext();
        }

        private static string ResultText(BattleCompletionScreen s)
        {
            try
            {
                if (WonField != null && (bool)WonField.GetValue(s)) return GameText.T("Battle Won");
                if (LostField != null && (bool)LostField.GetValue(s)) return GameText.T("Battle Lost");
                return GameText.T("Battle Drawn");
            }
            catch { return null; }
        }

        private static string TitleLine(BattleCompletionScreen s)
        {
            // The drawn "{title} ({subtitle})" line under the heading.
            try
            {
                var meta = MetaOf(s);
                return meta == null ? null
                    : GameText.Speech(meta.locString_0 + " (" + meta.locString_1 + ")");
            }
            catch { return null; }
        }

        private static string SelfName()
        {
            try { return Steamworks.SteamFriends.GetFriendPersonaName(Steamworks.SteamUser.GetSteamID()); }
            catch { return null; }
        }

        private static string OpponentDisplayName(BattleCompletionScreen s)
        {
            try
            {
                var maybe = (Maybe<NetID>)OpponentField.GetValue(s);
                if (maybe.method_0())
                    return Steamworks.SteamFriends.GetFriendPersonaName(maybe.method_2());
                var meta = MetaOf(s);
                return meta == null ? null : Vignette.dictionary_0[meta.vignetteCharacter_0].Replace("\\", "");
            }
            catch { return null; }
        }

        /// <summary>The rating badge is LETTERED SPRITE art tiered by wins — all six tiers
        /// transcribed 2026-08-29 by reading the badge textures back off the GPU
        /// (gclass175_0.texture_0..5 via Renderer.smethod_9): C / B / A / S / S+, and N/A
        /// when the battle wasn't won. The tier picks mirror the draw's exact ternary.
        /// Letters are art transcription (language-invariant), not ui.json.</summary>
        private static string RatingText(BattleCompletionScreen s)
        {
            try
            {
                if (WonField == null || !(bool)WonField.GetValue(s)) return GameText.T("N/A");
                int wins = int.Parse(IntField(s, WinsField));
                if (wins == 100) return "S+";  // texture_5
                if (wins >= 95) return "S";    // texture_4
                if (wins >= 80) return "A";    // texture_0
                if (wins < 60) return "C";     // texture_2
                return "B";                    // texture_1
            }
            catch { return null; }
        }

        private static bool UploadNoticeShown(BattleCompletionScreen s)
        {
            try
            {
                return WonField != null && (bool)WonField.GetValue(s)
                    && GameLogic.gameLogic_0.gclass17_0.gclass52_12.method_0();
            }
            catch { return false; }
        }

        private static string UploadNotice(BattleCompletionScreen s)
        {
            try
            {
                var maybe = (Maybe<NetID>)OpponentField.GetValue(s);
                return maybe.method_0()
                    ? string.Format(GameText.TSpeech("Your solution has been uploaded for {0} to play against."),
                        Steamworks.SteamFriends.GetFriendPersonaName(maybe.method_2()))
                    : GameText.TSpeech("Your solution has been uploaded for new opponents to play against.");
            }
            catch { return null; }
        }

        private static string IntField(BattleCompletionScreen s, FieldInfo f)
        {
            try { return f?.GetValue(s).ToString(); }
            catch { return null; }
        }

        private static void ContinueEditing(BattleCompletionScreen s)
        {
            // The game's button: leave sound, reset the editor, arm the fade that pops.
            try
            {
                var fade = (Maybe<float>)FadeField.GetValue(s);
                if (fade.method_0()) return; // already fading out
                var editor = EditorField?.GetValue(s) as EditorScreen;
                if (editor == null) return;
                try { GClass45.soundsNamespace_0.sound_34.smethod_1(1f); } catch { }
                editor.method_19();
                FadeField.SetValue(s, (Maybe<float>)0f);
            }
            catch (Exception ex) { Log.Error("[battle] continue failed", ex); }
        }

        private static void RecordGif(BattleCompletionScreen s)
        {
            try
            {
                var solution = SolutionField?.GetValue(s) as Solution;
                var editor = EditorField?.GetValue(s) as EditorScreen;
                if (solution == null || editor == null) return;
                var gif = new GifRecorderScreen(solution, editor.int_4);
                try
                {
                    gif.int_1 = int.Parse(IntField(s, WinsField));
                    gif.int_2 = int.Parse(IntField(s, DrawsField));
                    gif.int_3 = int.Parse(IntField(s, LossesField));
                }
                catch { }
                GameApi.PushScreen(gif);
            }
            catch (Exception ex) { Log.Error("[battle] gif failed", ex); }
        }

        private static void Leave(BattleCompletionScreen s)
        {
            try
            {
                // Drawn enabled only on a win — the same gate here.
                if (WonField == null || !(bool)WonField.GetValue(s))
                {
                    Speech.Tts.Speak(Loc.T("value.unavailable"));
                    return;
                }
                var editor = EditorField?.GetValue(s) as EditorScreen;
                if (editor == null) return;
                try { GClass45.soundsNamespace_0.sound_34.smethod_1(1f); } catch { }
                editor.method_19();
                GameApi.PopScreen();
                GameApi.PopScreen();
            }
            catch (Exception ex) { Log.Error("[battle] leave failed", ex); }
        }

        private static void Row(GraphBuilder b, string id, Func<string> label, Func<string> value)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                SpeaksOwnPosition = true, // the completion cards speak no position counts
                Announcements = new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(value, kind: AnnouncementKinds.Value),
                },
            });
        }

        private static void Button(GraphBuilder b, string id, Func<string> label, Action activate)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                SpeaksOwnPosition = true,
                Announcements = new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                },
                OnActivate = activate,
            });
        }
    }
}

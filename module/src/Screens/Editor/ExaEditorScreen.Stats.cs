using System;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.UI;
using Echopunks.UI.Graph;

namespace Echopunks.Screens
{
    public sealed partial class ExaEditorScreen
    {
        // ---- the score readouts, each with the game's own explainer as its tooltip ----

        private void BuildStats(GraphBuilder b, EditorScreen e)
        {
            // SANDBOX (mode 2) draws NO scores panel at all — the Test Run block and the
            // stat columns are both gated to modes 0/1 in the game's draw, and the run
            // number is meaningless there (the sim ignores it). No stop, mirroring the
            // screen; cycle numbers still speak through the step echo.
            if (SandboxMode(e)) return;
            b.BeginStop("stats");
            b.PushContext(Loc.T("editor.stats"), positions: false);

            // The test-run row is ADJUSTABLE (Left/Right, PgUp/PgDn) while editing — the
            // keyboard version of the game's mouse-only arrows, riding the game's own request
            // path (maybe_10, clamped + rebuilt at the next tick) — and Enter opens the game's
            // inline TYPED field (bool_8): the row flips to a text field, digits flow to the
            // game's widget whose parse applies live, Enter or leaving closes. While running
            // the row reads the current run and is inert.
            if (!RunFieldOpen(e))
                b.AddItem(ControlId.Structural("ed.run"), new NodeVtable
                {
                    ControlType = ControlTypes.Slider,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => GameText.T("Test Run"), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(RunText, kind: AnnouncementKinds.Value),
                    },
                    StateText = RunText,
                    OnAdjust = (sign, large) =>
                    {
                        var ed = Editor;
                        if (ed == null || !Editing(ed)) return;
                        try { RunRequestField?.SetValue(ed, (Maybe<int>)(ed.int_4 + sign * (large ? 10 : 1))); }
                        catch { }
                    },
                    OnActivate = OpenRunField,
                    OnTooltip = () => Speech.Tts.Speak(GameText.TSpeech("To complete a task you must complete all 100 test runs. You can change the initial test run to more easily debug problems that only occur on specific test runs.")),
                });
            else
                b.AddItem(ControlId.Structural("ed.run"), new NodeVtable
                {
                    ControlType = ControlTypes.TextField,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => GameText.T("Test Run"), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(RunFieldValue, kind: AnnouncementKinds.Value),
                    },
                    TextEntry = true,
                    TextEchoCaps = false,
                    TextValue = RunFieldValue,
                    OnSelect = () => { }, // Enter on the slider already armed the game's field
                    OnActivate = () => CloseRunField(announce: true),
                });

            // BATTLE (mode 1) swaps the drawn panel: Win Count / Cycles-with-cap / Size /
            // Points / Storage Limit (You + Opponent rows) — no Activity. Each row mirrors the
            // drawn string exactly, with the game's own battle tooltips.
            bool battle = BattleMode(e);

            if (battle)
                // LIVE: the counter ticks once per round during a battle sweep — it speaks
                // only while THIS row is focused (the register-plate pattern; the old global
                // OnUpdate announce narrated all 100 rounds wherever focus was — user rule,
                // 2026-08-29).
                StatRow(b, "ed.wins", () => GameText.T("Win Count"), WinCountText,
                    () => GameText.TSpeech("To win this battle you must win more than half of the test runs."),
                    live: true);

            StatRow(b, "ed.cycles", () => ScoreManager.locString_0.ToString(), () =>
            {
                var ed = Editor;
                var sim = TheSim(ed);
                if (sim == null) return "0";
                // The battle panel draws cycles against the round's fixed duration.
                if (BattleMode(ed)) return sim.method_52() + " / " + sim.int_5;
                return Editing(ed) ? "0" : sim.method_52().ToString();
            }, () => BattleMode(Editor)
                ? GameText.TSpeech("Each test run has a fixed duration, in cycles.")
                : GameText.TSpeech("Your cycles score is the number of turns it takes your EXAs to complete the task."));

            StatRow(b, "ed.size", () => ScoreManager.locString_1.ToString(), () =>
            {
                var ed = Editor;
                if (ed == null) return null;
                try
                {
                    var size = (Maybe<int>)SizeField.GetValue(ed);
                    int limit = Meta(ed)?.int_2 ?? 0;
                    return size.method_0() ? size.method_2() + " / " + limit : GameText.T("N/A");
                }
                catch { return null; }
            }, () =>
            {
                // The game's two size tooltips: battle = a HARD cap; normal = leaderboard
                // eligibility only (speaking the hard-cap wording on normal tasks misled).
                int limit = Meta(Editor)?.int_2 ?? 0;
                return string.Format(GameText.TSpeech(BattleMode(Editor)
                    ? "Your size score is the total number of instructions in all of your EXAs, including MARK pseudo-instructions.\n\nIn this task your size may not exceed {0}."
                    : "Your size score is the total number of instructions in all of your EXAs, including MARK pseudo-instructions.\n\nIn this task your scores will not be eligible for the histograms or leaderboards if your size exceeds {0}."), limit);
            });

            if (battle)
            {
                StatRow(b, "ed.points", () => GameText.T("Points"),
                    () => TeamPair(t => BattlePoints(t)),
                    () => GameText.TSpeech("To win this test run you must score more points than your opponent."));
                StatRow(b, "ed.storage", () => GameText.T("Storage Limit"),
                    () => TeamPair(t =>
                    {
                        var sim = TheSim(Editor);
                        return sim == null ? null : sim.method_76(t) + " / " + sim.int_6;
                    }),
                    () => GameText.TSpeech("There is a limit to the number of EXAs and dropped files that each player can have in the network at a given time."));
            }
            else
                StatRow(b, "ed.activity", () => ScoreManager.locString_2.ToString(), () =>
                {
                    var ed = Editor;
                    var sim = TheSim(ed);
                    return Editing(ed) || sim == null ? "0" : sim.method_53().ToString();
                }, () => GameText.TSpeech("Your activity score is the number of times EXAs you control execute LINK or KILL instructions."));

            b.PopContext();
        }

        // The battle scores' private halves: int_1 = rounds WON, int_0 = rounds completed;
        // the drawn counter adds the round in progress (method_47) and clamps.
        private static readonly FieldInfo RoundsField = Deobf.Field(typeof(EditorScreen), "int_0");
        private static readonly FieldInfo WinsField = Deobf.Field(typeof(EditorScreen), "int_1");

        /// <summary>The drawn Win Count string, exactly: "wins / rounds".</summary>
        private static string WinCountText()
        {
            var ed = Editor;
            var sim = TheSim(ed);
            if (ed == null || sim == null || RoundsField == null || WinsField == null) return null;
            try
            {
                int rounds = (int)RoundsField.GetValue(ed) + (sim.method_47() ? 1 : 0);
                int wins = Math.Min((int)WinsField.GetValue(ed), rounds);
                return wins + " / " + rounds;
            }
            catch { return null; }
        }

        /// <summary>"You X, Opponent Y" — the drawn two-row block as one spoken value.</summary>
        private static string TeamPair(Func<Team, string> value)
        {
            try
            {
                var ed = Editor;
                if (ed == null) return null;
                Team mine = ed.method_24();
                return GameText.T("You") + " " + (value(mine) ?? "0") + ", "
                    + GameText.T("Opponent") + " " + (value(mine.smethod_0()) ?? "0");
            }
            catch { return null; }
        }

        private static string BattlePoints(Team team)
        {
            try
            {
                var sim = TheSim(Editor);
                var logic = sim?.method_43();
                int points;
                return logic != null && logic.dictionary_0.TryGetValue(team, out points)
                    ? points.ToString() : "0";
            }
            catch { return "0"; }
        }

        // The game's typed test-run field: bool_8 = the open flag, string_0 = its text buffer,
        // maybe_10 = the pending run request the tick clamps and applies (method_25 + method_45).
        private static readonly FieldInfo RunFieldFlag = Deobf.Field(typeof(EditorScreen), "bool_8");
        private static readonly FieldInfo RunFieldText = Deobf.Field(typeof(EditorScreen), "string_0");
        private static readonly FieldInfo RunRequestField = Deobf.Field(typeof(EditorScreen), "maybe_10");

        private static bool RunFieldOpen(EditorScreen e)
        {
            try { return e != null && RunFieldFlag != null && (bool)RunFieldFlag.GetValue(e); }
            catch { return false; }
        }

        private static string RunFieldValue()
        {
            try { return (string)RunFieldText.GetValue(Editor); }
            catch { return null; }
        }

        private static void OpenRunField()
        {
            var e = Editor;
            if (e == null || !Editing(e) || RunFieldOpen(e)) return;
            try
            {
                // The game's click on the number, exactly (close fields, flag, blink, clear).
                Invoke(CloseFieldsMethod, e);
                RunFieldFlag.SetValue(e, true);
                GClass288.smethod_1();
                RunFieldText.SetValue(e, string.Empty);
                Speech.Tts.Speak(Loc.T("editor.run.type"), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[editor] run field open failed", ex); }
        }

        /// <summary>The typed parse already applied itself (the game re-parses into maybe_10 as
        /// the text changes) — closing is just the flag, like Enter or a click-away.</summary>
        private static void CloseRunField(bool announce)
        {
            var e = Editor;
            if (e == null || !RunFieldOpen(e)) return;
            try
            {
                RunFieldFlag.SetValue(e, false);
                if (announce) Speech.Tts.Speak(RunText(), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[editor] run field close failed", ex); }
        }

        private static string RunText()
        {
            var ed = Editor;
            if (ed == null) return null;
            int run = ed.int_4;
            if (!Editing(ed))
                try { run = (int)RunNumberMethod.Invoke(ed, null); } catch { }
            else
                // A request the tick hasn't applied yet (our adjust, or the typed field's live
                // parse) IS the value to speak — mirror the clamp it will get.
                try
                {
                    var pending = (Maybe<int>)RunRequestField.GetValue(ed);
                    if (pending.method_0())
                        run = Math.Max(0, Math.Min(GClass68.int_0 - 1, pending.method_2()));
                }
                catch { }
            return (run + 1) + " / 100";
        }

        private static void StatRow(GraphBuilder b, string id, Func<string> label, Func<string> value, Func<string> tooltip, bool live = false)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(value, live: live, kind: AnnouncementKinds.Value),
                },
                OnTooltip = () =>
                {
                    try { Speech.Tts.Speak(tooltip()); } catch { }
                },
            });
        }

        // ---- the solution header ----

        private void BuildSolution(GraphBuilder b, EditorScreen e)
        {
            b.BeginStop("solution");
            b.PushContext(Loc.T("editor.solution"), positions: false);
            // The solution name is EDITABLE, typing-first: landing arms the game's own inline
            // field (append-style; typing/Backspace flow, echoed); Enter or leaving commits with
            // the game's undo snapshot.
            b.AddItem(ControlId.Structural("ed.solname"), new NodeVtable
            {
                ControlType = ControlTypes.TextField,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T("editor.solution.name"), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() =>
                    {
                        try { return Editor?.solution_0.string_0; }
                        catch { return null; }
                    }, kind: AnnouncementKinds.Value),
                },
                TextEntry = true,
                TextEchoCaps = false, // the game font uppercases inserts
                TextValue = () =>
                {
                    try { return Editor?.solution_0.string_0; }
                    catch { return null; }
                },
                OnSelect = () => ArmName(Editor),
                OnActivate = () => CommitName(Editor),
            });
            // The folder button right of the name (art-only, every mode): the game names it
            // nowhere, so it speaks the drawn title of the screen it opens. Dimmed and dead
            // while armed like the drawn one (the game's Ctrl+O stays native either way).
            b.AddItem(ControlId.Structural("ed.browser"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T("EXODUS Solution Browser"), kind: AnnouncementKinds.Label),
                    ArmedUnavailable(),
                },
                OnActivate = OpenSolutionBrowser,
            });
            b.AddItem(ControlId.Structural("ed.newexa"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T("Create New EXA"), kind: AnnouncementKinds.Label),
                },
                OnActivate = CreateExa,
            });
            b.AddItem(ControlId.Structural("ed.exacount"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T("editor.exas"), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(ExaCountText, kind: AnnouncementKinds.Value),
                },
            });
            // The battle map's SELECT OPPONENT hotspot (drawn on the opponent's home host,
            // mouse-only): the same push the game's click makes — OpponentBrowserScreen with
            // the editor's own selection callback.
            if (BattleMode(e))
                b.AddItem(ControlId.Structural("ed.opponent"), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => GameText.T("SELECT OPPONENT"), kind: AnnouncementKinds.Label),
                        ArmedUnavailable(),
                    },
                    OnActivate = OpenOpponentBrowser,
                });
            b.PopContext();
        }

        // The game's selection callback (EditorScreen.method_44 — sets the opponent info the
        // home-plate draw and the battle sim read) is private; the browser takes it as a delegate.
        private static readonly MethodInfo SelectOpponentCallback = Deobf.Method(typeof(EditorScreen), "method_44");

        // Both header buttons draw dimmed and ignore clicks while the sim is armed (method_2).
        private static NodeAnnouncement ArmedUnavailable() =>
            new NodeAnnouncement(() => Editing(Editor) ? null : Loc.T("value.unavailable"),
                kind: AnnouncementKinds.Enabled);

        private static bool RefuseWhileArmed(EditorScreen e)
        {
            if (Editing(e)) return false;
            Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
            return true;
        }

        private static void OpenSolutionBrowser()
        {
            var e = Editor;
            if (e == null || RefuseWhileArmed(e)) return;
            try
            {
                GClass45.soundsNamespace_0.sound_41.smethod_1(1f);
                GameApi.PushScreen(new GClass253(e.solution_0));
            }
            catch (Exception ex) { Log.Error("[editor] solution browser open failed", ex); }
        }

        private static void OpenOpponentBrowser()
        {
            var e = Editor;
            if (e == null || SelectOpponentCallback == null || RefuseWhileArmed(e)) return;
            try
            {
                var cb = (Action<MultiplayerOpponentInfo>)Delegate.CreateDelegate(
                    typeof(Action<MultiplayerOpponentInfo>), e, SelectOpponentCallback);
                GameApi.PushScreen(new OpponentBrowserScreen(e.solution_0.method_0(), cb));
            }
            catch (Exception ex) { Log.Error("[editor] opponent browser open failed", ex); }
        }
    }
}

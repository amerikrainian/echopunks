using System;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.UI;
using Echopunks.UI.Graph;

namespace Echopunks.Screens
{
    /// <summary>The EXODUS Solution Browser (deob GClass253, obfuscated live) — the editor's
    /// Ctrl+O / folder-button screen, pushed over the editor. Natively: mouse rows with
    /// double-click open and a per-row context menu (GClass26: Open/Copy/Delete, + Export in
    /// sandbox), plus arrows/Enter/Escape the game already reads — which our suppression now
    /// owns, replaced by selection-follows-focus rows (the game's own public select,
    /// method_0, with its arrow sound), Enter = open (method_2 — the double-click path,
    /// byte-identical: close, music, push EditorScreen), Backspace = delete (method_5, NO
    /// confirm — faithful to the game's own menu item). The context-menu verbs act on the
    /// SELECTED solution from an actions stop under the game's own menu labels. SANDBOX adds
    /// Export (method_3 — the disc-PNG writer, which saves to the desktop with zero visual
    /// feedback; the filename it derives is announced) and the two drawn help panels
    /// (export/import instructions — the import itself is native OS drag-and-drop, armed by
    /// the game while this screen is open). Import/mouse selection changes FOLLOW into focus
    /// (the game selects the imported disc's solution itself). Escape stays native — and
    /// mirrors the game's gate: dead while the editor's open solution no longer exists (the
    /// drawn back button dims), which the Back row makes audible.</summary>
    public sealed class SolutionBrowserGameScreen : Screen
    {
        public SolutionBrowserGameScreen() { Wrap = true; }

        public override string Key => "solution.browser";
        public override string ScreenName => GameText.Speech(GameText.T("EXODUS Solution Browser"));
        public override bool IsActive() => GameState.TopScreen() is GClass253;
        public override object InitialFocusStop => "solutions";

        // The browser's private half: maybe_0 = the selected solution, puzzle_0/solution_0 =
        // what the editor opened it with, bool_0/1/2 = sandbox/battle/legacy layout flags,
        // locString_0/1 = the sandbox help texts; method_1..6 = create/open/export/copy/
        // delete/close — the exact handlers the drawn controls invoke.
        private static readonly FieldInfo SelectedField = Deobf.Field(typeof(GClass253), "maybe_0");
        private static readonly FieldInfo PuzzleField = Deobf.Field(typeof(GClass253), "puzzle_0");
        private static readonly FieldInfo EditorSolutionField = Deobf.Field(typeof(GClass253), "solution_0");
        private static readonly FieldInfo SandboxField = Deobf.Field(typeof(GClass253), "bool_0");
        private static readonly FieldInfo BattleField = Deobf.Field(typeof(GClass253), "bool_1");
        private static readonly FieldInfo LegacyField = Deobf.Field(typeof(GClass253), "bool_2");
        private static readonly FieldInfo ExportHelpField = Deobf.Field(typeof(GClass253), "locString_0");
        private static readonly FieldInfo ImportHelpField = Deobf.Field(typeof(GClass253), "locString_1");
        private static readonly MethodInfo CreateMethod = Deobf.Method(typeof(GClass253), "method_1");
        private static readonly MethodInfo OpenMethod = Deobf.Method(typeof(GClass253), "method_2");
        private static readonly MethodInfo ExportMethod = Deobf.Method(typeof(GClass253), "method_3");
        private static readonly MethodInfo CopyMethod = Deobf.Method(typeof(GClass253), "method_4");
        private static readonly MethodInfo DeleteMethod = Deobf.Method(typeof(GClass253), "method_5");
        private static readonly MethodInfo CloseMethod = Deobf.Method(typeof(GClass253), "method_6");

        private static GClass253 Browser => GameState.TopScreen() as GClass253;

        private static bool Flag(FieldInfo f, GClass253 s)
        {
            try { return f != null && (bool)f.GetValue(s); }
            catch { return false; }
        }

        private static Solution SelectedSolution(GClass253 s)
        {
            try
            {
                var sel = (Maybe<Solution>)SelectedField.GetValue(s);
                return sel.method_0() ? sel.method_2() : null;
            }
            catch { return null; }
        }

        private static Solution[] Solutions(GClass253 s)
        {
            try { return SolutionManager.smethod_3((Puzzle)PuzzleField.GetValue(s)); }
            catch { return new Solution[0]; }
        }

        private static ControlId RowId(Solution sol)
        {
            string disk = "?";
            try { disk = sol.solutionNameOnDisk_0.ToString(); } catch { }
            return ControlId.Structural("sb.row." + disk);
        }

        public override void Build(GraphBuilder b)
        {
            var s = Browser;
            if (s == null || SelectedField == null) return;
            bool sandbox = Flag(SandboxField, s), battle = Flag(BattleField, s);

            b.BeginStop("solutions");
            b.PushContext(Loc.T("browser.solutions"));
            foreach (var sol in Solutions(s))
            {
                var row = sol;
                b.AddItem(RowId(row), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => row.string_0, kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => RowStats(row), kind: AnnouncementKinds.Value),
                    },
                    // Selection follows focus (engine-only Selected — never spoken, drives
                    // stop landings onto the selected row, the drawn highlight).
                    Selected = () => ReferenceEquals(SelectedSolution(Browser), row),
                    OnSelect = () => SelectRow(row),
                    OnActivate = () => OpenRow(row),
                    OnSecondary = () => DeleteRow(row),
                });
            }
            b.PopContext();

            b.BeginStop("actions");
            b.PushContext(Loc.T("browser.actions"), positions: false);
            ActionButton(b, "sb.create", () => GameText.T("Create New Solution"), null, CreateSolution);
            // The context menu's verbs, acting on the SELECTED solution (its name reads as
            // the value so the target is always audible). Open is the rows' Enter.
            ActionButton(b, "sb.copy", () => GameText.T("Copy"),
                () => SelectedSolution(Browser)?.string_0, CopySelected);
            if (sandbox)
                ActionButton(b, "sb.export", () => GameText.T("Export"),
                    () => SelectedSolution(Browser)?.string_0, ExportSelected);
            // The drawn back button (art-only) dims when the editor's open solution was
            // deleted — Escape then goes dead too (the game's own gate); this row says why.
            b.AddItem(ControlId.Structural("sb.back"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T("browser.back"), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => BackEnabled(Browser) ? null : Loc.T("value.unavailable"),
                        kind: AnnouncementKinds.Enabled),
                },
                OnActivate = CloseBrowser,
            });
            b.PopContext();

            if (sandbox)
            {
                // The two drawn help panels: header + the game's own instruction text.
                b.BeginStop("help");
                b.PushContext(Loc.T("browser.help"), positions: false);
                HelpRow(b, "sb.help.export", "Exporting Redshift Solutions", ExportHelpField);
                HelpRow(b, "sb.help.import", "Importing Redshift Solutions", ImportHelpField);
                b.PopContext();
            }
            else if (!battle && !Flag(LegacyField, s) && SelectedSolution(s) != null)
            {
                // The right-half panel block — the game draws it only on normal puzzles
                // with a solution SELECTED (the ctor selects the editor's solution, so it
                // is empty only right after a delete).
                if (!PuzzleSolved(s))
                {
                    // The drawn replacement notice.
                    b.BeginStop("notice");
                    b.AddItem(ControlId.Structural("sb.notice"), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(
                                () => GameText.TSpeech("Solve this puzzle to view histograms and leaderboards."),
                                kind: AnnouncementKinds.Label),
                        },
                    });
                }
                else
                {
                    // The three histogram panels for the SELECTED solution — the browser
                    // recomputes dictionary_0 (scoreManager method_14) on every selection
                    // change, so arrowing the rows re-reads the panels for that solution.
                    // Browser layout: no caption (the row already spoke the scores); the
                    // marker = the SELECTED solution's own score (maybe_2 — the GEnum216 1
                    // its Theme call selects), suppressed when the solution is over the
                    // size limit exactly like the drawn arrow (the panel itself still
                    // shows).
                    bool eligible = SelectionEligible(s);
                    LeaderboardRows.Build(b, s.dictionary_0.Values, caption: false,
                        g => eligible ? g.maybe_2 : (Maybe<int>)GStruct10.gstruct10_0);
                }
            }
        }

        private static void ActionButton(GraphBuilder b, string id, Func<string> label,
            Func<string> value, Action activate)
        {
            var anns = value == null
                ? new[] { new NodeAnnouncement(label, kind: AnnouncementKinds.Label) }
                : new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(value, kind: AnnouncementKinds.Value),
                };
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = anns,
                OnActivate = activate,
            });
        }

        private static void HelpRow(GraphBuilder b, string id, string headerKey, FieldInfo body)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T(headerKey), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() =>
                    {
                        try { return GameText.Speech((body.GetValue(null) as LocString).ToString()); }
                        catch { return null; }
                    }, kind: AnnouncementKinds.Value),
                },
            });
        }

        /// <summary>The drawn row stats, per mode: sandbox = Size (the stored solution.int_1)
        /// + EXA count; battle = wins + count; normal = Cycles/Size/Activity once all three
        /// scores exist, else Unsolved + count. Count via the game's own "{0} EXAS"/"1 EXA".</summary>
        private static string RowStats(Solution sol)
        {
            try
            {
                var s = Browser;
                if (s == null) return null;
                int count = sol.list_0.Count;
                string exas = count == 1
                    ? GameText.T("1 EXA")
                    : string.Format(GameText.T("{0} EXAS"), count);
                if (Flag(SandboxField, s))
                    return ScoreManager.locString_1.ToString() + " " + sol.int_1 + ", " + exas;
                if (Flag(BattleField, s))
                    return GameText.T("WINS") + " " + sol.int_0 + ", " + exas;
                int cycles, size, activity;
                if (sol.dictionary_0.TryGetValue(GEnum178.Cycles, out cycles)
                    && sol.dictionary_0.TryGetValue(GEnum178.Size, out size)
                    && sol.dictionary_0.TryGetValue(GEnum178.Activity, out activity))
                    return ScoreManager.locString_0.ToString() + " " + cycles + ", "
                        + ScoreManager.locString_1.ToString() + " " + size + ", "
                        + ScoreManager.locString_2.ToString() + " " + activity;
                return GameText.T("Unsolved") + ", " + exas;
            }
            catch { return null; }
        }

        private static bool PuzzleSolved(GClass253 s)
        {
            try
            {
                return GameLogic.gameLogic_0.saveData_0
                    .method_6((Puzzle)PuzzleField.GetValue(s), bool_0: false);
            }
            catch { return false; }
        }

        private static readonly MethodInfo PuzzleMetaMethod =
            Deobf.Method(typeof(Puzzle).Assembly.GetType("Puzzles"), "smethod_2");

        /// <summary>The game's marker gate (its flag7): the selected solution's Size score —
        /// int.MaxValue when unscored — within the puzzle's size limit.</summary>
        private static bool SelectionEligible(GClass253 s)
        {
            try
            {
                var sel = SelectedSolution(s);
                if (sel == null) return false;
                int size;
                if (!sel.dictionary_0.TryGetValue(GEnum178.Size, out size)) size = int.MaxValue;
                var meta = PuzzleMetaMethod?.Invoke(null,
                    new object[] { (Puzzle)PuzzleField.GetValue(s) }) as GClass361;
                return meta == null || size <= meta.int_2;
            }
            catch { return true; }
        }

        private static bool BackEnabled(GClass253 s)
        {
            try
            {
                var current = EditorSolutionField.GetValue(s) as Solution;
                foreach (var sol in Solutions(s))
                    if (ReferenceEquals(sol, current)) return true;
            }
            catch { }
            return false;
        }

        // ---- the game's own handlers ----

        private static void SelectRow(Solution sol)
        {
            var s = Browser;
            if (s == null) return;
            try
            {
                // The game's arrow/click path: the sound only on an actual change.
                if (!ReferenceEquals(SelectedSolution(s), sol))
                    try { GClass45.soundsNamespace_0.sound_43.smethod_1(1f); } catch { }
                s.method_0(sol);
            }
            catch (Exception ex) { Log.Error("[browser] select failed", ex); }
        }

        private static void OpenRow(Solution sol)
        {
            var s = Browser;
            if (s == null) return;
            try
            {
                s.method_0(sol);
                OpenMethod.Invoke(s, null); // close + music + push EditorScreen(selected)
            }
            catch (Exception ex) { Log.Error("[browser] open failed", ex); }
        }

        private void DeleteRow(Solution sol)
        {
            var s = Browser;
            if (s == null) return;
            try
            {
                string name = sol.string_0;
                s.method_0(sol);
                DeleteMethod.Invoke(s, null); // the menu's Delete: immediate, no confirm
                _lastSelected = null;
                Speech.Tts.Speak(Loc.T("browser.deleted", new { name }), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[browser] delete failed", ex); }
        }

        private void CreateSolution()
        {
            var s = Browser;
            if (s == null) return;
            try
            {
                CreateMethod.Invoke(s, null); // create + select + scroll to top
                FocusSelected(s);             // the row announce is the feedback
            }
            catch (Exception ex) { Log.Error("[browser] create failed", ex); }
        }

        private void CopySelected()
        {
            var s = Browser;
            if (s == null) return;
            if (SelectedSolution(s) == null)
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                return;
            }
            try
            {
                CopyMethod.Invoke(s, null); // duplicate + select the copy
                FocusSelected(s);
            }
            catch (Exception ex) { Log.Error("[browser] copy failed", ex); }
        }

        private static void ExportSelected()
        {
            var s = Browser;
            var sol = s != null ? SelectedSolution(s) : null;
            if (sol == null)
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                return;
            }
            try
            {
                ExportMethod.Invoke(s, null);
                // The game writes the disc PNG to the desktop with no visual feedback at
                // all — announce the filename it derives (name + ".png", invalid chars
                // stripped), so the file is findable.
                string file = sol.string_0 + ".png";
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                    file = file.Replace(c.ToString(), string.Empty);
                Speech.Tts.Speak(Loc.T("browser.exported", new { file }), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[browser] export failed", ex); }
        }

        private static void CloseBrowser()
        {
            var s = Browser;
            if (s == null) return;
            if (!BackEnabled(s))
            {
                // The game's gate: the editor's solution is gone — open or create one first.
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                return;
            }
            try { CloseMethod.Invoke(s, null); }
            catch (Exception ex) { Log.Error("[browser] close failed", ex); }
        }

        private void FocusSelected(GClass253 s)
        {
            var sel = SelectedSolution(s);
            _lastSelected = sel;
            if (sel != null) Navigation.FocusNode(RowId(sel));
        }

        // ---- selection changes we didn't drive (a disc-image IMPORT selects the imported
        // solution game-side; so does a mouse click on a row) follow into focus, so the
        // otherwise-silent import lands audibly on its new row. Guarded to the solutions
        // stop so a mouse click never yanks focus out of the actions rows. ----

        private object _instance;
        private Solution _lastSelected;

        public override void OnUpdate()
        {
            var s = Browser;
            if (s == null) return;
            if (!ReferenceEquals(_instance, s))
            {
                _instance = s;
                _lastSelected = SelectedSolution(s);
                return;
            }
            var sel = SelectedSolution(s);
            if (ReferenceEquals(sel, _lastSelected)) return;
            _lastSelected = sel;
            if (sel == null || !"solutions".Equals(Navigation.FocusedStopKey)) return;
            var id = RowId(sel);
            var nav = Navigation.Active as GraphNavigator;
            if (nav != null && !id.Equals(nav.FocusedNodeId))
                Navigation.FocusNode(id);
        }
    }
}

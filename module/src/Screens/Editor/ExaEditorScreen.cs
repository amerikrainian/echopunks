using System;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.UI;
using Echopunks.UI.Graph;

namespace Echopunks.Screens
{
    /// <summary>
    /// The EXA editor (game type EditorScreen, name-preserved) — the puzzle-solving screen and the
    /// mod's long game (see "EXA editor decoded" in CLAUDE.md). TASK 1 of the build-out: the
    /// read-only browse skeleton — three Tab stops over the fully-text parts of the screen: the
    /// task panel (puzzle description + live goal checklist), the score readouts (test run,
    /// cycles/size/activity — each carrying the game's own explainer tooltip on Space), and the
    /// solution header. Values read live at announce time; nothing is marked live-watched (cycles
    /// change 50×/second while running — announce-on-focus only). Escape stays native (the game's
    /// own leave-screen/reset behavior).
    /// </summary>
    public sealed partial class ExaEditorScreen : Screen
    {
        public ExaEditorScreen() { Wrap = true; }

        public override string Key => "editor";
        public override bool IsActive() => GameState.TopScreen() is EditorScreen;
        public override object InitialFocusStop => "task";

        public override string ScreenName
        {
            get
            {
                // The puzzle's own title; the generic label if anything is missing.
                try
                {
                    string title = GameText.Speech(Meta(Editor)?.locString_0.ToString());
                    if (!string.IsNullOrEmpty(title)) return title;
                }
                catch { }
                return Loc.T("screen.EditorScreen");
            }
        }

        // The private half (EditorScreen is name-preserved — direct namemap hits):
        // sim_0 = the live sim (rebuilt every frame while editing), maybe_1 = the size score,
        // method_23 = the effective test-run number while running.
        private static readonly FieldInfo SimField = Deobf.Field(typeof(EditorScreen), "sim_0");
        private static readonly FieldInfo SizeField = Deobf.Field(typeof(EditorScreen), "maybe_1");
        private static readonly MethodInfo RunNumberMethod = Deobf.Method(typeof(EditorScreen), "method_23");

        // The sim-control seams (decompile-verified against the button handlers):
        // method_18(fast, Maybe<int> stepCount) = start/advance, method_19 = reset, method_20 =
        // pause, method_22 = advance to the next test run once solved, method_57 = the common
        // pre-action (dismisses inline fields), bool_12 = the show-compile-errors state,
        // hashSet_0 = pending animations (gates the test-run advance).
        private static readonly MethodInfo AdvanceMethod = Deobf.Method(typeof(EditorScreen), "method_18");
        private static readonly MethodInfo ResetMethod = Deobf.Method(typeof(EditorScreen), "method_19");
        private static readonly MethodInfo PauseMethod = Deobf.Method(typeof(EditorScreen), "method_20");
        private static readonly MethodInfo NextRunMethod = Deobf.Method(typeof(EditorScreen), "method_22");
        private static readonly MethodInfo PreActionMethod = Deobf.Method(typeof(EditorScreen), "method_57");
        private static readonly FieldInfo ErrorViewField = Deobf.Field(typeof(EditorScreen), "bool_12");
        private static readonly FieldInfo AnimationsField = Deobf.Field(typeof(EditorScreen), "hashSet_0");

        // Edit-side seams (task 7): method_32 = mark undo-dirty, method_33 = undo snapshot,
        // method_25 = rebuild the sim now, method_54 = close inline fields, bool_7 = the
        // solution-name field's active flag.
        private static readonly MethodInfo DirtyMethod = Deobf.Method(typeof(EditorScreen), "method_32");
        private static readonly MethodInfo SnapshotMethod = Deobf.Method(typeof(EditorScreen), "method_33");
        private static readonly MethodInfo RebuildMethod = Deobf.Method(typeof(EditorScreen), "method_25");
        private static readonly MethodInfo CloseFieldsMethod = Deobf.Method(typeof(EditorScreen), "method_54");
        private static readonly FieldInfo NameActiveField = Deobf.Field(typeof(EditorScreen), "bool_7");

        // maybe_0 = the run's STEP BUDGET: method_18 stores it (stepping passes Some(n), a free
        // run None), method_20 pauses by setting Some(0). So "free-running" = armed AND no budget.
        private static readonly FieldInfo StepBudgetField = Deobf.Field(typeof(EditorScreen), "maybe_0");

        // maybe_13 = the pending run-to-instruction marker (the game's Alt+Click feature): while
        // set, the frame draw auto-starts a run that pauses when the target line is reached; any
        // sim button (method_57) or a compile error cancels it SILENTLY.
        private static readonly FieldInfo RunToMarkerField = Deobf.Field(typeof(EditorScreen), "maybe_13");
        private static readonly object EmptyRunToMarker =
            RunToMarkerField != null ? Activator.CreateInstance(RunToMarkerField.FieldType) : null;

        private static Maybe<int> StepBudget(EditorScreen e)
        {
            try { return (Maybe<int>)StepBudgetField.GetValue(e); }
            catch { return (Maybe<int>)0; } // a budget — never mistaken for a free run
        }

        private static EditorScreen Editor => GameState.TopScreen() as EditorScreen;

        private static Sim TheSim(EditorScreen e)
        {
            try { return SimField?.GetValue(e) as Sim; }
            catch { return null; }
        }

        // Puzzle → metadata is an extension on the INTERNAL static class Puzzles — invisible to
        // the compiler, reached by reflection (name-preserved class, so Deobf resolves directly).
        private static readonly MethodInfo PuzzleMetaMethod =
            Deobf.Method(typeof(Puzzle).Assembly.GetType("Puzzles"), "smethod_2");

        /// <summary>The puzzle's metadata blob (title, description, size limit).</summary>
        private static GClass361 Meta(EditorScreen e)
        {
            try
            {
                if (e == null || PuzzleMetaMethod == null) return null;
                return PuzzleMetaMethod.Invoke(null, new object[] { e.solution_0.method_0() }) as GClass361;
            }
            catch { return null; }
        }

        private static bool Editing(EditorScreen e)
        {
            try { return e != null && e.method_2(); }
            catch { return true; }
        }

        /// <summary>meta.genum18_0: 0 = normal task, 1 = BATTLE, 2 = sandbox. Battles swap the
        /// whole scores panel (Win Count / Points / Storage Limit), drop the goal checklist and
        /// the Show Goal button, and add the SELECT OPPONENT map hotspot.</summary>
        private static int GameMode(EditorScreen e)
        {
            try
            {
                var m = Meta(e);
                return m == null ? 0 : (int)m.genum18_0;
            }
            catch { return 0; }
        }

        private static bool BattleMode(EditorScreen e) => GameMode(e) == 1;

        /// <summary>The Redshift sandbox (PB039): no tests/goals/scores, the map narrows,
        /// the right half is the drawn handheld console, EXAs are sprites.</summary>
        private static bool SandboxMode(EditorScreen e) => GameMode(e) == 2;

        /// <summary>PLAY MODE (sandbox only, no key of its own — user design 2026-08-29):
        /// while the homebrew game FREE-RUNS, every key belongs to the player — the Redshift
        /// pad reads raw held keys (GClass64.smethod_10, default W/S/A/D + J/K/L + Enter for
        /// START), so the navigator and both suppression seams stand down via
        /// CapturesRawInput, exactly like a cutscene. Free-running = armed with NO step
        /// budget (F4/F5, run-to's hunt); any pause — native F3, Tab-step, Escape-reset —
        /// brings the navigator straight back. The game's own text-input gate already kills
        /// pad reads while one of its fields is armed, and its run pre-action closes them,
        /// so entering a run never types into a field.</summary>
        public override bool CapturesRawInput => PlayMode;

        private bool PlayMode
        {
            get
            {
                try
                {
                    var e = Editor;
                    return e != null && SandboxMode(e) && e.method_0() && !StepBudget(e).method_0();
                }
                catch { return false; }
            }
        }

        public override void Build(GraphBuilder b)
        {
            var e = Editor;
            if (e == null) return;
            // The file-values popup is MODAL while open: it is the whole graph (Enter or
            // Backspace closes it, back to the file row).
            if (FilePopupOpen)
            {
                if (BuildFilePopup(b)) return;
                _popupFile = null; // the file vanished — fall through to the normal graph
                _popupGoalHost = _popupGoalRequired = -1;
            }
            if (_goalPopup)
            {
                if (BuildGoalPopup(b)) return;
                _goalPopup = false; // content vanished — fall through to the normal graph
                Patches.PanelCapture.SetArmed(false, false);
            }
            // Stop order (user preference): task -> windows -> hosts -> grid -> links -> files -> code…
            BuildTask(b, e);
            BuildWindows(b, e);
            BuildHosts(b, e);
            BuildGrid(b, e);
            BuildLinks(b, e);
            BuildFiles(b, e);
            BuildRegisters(b, e);
            BuildSign(b, e);
            BuildProblems(b, e);
            BuildCode(b, e);
            BuildSprite(b, e);
            BuildScreenStop(b, e);
            BuildStats(b, e);
            BuildControls(b, e);
            BuildTestLog(b, e);
            BuildExecutionLog(b, e);
            BuildSolution(b, e);
        }
    }
}

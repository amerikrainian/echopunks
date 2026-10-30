using System;
using System.Collections.Generic;
using Echopunks.Game;
using Echopunks.Localization;
using HarmonyLib;

namespace Echopunks.Patches
{
    /// <summary>
    /// Captures sim events the model destroys too fast to poll: an errored EXA keeps its message
    /// for roughly ONE cycle before the sim deletes it (and at fast-forward the whole run passes
    /// in a blink). A postfix on the error seam — Sim.smethod_16(SimExa, isError, message), the
    /// single chokepoint every runtime error and kill goes through — buffers player-EXA errors as
    /// composed announcements tagged with the test run they landed on; the editor screen drains
    /// the queue each frame into its TEST LOG (and speaks them live only while stepping). The
    /// typed line number is recovered by inverting the compiler's written→expanded line map
    /// (identity for macro-free code).
    /// </summary>
    internal static class SimNarration
    {
        /// <summary>One buffered sim event: the composed message and the 0-based test run it
        /// happened on (-1 when unknown).</summary>
        public struct SimEvent
        {
            public int Test;
            public string Message;
        }

        private static readonly Queue<SimEvent> Events = new Queue<SimEvent>();
        private static readonly object Gate = new object();
        // A fast-forwarded free run can raise hundreds of routine errors in ONE frame (eight
        // search copies dying per test, times the tests swept that frame) — they all belong
        // in the log, so the cap only guards against a runaway.
        private const int QueueCap = 512;

        // ---- which test run the current run STARTED on (0-based; -1 = none). A passing run
        // AUTO-ADVANCES through the validation tests, so an event can land on a DIFFERENT
        // random layout than the one the user was looking at — spoken, it then names its
        // test ("Test 2: …"), and only then: failing the test you started on stays
        // unprefixed (user rule, 2026-08-23). The editor's run-state watch marks start/stop. ----
        private static readonly System.Reflection.MethodInfo RunNumber =
            Deobf.Method(typeof(EditorScreen), "method_23"); // the ACTIVE run while armed
        private static int _runStartTest = -1;

        public static void MarkRunStart(EditorScreen e) { _runStartTest = CurrentTest(e); }
        public static void ClearRunStart() { _runStartTest = -1; }

        /// <summary>The 0-based test run the editor is on right now (-1 if unreadable).</summary>
        public static int CurrentTest(EditorScreen e)
        {
            try { return e != null && RunNumber != null ? (int)RunNumber.Invoke(e, null) : -1; }
            catch { return -1; }
        }

        /// <summary>The spoken form of an event: "Test n: …" when it landed on a later test than
        /// the run started on, bare otherwise.</summary>
        public static string Prefix(int test, string message)
        {
            if (IsLaterTest(test))
                return Loc.T("editor.error.test", new { test = test + 1, message });
            return message;
        }

        /// <summary>Did this test come after the one the run started on (the prefix rule)?</summary>
        public static bool IsLaterTest(int test) => _runStartTest >= 0 && test >= 0 && test != _runStartTest;

        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(Expr.MethodOf(() => Sim.smethod_16(null, false, null)),
                    prefix: new HarmonyMethod(typeof(SimNarration), nameof(ErrorPrefix)),
                    postfix: new HarmonyMethod(typeof(SimNarration), nameof(ErrorPostfix)));
                Log.Info("[patch] sim narration armed");
            }
            catch (Exception ex) { Log.Error("[patch] sim narration failed to apply", ex); }
        }

        public static bool TryDequeue(out SimEvent ev)
        {
            lock (Gate)
            {
                if (Events.Count > 0) { ev = Events.Dequeue(); return true; }
                ev = default(SimEvent);
                return false;
            }
        }

        // The seam no-ops when the EXA is already errored — remember whether this call is the one
        // that actually sets the state, so repeats never re-announce.
        private static void ErrorPrefix(SimExa __0, ref bool __state)
        {
            try { __state = __0 != null && __0.bool_0; } catch { __state = true; }
        }

        private static void ErrorPostfix(SimExa __0, bool __1, string __2, bool __state)
        {
            try
            {
                if (__state || __0 == null || !__0.maybe_2.method_0()) return; // player EXAs, first set only
                // While EDITING the whole sim is rebuilt every frame and zero-instruction EXAs
                // re-error on every rebuild — only a genuinely RUNNING sim's errors are events.
                var editor = GameState.TopScreen() as EditorScreen;
                if (editor == null || !editor.method_0()) return;
                // A battle OPPONENT's EXAs carry a SolutionExa too — but their names, lines and
                // error text are drawn for no one (no window, no name tag). Their deaths reach
                // the execution log as visible effects instead (ExecutionCapture).
                try { if (__0.team_0 != editor.method_24()) return; } catch { return; }
                lock (Gate)
                {
                    if (Events.Count >= QueueCap) return;
                    string line = WrittenLine(__0);
                    string msg = line != null
                        ? Loc.T("editor.error.line", new { exa = __0.string_0, line, message = GameText.Speech(__2) })
                        : Loc.T("editor.error", new { exa = __0.string_0, message = GameText.Speech(__2) });
                    Events.Enqueue(new SimEvent { Test = CurrentTest(editor), Message = msg });
                }
            }
            catch { }
        }

        /// <summary>The 1-based line the user TYPED for the EXA's current instruction — the
        /// instruction indexes the macro-expanded pass; the compiler's line map inverts it.</summary>
        private static string WrittenLine(SimExa exa)
        {
            try
            {
                var instr = exa.method_10();
                if (!instr.maybe_0.method_0()) return null;
                int expanded = instr.maybe_0.method_2();
                var map = exa.maybe_2.method_2().gclass276_0.gclass286_1.dictionary_0;
                foreach (var pair in map)
                    if (pair.Value == expanded) return (pair.Key + 1).ToString();
                return (expanded + 1).ToString(); // identity fallback (macro-free code)
            }
            catch { return null; }
        }
    }
}

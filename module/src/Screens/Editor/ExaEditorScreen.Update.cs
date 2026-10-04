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
        // ---- per-frame: step-cycle echo, run-stop and test-run-complete announcements ----

        private object _instance;
        private bool _wasRunning, _wasSolved, _wasCodeFocused, _wasShowGoal, _wasPlayMode;
        private int _renameFocused = -1; // the EXA whose rename node had our focus last tick

        public override void OnUpdate()
        {
            var e = Editor;
            if (e == null) return;
            if (!ReferenceEquals(_instance, e))
            {
                _instance = e;
                _wasRunning = _wasSolved = _stepEcho = _wasCodeFocused = _wasPlayMode = false;
                _lastCycle = -1;
                _lastCodeExa = int.MinValue;
                _popupFile = null;
                _popupGoalHost = _popupGoalRequired = -1;
                _goalPopup = false;
                _goalPopupPending = 0;
                _runToLine = 0;
                _suppressRunAnnounce = false;
                _testLog.Clear();
                Patches.ExecutionCapture.Clear();
                _execAnchor = null;
                _testAnchor = null;
                _gotoBuffer = "";
                _refocusLog = null;
                Patches.PanelCapture.SetArmed(false, false);
            }

            // The panel capture publishes at tick time: the buffer holds the PREVIOUS frame's
            // complete draw (our tick runs before this frame draws). The deferred popup open
            // waits for that — the first published goal-view frame.
            Patches.PanelCapture.Publish();
            if (_goalPopupPending > 0 && --_goalPopupPending == 0)
            {
                if (GoalRowTexts(e).Count == 0)
                {
                    Patches.PanelCapture.SetArmed(false, false);
                    Speech.Tts.Speak(Loc.T("nav.no_details"));
                }
                else
                {
                    _goalPopup = true;
                    Navigation.FocusStop("goalpop");
                }
            }

            // (Battle Win Count no longer announces globally — the ed.wins row is LIVE, so
            // the counter ticks only while that row is focused; user rule 2026-08-29.)

            // The exec log's synthetic "Go to cycle" field types through our own keyboard
            // snapshot (no game widget backs it).
            PollGotoTyping();

            // Leaving the code stop releases the game's real code focus (falling edge only, so a
            // mouse user's own click-focus is never fought over).
            bool codeFocused = Navigation.CaretTextEntryFocused;
            if (_wasCodeFocused && !codeFocused && FocusedCodeExa(e) != null) DisarmCode(e);
            if (codeFocused) NarrateCaret(e, !_wasCodeFocused);
            else _caretExa = int.MinValue;
            _wasCodeFocused = codeFocused;

            // Leaving the solution-name field commits it, like the game's click-away; leaving
            // the test-run field closes it the same way (silently — the landing speaks). The
            // run field keys on the NODE, not TextEntryFocused: it opens from the slider
            // vtable, which only becomes a text field at the NEXT rebuild — the flag check
            // would kill it on the opening frame.
            if (NameArmed(e) && !Navigation.TextEntryFocused) CommitName(e);
            // Leaving an EXA's rename node commits that name (falling edge on OUR focus, keyed
            // to the EXA we armed — a mouse user's own title click is never closed under them).
            int renameFocused = FocusedRenameExa();
            if (_renameFocused >= 0 && renameFocused != _renameFocused && ArmedNameExa(e) == _renameFocused)
                CommitExaName(e, announce: false);
            _renameFocused = renameFocused;
            if (RunFieldOpen(e)
                && !ControlId.Structural("ed.run").Equals((Navigation.Active as GraphNavigator)?.FocusedNodeId))
                CloseRunField(announce: false);

            // The native F1 press opens the goal popup, same as our button (while the popup
            // forces the flag, method_7 reads true — no re-trigger until a fresh press).
            bool showGoal = false;
            try { showGoal = e.method_7(); } catch { }
            if (showGoal && !_wasShowGoal) RequestGoalPopup();
            _wasShowGoal = showGoal;

            var sim = TheSim(e);
            bool running = false;
            try { running = e.method_0(); } catch { }

            // A free run started NATIVELY (F4/F5) must stand the step echo down the way our Run
            // button does, or the echo narrates every cycle of the run. This is also the
            // F4-after-stepping "Running." — a stepped sim is already armed, so the
            // not-running -> running announce below never fires for it.
            if (_stepEcho && running && !StepBudget(e).method_0())
            {
                _stepEcho = false;
                if (_suppressRunAnnounce) _suppressRunAnnounce = false;
                else Speech.Tts.Speak(Loc.T("editor.running"));
            }

            // Stop transition FIRST: a reset rebuilds the sim at cycle 0, and the echo below
            // must not read that as a step.
            if (_wasRunning && !running)
            {
                _stepEcho = false;
                _lastCycle = -1;
                _runToLine = 0;
                _suppressRunAnnounce = false;
                Patches.SimNarration.ClearRunStart();
                Speech.Tts.Speak(_runCycles > 0
                    ? Loc.T("editor.stopped.at", new { n = _runCycles })
                    : Loc.T("editor.stopped"));
                _runCycles = 0;
                _goalStates = null;
            }
            // A free run started by the NATIVE F4/F5 gets the same "Running." confirmation our
            // buttons give (stepping stays quiet — its cycle echo is the feedback).
            if (running && !_wasRunning && !_stepEcho)
            {
                if (_suppressRunAnnounce) _suppressRunAnnounce = false;
                else Speech.Tts.Speak(Loc.T("editor.running"));
            }
            // ANY arming (run buttons, native F4/F5, F2 stepping) baselines the test run the
            // run started on — events landing on a LATER auto-advanced test name their test —
            // and starts a fresh test log (the previous run's log lives until now, so it can
            // be browsed after the stop).
            if (running && !_wasRunning)
            {
                Patches.SimNarration.MarkRunStart(e);
                // Re-arming under the user's feet: the log stops vanish for the frames their
                // stores sit empty, and reconcile re-seats focus wherever it lands — silently
                // OUT of the log, where ctrl+arrows then do nothing (user report, 2026-08-25).
                // Follow the NEW run instead: remember which log stop held focus and return
                // there once it refills.
                object focusStop = Navigation.FocusedStopKey;
                _refocusLog = "execlog".Equals(focusStop) || "execgoto".Equals(focusStop)
                    || "testlog".Equals(focusStop) ? (string)focusStop : null;
                _refocusTtl = 180; // a free run refills within a frame; a paused F2 arm may never
                _testLog.Clear();
                Patches.ExecutionCapture.Clear();
                _execAnchor = null; // a fresh run's logs follow the tail again
                _testAnchor = null;
                _gotoBuffer = "";
            }
            if (_refocusLog != null)
            {
                bool refilled = _refocusLog == "testlog"
                    ? !_testLog.IsEmpty
                    : !Patches.ExecutionCapture.Store.IsEmpty;
                if (refilled)
                {
                    Navigation.FocusStop(_refocusLog);
                    _refocusLog = null;
                }
                else if (--_refocusTtl <= 0) _refocusLog = null;
            }
            _wasRunning = running;

            // PLAY MODE transitions (sandbox): entering a free run hands the keyboard to the
            // Redshift pad — say so, right after the "Running." it rides in on. Leaving it
            // while still armed = a NATIVE pause (F3/Tab — our Pause button can't fire in play
            // mode: Enter belongs to the pad), which otherwise says nothing: announce it and
            // arm the step echo the way our Pause button does. A run-to arrival pause speaks
            // its own landing below; a full stop already said "Stopped.".
            bool play = PlayMode;
            if (play && !_wasPlayMode) Speech.Tts.Speak(Loc.T("editor.playmode"));
            if (_wasPlayMode && !play && running && _runToLine == 0)
            {
                _stepEcho = true;
                Speech.Tts.Speak(Loc.T("editor.paused"));
            }
            _wasPlayMode = play;

            int cycles = 0;
            try { cycles = sim != null ? sim.method_52() : 0; } catch { }
            if (running && cycles > 0) _runCycles = cycles;

            if (_stepEcho && sim != null && cycles != _lastCycle)
            {
                // Announce from cycle 0 too: the first step arms the sim paused, and hearing the
                // PENDING instruction ("Cycle 0. XA: LINK 800") is the point of stepping.
                if (_lastCycle >= 0 || cycles == 0)
                    SpeakStep(e, cycles);
                _lastCycle = cycles;
            }

            // Run-to arrival: the marker clears when the hunt ends. Still armed with a zero
            // step budget = PAUSED at the target — speak the landing and hand over to the step
            // echo (a cancel or a stop clears the marker too; those announce themselves).
            if (_runToLine > 0 && EmptyRunToMarker != null)
            {
                bool pending = true;
                try { pending = !RunToMarkerField.GetValue(e).Equals(EmptyRunToMarker); }
                catch { }
                if (!pending)
                {
                    if (running && StepBudget(e).method_0())
                    {
                        _stepEcho = true;
                        _lastCycle = cycles;
                        SpeakStep(e, cycles);
                    }
                    _runToLine = 0;
                    _suppressRunAnnounce = false;
                }
            }

            // Buffered sim events (errors captured by the SimNarration patch — the model deletes
            // errored EXAs after one cycle, so polling could never catch them). EVERY event lands
            // in the test log; it is SPOKEN live only while STEPPING (user rule 2026-08-23: a
            // free run's error deaths are routine — a fan-out solution's probes dying by design,
            // times up to 100 tests — so a free run voices goal failures only, see WatchGoals).
            // Drained AFTER the cycle echo: its interrupt would cut an error spoken first (a
            // step onto an error lands both in the same frame), while errors queue behind the
            // echo untouched.
            Patches.SimNarration.SimEvent ev;
            int drained = 0, spoken = 0;
            while (drained++ < 1024 && Patches.SimNarration.TryDequeue(out ev))
            {
                _testLog.Add(ev.Test, ev.Message);
                if (_stepEcho && spoken++ < 4)
                    Speech.Tts.Speak(Patches.SimNarration.Prefix(ev.Test, ev.Message));
            }

            WatchGoals(e, sim, running, cycles);

            bool solved = false;
            try { solved = sim != null && sim.method_47(); } catch { }
            // Voiced while STEPPING only — Run/Fast sweeps all 100 runs and would repeat this
            // per run (user rule: free runs voice failures only).
            if (solved && !_wasSolved && _stepEcho)
                Speech.Tts.Speak(GameText.T("Test Run Complete"));
            _wasSolved = solved;
        }

        private int _runCycles;
        private int[] _goalStates;
        private string _refocusLog; // log stop to re-enter once its store refills (see arming)
        private int _refocusTtl;

        // "Cycle 3", "XA: LINK 800" (two entries) — the next instruction of the code-focused EXA (or the first
        // live player EXA), read from the macro-expanded source that actually executes.
        // One speech entry per event: the first interrupts (it arrived on a press), the rest
        // queue behind it — never one mashed-together string.
        private void SpeakStep(EditorScreen e, int cycle)
        {
            var entries = StepEchoScope.All ? StepNarrationAll(e, cycle) : StepNarration(e, cycle);
            for (int i = 0; i < entries.Count; i++)
                Speech.Tts.Speak(entries[i], interrupt: i == 0);
        }

        private System.Collections.Generic.List<string> StepNarration(EditorScreen e, int cycle)
        {
            string detail = null;
            try
            {
                var sim = TheSim(e);
                SimExa target = null;
                // Off the code stop the game's code focus is released (DisarmCode) — the echo
                // stays on the LAST-armed program's followed instance, never sliding back to
                // the first EXA just because focus Tabbed to another stop.
                var focused = FocusedCodeExa(e) ?? LastOrFirstExa(e);
                Team mine = e.method_24();
                if (focused != null)
                {
                    var followed = FollowedExa(focused);
                    if (followed != null && followed.team_0 == mine) target = followed;
                }
                if (sim != null && target == null)
                    foreach (var entity in sim.list_1)
                    {
                        var exa = entity as SimExa;
                        if (exa == null || !exa.maybe_2.method_0()) continue;
                        // Another team's code is never drawn (no window, no tag, no line
                        // highlight — the team gate), so the echo must not read it either:
                        // with no live player EXA the echo stays a bare "Cycle n" (enemy
                        // MOVES still land in the exec log as visible-effect rows).
                        if (exa.team_0 != mine) continue;
                        if (focused != null && ReferenceEquals(exa.maybe_2.method_2(), focused)) { target = exa; break; }
                        if (target == null) target = exa;
                    }
                if (target != null)
                {
                    string line = PendingInstruction(target);
                    if (line != null) detail = target.string_0 + ": " + line;
                }
            }
            catch { }
            var parts = new System.Collections.Generic.List<string> { Loc.T("editor.cycle", new { n = cycle }) };
            if (detail != null) parts.Add(detail);
            return parts;
        }

        // The "All EXAs" step scope (Mod tab), one entry each: first what the other side VISIBLY
        // did in the cycle just stepped (the exec log's effect rows — enemy code stays unspoken),
        // then "Cycle n", then every live player EXA's pending instruction, in the drawn window
        // order (program list index, then creation — each REPL copy right after its parent).
        // Hidden-host occupants have no window, so they stay out like everywhere else.
        private System.Collections.Generic.List<string> StepNarrationAll(EditorScreen e, int cycle)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (cycle > 0) parts.AddRange(Patches.ExecutionCapture.LastCycleEffects);
            parts.Add(Loc.T("editor.cycle", new { n = cycle }));
            try
            {
                var sim = TheSim(e);
                Team mine = e.method_24();
                var programs = e.solution_0.list_0;
                var live = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<long, SimExa>>();
                if (sim != null)
                    foreach (var entity in sim.list_1)
                    {
                        var exa = entity as SimExa;
                        if (exa == null || !exa.maybe_2.method_0() || exa.team_0 != mine) continue;
                        try { if (HostHidden(exa.method_0(), false)) continue; } catch { }
                        long order = 0;
                        try { order = ((long)programs.IndexOf(exa.maybe_2.method_2()) << 32) + exa.entityDisplayRank_0.int_1; } catch { }
                        live.Add(new System.Collections.Generic.KeyValuePair<long, SimExa>(order, exa));
                    }
                foreach (var kv in System.Linq.Enumerable.OrderBy(live, p => p.Key))
                {
                    string line = PendingInstruction(kv.Value);
                    if (line != null) parts.Add(kv.Value.string_0 + ": " + line);
                }
            }
            catch { }
            return parts;
        }

        // Goal-state flips while the sim runs: every flip lands in the test log; live speech is
        // the key feedback during Run/Fast — FAILURES only there (the per-run completes across
        // 100 auto-advancing test runs were pure spam — user rule), both directions while
        // stepping; the completion screen announces overall success. A failure on a later
        // test than the run started on names its test, like errors do.
        private void WatchGoals(EditorScreen e, Sim sim, bool running, int cycles)
        {
            if (sim == null || !running || cycles < 1) { _goalStates = null; return; }
            try
            {
                var goals = sim.list_2;
                if (_goalStates == null || _goalStates.Length != goals.Count)
                {
                    _goalStates = new int[goals.Count];
                    for (int i = 0; i < goals.Count; i++)
                        _goalStates[i] = (int)goals[i].imethod_1(sim, sim.list_5).genum152_0;
                    return;
                }
                int test = Patches.SimNarration.CurrentTest(e);
                for (int i = 0; i < goals.Count; i++)
                {
                    int state = (int)goals[i].imethod_1(sim, sim.list_5).genum152_0;
                    if (state != _goalStates[i] && state != 0)
                    {
                        string text = GoalLabel(i) + ", " + Loc.T(state == 1 ? "value.complete" : "value.failed");
                        _testLog.Add(test, text);
                        if (_stepEcho || state != 1)
                            Speech.Tts.Speak(Patches.SimNarration.Prefix(test, text));
                    }
                    _goalStates[i] = state;
                }
            }
            catch { }
        }
    }
}

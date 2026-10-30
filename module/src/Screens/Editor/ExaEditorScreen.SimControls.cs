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
        // ---- sim controls: the five buttons, each invoking the game's own handler path ----

        private void BuildControls(GraphBuilder b, EditorScreen e)
        {
            b.BeginStop("controls");
            b.PushContext(Loc.T("editor.controls"), positions: false);
            b.StartRow("ed.controls");
            ControlButton(b, "ed.ctl.step", "Step",
                "Press to advance the simulation a single cycle.\n\nHold to continuously advance the simulation.", StepSim);
            ControlButton(b, "ed.ctl.run", "Run",
                "Run the simulation at a speed that is slow enough to watch.", () => RunSim(fast: false));
            ControlButton(b, "ed.ctl.fast", "Fast-forward",
                "Run the simulation as quickly as possible.", () => RunSim(fast: true));
            ControlButton(b, "ed.ctl.pause", "Pause", "Pause the simulation.", PauseSim);
            ControlButton(b, "ed.ctl.reset", "Reset", "Reset the simulation.", ResetSim);
            b.EndRow();
            // The console's 2D/3D switch (sandbox only): natively a MOUSE-ONLY hotspot on
            // the drawn console (GClass313's click bounds — no key reaches it) flipping
            // EditorScreen.bool_10, which #EN3D reads and the anaglyph crossfade follows.
            // Same flip + the game's own switch sound; works armed or editing, like the click.
            if (SandboxMode(e))
                b.AddItem(ControlId.Structural("ed.3d"), new NodeVtable
                {
                    ControlType = ControlTypes.Toggle,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => Loc.T("editor.3dswitch"), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(Mode3dText, live: true, kind: AnnouncementKinds.Value),
                    },
                    StateText = Mode3dText,
                    OnActivate = Toggle3d,
                });
            b.PopContext();
        }

        /// <summary>The switch position by its own drawn labels: "2D" or "3D".</summary>
        private static string Mode3dText()
        {
            try { return Loc.T(Editor?.bool_10 == true ? "editor.mode3d" : "editor.mode2d"); }
            catch { return null; }
        }

        private static void Toggle3d()
        {
            var e = Editor;
            if (e == null) return;
            try
            {
                e.bool_10 = !e.bool_10;
                try { GClass45.soundsNamespace_0.sound_29.smethod_1(1f); } catch { }
            }
            catch (Exception ex) { Log.Error("[editor] 2D/3D toggle failed", ex); }
        }

        private static void ControlButton(GraphBuilder b, string id, string gameKey, string tooltipKey, Action activate)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T(gameKey), kind: AnnouncementKinds.Label),
                },
                OnTooltip = () => Speech.Tts.Speak(GameText.TSpeech(tooltipKey)),
                OnActivate = activate,
            });
        }

        // The first compile error across the solution, from the requested compile pass.
        private static bool FirstCompileError(EditorScreen e, bool expanded, out string exaName, out CompileError error)
        {
            exaName = null; error = null;
            try
            {
                foreach (var exa in e.solution_0.list_0)
                {
                    var errors = expanded ? exa.gclass276_0.gclass286_1.list_1 : exa.gclass276_0.gclass286_0.list_1;
                    if (errors.Count > 0)
                    {
                        exaName = exa.string_0;
                        error = errors[0];
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void SpeakCompileError(string exaName, CompileError error)
        {
            try
            {
                Speech.Tts.Speak(Loc.T("editor.compile_error",
                    new { exa = exaName, line = error.int_0 + 1, message = GameText.Speech(error.string_0) }));
            }
            catch { }
        }

        private static void Invoke(MethodInfo m, EditorScreen e, params object[] args)
        {
            try { m?.Invoke(e, args); }
            catch (Exception ex) { Log.Error("[editor] control invoke failed", ex); }
        }

        private bool _stepEcho; // announce cycle numbers while single-stepping (not while running)
        private int _lastCycle = -1;

        private void StepSim()
        {
            var e = Editor;
            if (e == null) return;
            Invoke(PreActionMethod, e);
            string exaName; CompileError error;
            if (FirstCompileError(e, expanded: true, out exaName, out error))
            {
                // The game's step-with-errors path flips into bool_12 — the read-only error VIEW,
                // which LOCKS the code editor until reset. Announcing the error carries the same
                // information without trapping a blind user out of their own code.
                SpeakCompileError(exaName, error);
                return;
            }
            var sim = TheSim(e);
            try
            {
                if (sim != null && sim.method_47())
                {
                    // Test run solved: step advances to the next run once animations settle.
                    var anims = AnimationsField?.GetValue(e) as System.Collections.ICollection;
                    if (anims == null || anims.Count == 0) Invoke(NextRunMethod, e);
                    return;
                }
            }
            catch { }
            _stepEcho = true;
            Invoke(AdvanceMethod, e, false, (Maybe<int>)(e.method_0() ? 1 : 0));
        }

        // ---- run to caret line: the keyboard shape of the game's Alt+Click "run to
        // instruction". F8 arms the game's own marker (method_52, which does the
        // written->expanded remap while editing) at the caret's line — the game then runs and
        // pauses when ANY EXA of this program reaches it; Shift+F8 pins the SPECIFIC EXA. The
        // arrival pause keeps the sim ARMED with a zero step budget, which the stop narration
        // ignores — the OnUpdate watch below speaks the landing and arms the step echo. ----

        private int _runToLine;              // 1-based, for speech; 0 = idle
        private int _runToExpanded = -1;     // the target's EXECUTING-listing line (read-cursor index)
        private int _runToProgram;           // the target's solution number
        private bool _suppressRunAnnounce;   // the arm announce replaces the generic "Running."

        // F8 — unpinned (GEnum175 1): the marker matches ANY copy of the focused program,
        // first arrival wins; one-shot, re-arm after each pause.
        private void RunToCaret()
        {
            var e = Editor;
            if (e == null) return;
            var exa = FocusedCodeExa(e);
            if (exa == null || !Navigation.CaretTextEntryFocused)
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                return;
            }
            string exaName; CompileError error;
            if (FirstCompileError(e, expanded: true, out exaName, out error))
            {
                // Same guard as Run: the game would flip the LOCKED error view and drop the
                // marker without a word.
                SpeakCompileError(exaName, error);
                return;
            }
            try
            {
                int line, expanded;
                if (!Editing(e))
                {
                    // Armed: the target is the VIRTUAL read cursor's line — it already indexes
                    // the executing (expanded) listing, which is what method_52 takes mid-run.
                    if (_virtLine < 0)
                    {
                        Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                        return;
                    }
                    line = expanded = _virtLine;
                }
                else
                {
                    ResolveCaretLine(exa, out line, out expanded);
                }
                ArmMarker(e, exa, exa.method_0(), (GEnum175)1, line, expanded, null);
            }
            catch (Exception ex) { Log.Error("[editor] run-to failed", ex); }
        }

        // Shift+Enter — pinned (GEnum175 0, the native Alt+Click in a specific window):
        // on an EXA WINDOW row, pin THAT row's EXA — the line is the read cursor when the
        // code stop is on its program, else the program's frozen edit caret, so it ALWAYS
        // resolves (v2, user redesign 2026-08-23; v1's browsed-since-arming precondition
        // sank it). In the code editor, pin the program's ORIGINAL, symmetric with F8.
        private void RunToPinned()
        {
            var e = Editor;
            if (e == null) return;
            string exaName; CompileError error;
            if (FirstCompileError(e, expanded: true, out exaName, out error))
            {
                SpeakCompileError(exaName, error);
                return;
            }
            try
            {
                if (TryPinFromWindowRow(e)) return;
                var exa = FocusedCodeExa(e);
                if (exa == null || !Navigation.CaretTextEntryFocused)
                {
                    Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                    return;
                }
                int line, expanded;
                if (!Editing(e))
                {
                    if (_virtLine < 0)
                    {
                        Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                        return;
                    }
                    line = expanded = _virtLine;
                }
                else
                {
                    ResolveCaretLine(exa, out line, out expanded);
                }
                // Pin the FOLLOWED instance (the original unless a copy is followed via its
                // window row or Ctrl+Left/Right) — "this EXA" is whoever the label says.
                var live = FollowedExa(exa);
                int entity = exa.method_0();
                string name = exa.string_0;
                if (live != null)
                {
                    try { entity = live.entityID_0.Number; name = live.string_0; } catch { }
                }
                ArmMarker(e, exa, entity, (GEnum175)0, line, expanded, name);
            }
            catch (Exception ex) { Log.Error("[editor] run-to pinned failed", ex); }
        }

        /// <summary>Alt+Down/Up in the ARMED code field: cycle which live instance of
        /// this program the code view follows (original, then copies in spawn order,
        /// wrapping) — Ctrl+Up/Down walks programs, Alt+Up/Down walks instances. The read
        /// cursor snaps to the new instance's current instruction and the announce names
        /// it — Shift+Enter then pins whoever the label says.</summary>
        private void FollowInstance(int dir)
        {
            try
            {
                var e = Editor;
                if (e == null || Editing(e) || !Navigation.CaretTextEntryFocused) return;
                var program = FocusedCodeExa(e);
                var sim = TheSim(e);
                if (program == null || sim == null) return;
                int s = program.method_0();
                var list = new System.Collections.Generic.List<SimExa>();
                foreach (var entity in sim.list_1)
                {
                    var x = entity as SimExa;
                    if (x != null && x.maybe_2.method_0() && x.maybe_2.method_2().method_0() == s
                        && Mine(x))
                        list.Add(x);
                }
                if (list.Count == 0) return;
                var cur = FollowedExa(program);
                int idx = list.FindIndex(x => ReferenceEquals(x, cur));
                if (idx < 0) idx = 0;
                idx = ((idx + dir) % list.Count + list.Count) % list.Count;
                var next = list[idx];
                _followedEntity = next.entityID_0.Number;
                // Land ON the instance's currently executing line and announce it exactly
                // like an arrow move would — the current-marker carries the name (user
                // spec: "LINK 800, current, XA:1"; no special wording).
                string announce = next.string_0; // fallback when it has no pending line
                try
                {
                    int cl = CurrentListingLine(next);
                    if (cl >= 0)
                    {
                        _virtExa = s;
                        _virtLine = cl;
                        string listing = next.method_9() ?? string.Empty;
                        int start = CaretText.OffsetOfLine(listing, cl);
                        int end = listing.IndexOf('\n', start);
                        if (end < 0) end = listing.Length;
                        string lineText = end > start
                            ? listing.Substring(start, end - start)
                            : Loc.T("text.blank");
                        announce = lineText + (CurrentMarker(program, cl) ?? "");
                    }
                }
                catch { }
                Speech.Tts.Speak(announce, interrupt: true);
            }
            catch (Exception ex) { Log.Error("[editor] follow instance failed", ex); }
        }

        /// <summary>The window-row half of Shift+Enter: consumed (true) whenever focus sits
        /// on a win.exa row, whatever the outcome — never falls through to the code path.</summary>
        private bool TryPinFromWindowRow(EditorScreen e)
        {
            int n;
            var key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (key == null || !key.StartsWith("win.exa.", StringComparison.Ordinal)
                || !int.TryParse(key.Substring(8), out n)) return false;
            var target = FindExaByEntity(n);
            if (target == null || !target.maybe_2.method_0())
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true); // row went stale
                return true;
            }
            var program = target.maybe_2.method_2();
            int line, expanded;
            if (!Editing(e) && _virtLine >= 0 && ReferenceEquals(FocusedCodeExa(e), program))
                line = expanded = _virtLine; // the read cursor: the latest expressed intent
            else
                ResolveCaretLine(program, out line, out expanded); // always defined
            ArmMarker(e, program, n, (GEnum175)0, line, expanded, target.string_0);
            return true;
        }

        /// <summary>The written line under a PROGRAM's edit caret (frozen mid-run) and its
        /// macro-expanded index.</summary>
        private static void ResolveCaretLine(SolutionExa exa, out int line, out int expanded)
        {
            string text = exa.string_1 ?? string.Empty;
            int caret = (int)CaretField.GetValue(exa.codeEditorWidget_0);
            if (caret > text.Length) caret = text.Length;
            line = CaretText.LineIndex(text, caret);
            if (!exa.gclass276_0.gclass286_1.dictionary_0.TryGetValue(line, out expanded)) expanded = line;
        }

        /// <summary>Forward-snap a target to the first REAL instruction at-or-after it.
        /// Blanks, NOTEs and MARKs compile to EmptyLine and OCCUPY line indices — keyboard
        /// browsing lands on them constantly, and a jump target IS its MARK line — so
        /// refusing there was a keyboard dead-end the mouse never hits (Alt+Click only
        /// offers opcode lines). Execution itself skips EmptyLines; "run to this MARK"
        /// means its first real instruction. false = nothing runnable at or below.</summary>
        private static bool SnapToInstruction(SolutionExa program, bool editing,
            ref int line, ref int expanded, out string lineText)
        {
            var pass = program.gclass276_0.gclass286_1;
            if (editing)
            {
                var written = (program.string_1 ?? string.Empty).Split('\n');
                for (int w = line; w < written.Length; w++)
                {
                    int exp;
                    if (!pass.dictionary_0.TryGetValue(w, out exp)) exp = w;
                    if (exp < pass.list_0.Count && Sim.smethod_13(pass.list_0[exp]))
                    {
                        line = w;
                        expanded = exp;
                        lineText = written[w];
                        return true;
                    }
                }
            }
            else
            {
                var listing = (pass.string_0 ?? string.Empty).Split('\n');
                for (int x = Math.Max(0, expanded); x < pass.list_0.Count; x++)
                {
                    if (Sim.smethod_13(pass.list_0[x]))
                    {
                        line = expanded = x;
                        lineText = x < listing.Length ? listing[x] : null;
                        return true;
                    }
                }
            }
            lineText = null;
            return false;
        }

        /// <summary>Shared arm tail: snap to the first real instruction, clear any stale
        /// marker, arm, and say where (line + ITS TEXT, so a mistarget is instantly
        /// audible) and — when pinned — who.</summary>
        private void ArmMarker(EditorScreen e, SolutionExa program, int entityNumber, GEnum175 mode,
            int line, int expanded, string name)
        {
            bool editing = Editing(e);
            string lineText;
            if (!SnapToInstruction(program, editing, ref line, ref expanded, out lineText))
            {
                Speech.Tts.Speak(Loc.T("editor.runto.invalid"), interrupt: true);
                return;
            }
            // The name is DISAMBIGUATION — with a single live instance it is noise (user
            // rule 2026-08-23), the same principle as the bare "current" marker.
            if (name != null && LiveInstanceCount(program) <= 1) name = null;
            Invoke(PreActionMethod, e); // also clears any stale marker
            e.method_52(mode, program, EntityID.Exa(entityNumber), editing ? line : expanded);
            if (editing) ClearMarkerSkip(e);
            _runToLine = line + 1;
            _runToExpanded = expanded;
            _runToProgram = program.method_0();
            _suppressRunAnnounce = true;
            _stepEcho = false;
            string text = string.IsNullOrWhiteSpace(lineText) ? "" : lineText.Trim();
            Speech.Tts.Speak(name == null
                    ? Loc.T("editor.runto", new { line = line + 1, text })
                    : Loc.T("editor.runto.exa", new { line = line + 1, text, exa = name }),
                interrupt: true);
        }

        /// <summary>Armed from EDIT mode, the target counts from cycle 0: the game's marker skips
        /// its first check (where the EXA already stands), which turned F8 on a line that runs
        /// at cycle 0 into "the next time" — often the next test (user report, 2026-10-29).
        /// Mid-run arms keep the skip: there it means "the next time it gets here".</summary>
        private static void ClearMarkerSkip(EditorScreen e)
        {
            try
            {
                if (RunToMarkerValueField == null || RunToMarkerSkipField == null) return;
                var marker = RunToMarkerValueField.GetValue(RunToMarkerField.GetValue(e));
                if (marker != null) RunToMarkerSkipField.SetValue(marker, false);
            }
            catch (Exception ex) { Log.Error("[editor] run-to skip clear failed", ex); }
        }

        private void RunSim(bool fast)
        {
            var e = Editor;
            if (e == null) return;
            Invoke(PreActionMethod, e);
            string exaName; CompileError error;
            if (FirstCompileError(e, expanded: true, out exaName, out error))
            {
                // Announce only — never enter the editing-locked error view (see StepSim).
                SpeakCompileError(exaName, error);
                return;
            }
            _stepEcho = false;
            Invoke(AdvanceMethod, e, fast, (Maybe<int>)GStruct10.gstruct10_0);
            // The run-start confirmation comes from the state transition in OnUpdate — one
            // announcement whether the run began from our buttons or the native F4/F5.
        }

        private void PauseSim()
        {
            var e = Editor;
            if (e == null || !e.method_0()) return;
            Invoke(PreActionMethod, e);
            Invoke(PauseMethod, e);
            _stepEcho = true; // stepping usually follows a pause
            Speech.Tts.Speak(Loc.T("editor.paused"));
        }

        private void ResetSim()
        {
            var e = Editor;
            if (e == null) return;
            Invoke(PreActionMethod, e);
            bool errorView = false;
            try { errorView = ErrorViewField != null && (bool)ErrorViewField.GetValue(e); } catch { }
            if (errorView)
            {
                try { ErrorViewField.SetValue(e, false); } catch { }
            }
            else if (e.method_0())
            {
                Invoke(ResetMethod, e);
            }
            _stepEcho = false;
            Speech.Tts.Speak(Loc.T("editor.reset"));
        }

        // "1 / 5" — current EXA count against the effective cap (the same two limits the game's
        // Create button greys out on: the per-team create cap and the storage limit).
        private static string ExaCountText()
        {
            try
            {
                var e = Editor;
                if (e == null) return null;
                int count = e.solution_0.list_0.Count;
                try
                {
                    var sim = TheSim(e);
                    if (sim != null)
                    {
                        int cap = Math.Min(sim.dictionary_0[e.method_24()].int_0, sim.int_6);
                        return count + " / " + cap;
                    }
                }
                catch { }
                return count.ToString();
            }
            catch { return null; }
        }
    }
}

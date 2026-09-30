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
        // ---- the window column: one row per player EXA (registers + location + error) and one
        // per file window (location + contents). The Sim is rebuilt every frame, so rows are
        // keyed by stable ids and every announcement re-finds its entity live. ----

        private static readonly FieldInfo WindowListField = Deobf.Field(typeof(EditorScreen), "dictionary_1");

        /// <summary>"n of m" over the DRAWN WINDOWS (EXA windows, copies included, then file
        /// windows) — the count a sighted player sees in the column. Stamped explicitly because
        /// an EXA's row is a multi-cell bar (EXA → M-bus → Name) and the builder would position
        /// those cells within the bar instead; the bar's buttons speak no position at all
        /// (user rule, 2026-09-24).</summary>
        private static NodeAnnouncement WindowPosition(int index, int total)
        {
            return new NodeAnnouncement(
                () => total > 1 && GraphAnnouncer.PositionText != null ? GraphAnnouncer.PositionText(index, total) : null,
                kind: AnnouncementKinds.Position);
        }

        private void BuildWindows(GraphBuilder b, EditorScreen e)
        {
            var sim = TheSim(e);
            if (sim == null) return;

            // ---- pass 1: the window set, in drawn order (EXAs, then files) ----
            var exas = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, int>>(); // entity number, solution number
            var exaOrder = new System.Collections.Generic.List<long>(); // parallel: drawn-order sort key
            var programs = e.solution_0.list_0;
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var entity in sim.list_1)
            {
                var exa = entity as SimExa;
                if (exa == null || !exa.maybe_2.method_0()) continue;
                // BATTLE: the OPPONENT's EXAs carry SolutionExas too (their solution's) — but
                // the game windows only YOUR team's EXAs (method_39's team gate). No window
                // for sighted players = no row here; their solution numbers also collide with
                // ours, so an enemy row's Enter/Backspace/M-bus would act on OUR program.
                try { if (exa.team_0 != e.method_24()) continue; } catch { continue; }
                int solution = 0;
                try { solution = exa.maybe_2.method_2().method_0(); } catch { }
                int n = solution;
                try { n = exa.entityID_0.Number; } catch { }
                // Rows are keyed by ENTITY number: unique per live EXA. The game windows
                // every visible entity — a REPL copy gets its own window (fresh EntityID,
                // the game's own ":1" name) with the same registers readout; the authored
                // controls (edit / delete / rename / M-bus) stay on the ORIGINAL's row only,
                // like the drawn window's buttons. A copy shares its parent's SOLUTION number,
                // which is what tells the two kinds apart. Hidden-host occupants get no window.
                if (!seen.Add(n)) continue;
                try { if (HostHidden(exa.method_0(), false)) continue; } catch { }
                exas.Add(new System.Collections.Generic.KeyValuePair<int, int>(n, solution));
                // The game draws EXA windows sorted by display rank = (program's index in the
                // solution list, creation counter): originals in list order, each REPL copy right
                // after its parent (sim.list_1 appends copies at the END). Keying on the LIVE list
                // index rather than the rank also shows a reorder at once — the sim (and its
                // ranks) only rebuilds on the game's next frame.
                long order = 0;
                try { order = ((long)programs.IndexOf(exa.maybe_2.method_2()) << 32) + exa.entityDisplayRank_0.int_1; } catch { }
                exaOrder.Add(order);
            }
            if (exas.Count > 1)
            {
                var sorted = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(
                    System.Linq.Enumerable.Range(0, exas.Count), i => exaOrder[i]));
                exas = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(sorted, i => exas[i]));
            }

            // FILE windows, after the EXAs like the drawn column: the game pre-opens a viewer
            // per goal-relevant file (the sighted player reads tape contents without touching
            // the map). Label = the drawn title (the bare id); value = where it sits + the
            // usual readout. Enter = the values popup on the file's CURRENT host.
            var files = new System.Collections.Generic.List<EntityID>();
            try
            {
                var windows = WindowListField?.GetValue(e)
                    as System.Collections.Generic.Dictionary<EntityID, EditorWindow>;
                if (windows != null)
                {
                    // dictionary_1 is the game's window-STATE cache: insertion-ordered, never
                    // pruned, accumulating every id windowed across test-run switches. The
                    // drawn column is not that order — the game sorts its window set by the
                    // entity's display rank (method_40: OrderBy entityDisplayRank_0, a
                    // creation-order counter, so files list host by host the way the puzzle
                    // authored them). Enumerating the cache read PB024's column roughly
                    // backwards (audit 2026-09-06); collect, then order the rows as drawn.
                    var drawn = new System.Collections.Generic.List<
                        System.Collections.Generic.KeyValuePair<EntityID, SimFile>>();
                    foreach (var winId in windows.Keys)
                    {
                        if (winId.Type != (GEnum147)1) continue; // files only — EXAs handled above
                        // The game hides a window whose entity is gone or sits in a hidden
                        // host — gate at build; announcements still re-find live.
                        var f0 = FindFileForWindow(winId.Number, winId.Hostname);
                        if (f0 == null) continue;
                        try
                        {
                            var h0 = HolderOf(f0);
                            var at0 = h0 != null ? h0.method_0() : f0.method_0();
                            if (HostHidden(at0, false)) continue;
                        }
                        catch { }
                        drawn.Add(new System.Collections.Generic.KeyValuePair<EntityID, SimFile>(winId, f0));
                    }
                    // OrderBy is stable, so two cache keys resolving to one file keep cache order.
                    foreach (var pair in System.Linq.Enumerable.OrderBy(drawn, p => p.Value.entityDisplayRank_0))
                        files.Add(pair.Key);
                }
            }
            catch (Exception ex) { Log.Error("[editor] file windows failed", ex); }

            // ---- pass 2: emit, every window row positioned over the whole column ----
            int total = exas.Count + files.Count;
            int index = 0;
            b.BeginStop("windows");
            b.PushContext(Loc.T("editor.windows"));
            foreach (var pair in exas)
            {
                int n = pair.Key, solution = pair.Value;
                index++;
                if (n != solution)
                {
                    int cn = n;
                    b.AddItem(ControlId.Structural("win.exa." + cn), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => FindExaByEntity(cn)?.string_0, kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => ExaReadoutOf(FindExaByEntity(cn)), kind: AnnouncementKinds.Value),
                            WindowPosition(index, total),
                        },
                        // Enter = its program's code, read cursor on THE COPY's current line;
                        // Shift+Enter (ui.runto.exa) pins it — see RunToPinned.
                        OnActivate = () => JumpToCopyCode(cn),
                        Selected = () => IsTargetExa(solution, cn),
                        OnSelect = () => SelectTargetExa(solution, cn),
                    });
                    continue;
                }
                // ONE ROW per original: [EXA readout] → Right → [M-bus toggle] → Right → [Name].
                // Up/Down walk the EXAs (an unkeyed row always lands on the first cell, so
                // scrolling reads XA, XB, … — user layout, 2026-09-24). Only the EXA cell
                // carries the window position; the bar's buttons speak none.
                b.StartRow();
                b.AddItem(ControlId.Structural("win.exa." + n), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => AuthoredExaName(n), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => ExaReadout(n), kind: AnnouncementKinds.Value),
                        WindowPosition(index, total),
                    },
                    // Enter = edit this EXA's code (following the ORIGINAL again); Backspace =
                    // delete it (the game's own method_36, instantly undoable with Ctrl+Z).
                    OnActivate = () => { _followedEntity = -1; JumpToCode(n); },
                    OnSecondary = () => DeleteExa(n),
                    Selected = () => IsTargetExa(n, -1),
                    OnSelect = () => SelectTargetExa(n, -1),
                });
                b.AddItem(ControlId.Structural("win.mbus." + n), new NodeVtable
                {
                    ControlType = ControlTypes.Toggle,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => Loc.T("editor.mbus", new { exa = AuthoredExaName(n) }),
                            kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => MbusText(n), kind: AnnouncementKinds.Value),
                    },
                    StateText = () => MbusText(n),
                    OnActivate = () => ToggleMbus(n),
                    SpeaksOwnPosition = true, // none: a button on the EXA's bar
                });
                // RENAME: the window title's name field (see the rename section below). Drawn
                // editable only while editing — the click path is gated on method_2().
                if (Editing(e))
                    b.AddItem(ControlId.Structural("win.name." + n), new NodeVtable
                    {
                        ControlType = ControlTypes.TextField,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => Loc.T("editor.exa.name"), kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => SolutionExaOf(n)?.string_0, kind: AnnouncementKinds.Value),
                        },
                        TextEntry = true,
                        TextEchoCaps = false, // the widget uppercases every insert
                        TextValue = () => SolutionExaOf(n)?.string_0,
                        OnSelect = () => ArmExaName(Editor, n),
                        OnActivate = () => CommitExaName(Editor, announce: true),
                        // Commit BEFORE the neighbour's landing is read (an emptied name is
                        // restored synchronously, so the row never speaks nameless).
                        OnBlur = () => { if (ArmedNameExa(Editor) == n) CommitExaName(Editor, announce: false); },
                        SpeaksOwnPosition = true,
                    });
                b.EndRow();
            }

            foreach (var id in files)
            {
                index++;
                string key = "win.file." + id.Number + "."
                    + (id.Hostname.method_0() ? id.Hostname.method_2() : "-");
                b.AddItem(ControlId.Structural(key), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => id.Number.ToString(), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => WindowFileReadout(id), kind: AnnouncementKinds.Value),
                        WindowPosition(index, total),
                    },
                    OnActivate = () => OpenWindowFilePopup(id),
                });
            }
            b.PopContext();
        }

        // ---- EXA rename: the window title's name field. The game draws it with the append-only
        // text widget (GClass60.smethod_1 over SolutionExa.string_0 — GClass68.int_1 = 2 chars,
        // inserts uppercased, Backspace deletes, every change dirtied + snapshotted by the
        // widget's caller, so Ctrl+Z undoes a rename). Arming mirrors the title click
        // (EditorScreen ~1276): queue name focus (maybe_4, kind 0) and back the name up
        // (EditorWindow.method_18). Committing mirrors the focus-change path (~1333): method_19
        // puts the backup back if the name was emptied, then method_54 closes the field. The
        // game's own Enter (smethod_16 → method_54) is behind the suppression seam, so the
        // node's Enter commits; leaving the node commits on the falling edge (Update). ----

        private static readonly MethodInfo WindowOfMethod = Deobf.Method(typeof(EditorScreen), "method_37");

        /// <summary>An original's name as AUTHORED (SolutionExa.string_0), falling back to the sim
        /// entity's. The sim is rebuilt from the solution once per game frame, so right after a
        /// synchronous rename commit (OnBlur, the restore of an emptied name) the SimExa still
        /// carries the old text for the landing announce — the solution never does.</summary>
        private static string AuthoredExaName(int number)
        {
            string authored = SolutionExaOf(number)?.string_0;
            return !string.IsNullOrEmpty(authored) ? authored : FindExa(number)?.string_0;
        }

        /// <summary>The EXA whose NAME field the game has focused, else -1 (method_5 = maybe_3).</summary>
        private static int ArmedNameExa(EditorScreen e)
        {
            try
            {
                var focus = e.method_5();
                if (!focus.method_0() || focus.method_2().genum13_0 != (GEnum13)0) return -1;
                var id = focus.method_2().entityID_0;
                if (id.Type != (GEnum147)0) return -1;
                return id.Number;
            }
            catch { return -1; }
        }

        private static EditorWindow WindowOf(EditorScreen e, EntityID id)
        {
            try { return WindowOfMethod?.Invoke(e, new object[] { id }) as EditorWindow; }
            catch { return null; }
        }

        private static void ArmExaName(EditorScreen e, int number)
        {
            if (e == null || !Editing(e) || ArmedNameExa(e) == number) return;
            try
            {
                var id = EntityID.Exa(number);
                // The click's three steps: scroll the window into view, queue the name focus
                // (applied next frame — that application also closes the other typed fields
                // and primes text input), remember the name for the empty-restore.
                FocusExaMethod?.Invoke(e, new object[] { id, false, false });
                FocusQueueField?.SetValue(e, (Maybe<GStruct9>)new GStruct9(id, (GEnum13)0));
                WindowOf(e, id)?.method_18();
                GClass288.smethod_1();
            }
            catch (Exception ex) { Log.Error("[editor] rename arm failed", ex); }
        }

        private static void CommitExaName(EditorScreen e, bool announce)
        {
            if (e == null) return;
            int number = ArmedNameExa(e);
            if (number < 0) return;
            try
            {
                WindowOf(e, EntityID.Exa(number))?.method_19(); // an emptied name comes back
                Invoke(CloseFieldsMethod, e);                     // method_54: focus off, fields closed
                if (announce)
                {
                    string name = SolutionExaOf(number)?.string_0;
                    if (!string.IsNullOrEmpty(name))
                        Speech.Tts.Speak(Loc.T("editor.exa.renamed", new { exa = name }), interrupt: true);
                }
            }
            catch (Exception ex) { Log.Error("[editor] rename commit failed", ex); }
        }

        /// <summary>The EXA number of a focused rename node, else -1.</summary>
        private static int FocusedRenameExa()
        {
            try
            {
                var key = Navigation.FocusedNodeId?.StructuralKey as string;
                if (key == null || !key.StartsWith("win.name.", StringComparison.Ordinal)) return -1;
                int n;
                return int.TryParse(key.Substring("win.name.".Length), out n) ? n : -1;
            }
            catch { return -1; }
        }

        /// <summary>The program number of a focused ORIGINAL's EXA cell ("win.exa.N" with a
        /// SolutionExa behind it — a REPL copy's row keys an entity number no program has), else -1.</summary>
        private static int FocusedWindowExa()
        {
            try
            {
                var key = Navigation.FocusedNodeId?.StructuralKey as string;
                if (key == null || !key.StartsWith("win.exa.", StringComparison.Ordinal)) return -1;
                int n;
                if (!int.TryParse(key.Substring("win.exa.".Length), out n)) return -1;
                return SolutionExaOf(n) != null ? n : -1;
            }
            catch { return -1; }
        }

        // ---- EXA reorder: the window drag (EditorScreen ~2606 — while a window is dragged the
        // game re-sorts solution_0.list_0 by window height and dirties). The list's order is each
        // program's display rank, the input to the sim's per-cycle shuffle, so it decides
        // same-cycle races (M, GRAB, LINK, RAND). Gated like the drag: editing only. The swap is
        // snapshotted so Ctrl+Z restores the order (the snapshot copies the list). Nothing to do
        // (top/bottom edge, running) = silent no-op. ----

        private static void MoveExa(int number, int dir)
        {
            var e = Editor;
            var exa = SolutionExaOf(number);
            if (e == null || exa == null || !Editing(e)) return;
            try
            {
                var list = e.solution_0.list_0;
                int from = list.IndexOf(exa), to = from + dir;
                if (from < 0 || to < 0 || to >= list.Count) return;
                list[from] = list[to];
                list[to] = exa;
                Invoke(DirtyMethod, e);
                Invoke(SnapshotMethod, e);
                // Rows sort by the live list, so the rebuild already has the new position.
                Navigation.ReannounceCurrent();
            }
            catch (Exception ex) { Log.Error("[editor] move EXA failed", ex); }
        }

        /// <summary>"at {host}" + the standard file readout — the map shows the same: the
        /// file plate sits on its host's grid, HostName never leaks a hidden/undrawn name.</summary>
        private static string WindowFileReadout(EntityID id)
        {
            var file = FindFileForWindow(id.Number, id.Hostname);
            if (file == null) return null;
            string readout = FileReadoutOf(file);
            try
            {
                var holder = HolderOf(file);
                var at = holder != null ? holder.method_0() : file.method_0();
                string host = HostName(at);
                if (!string.IsNullOrEmpty(host))
                    return Loc.T("editor.window.file.at", new { host }) + ", " + readout;
            }
            catch { }
            return readout;
        }

        private void OpenWindowFilePopup(EntityID id)
        {
            try
            {
                var sim = TheSim(Editor);
                var file = FindFileForWindow(id.Number, id.Hostname);
                if (sim == null || file == null) return;
                var holder = HolderOf(file);
                var at = holder != null ? holder.method_0() : file.method_0();
                string key = "win.file." + id.Number + "."
                    + (id.Hostname.method_0() ? id.Hostname.method_2() : "-");
                OpenFilePopup(FileId(file), sim.list_0.IndexOf(at), ControlId.Structural(key));
            }
            catch (Exception ex) { Log.Error("[editor] file window popup failed", ex); }
        }
    }
}

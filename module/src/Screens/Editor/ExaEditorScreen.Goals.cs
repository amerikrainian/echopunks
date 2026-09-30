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
        // ---- the task panel: description + the live goal checklist ----

        private void BuildTask(GraphBuilder b, EditorScreen e)
        {
            // The description can be genuinely EMPTY — the sandbox ships a 0-byte
            // sandbox.txt (Content\descriptions) — and the sandbox also has no goals, no
            // logo row and no Show Goal, which left the whole stop as one silent blank row
            // (user report, 2026-08-29). Rows build only when they carry content, and the
            // stop only when it has rows — the Problems/logs absent-while-empty shape.
            string desc = null;
            try { desc = GameText.Speech(Meta(e)?.locString_2.ToString()); } catch { }
            if (string.IsNullOrWhiteSpace(desc)) desc = null;
            var sim0 = TheSim(e);
            int goals = 0;
            try { goals = sim0 != null ? sim0.list_2.Count : 0; } catch { }
            if (desc == null && NetworkLogoText() == null && goals == 0 && GameMode(e) != 0)
                return;
            b.BeginStop("task");
            b.PushContext(Loc.T("editor.task"), positions: false);
            if (desc != null)
                b.AddItem(ControlId.Structural("ed.desc"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => GameText.Speech(Meta(Editor)?.locString_2.ToString()),
                            kind: AnnouncementKinds.Label),
                    },
                });

            // The map's NETWORK LOGO decal (meta.texture_0, drawn on every network's
            // backdrop) is baked art — a lettered brand reaches only sighted players, and
            // on the mystery networks the logo says what no spoken string does (the game
            // titles them "UNKNOWN NETWORK"). Transcribed per puzzle; no entry = no row.
            if (NetworkLogoText() != null)
                b.AddItem(ControlId.Structural("ed.logo"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => Loc.T("editor.logo", new { text = NetworkLogoText() }),
                            kind: AnnouncementKinds.Label),
                    },
                });
            if (CompassRose() != null)
                b.AddItem(ControlId.Structural("ed.compass"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() =>
                        {
                            var ids = CompassRose();
                            return ids == null ? null : Loc.T("editor.compass",
                                new { n = ids[0], e = ids[1], s = ids[2], w = ids[3] });
                        }, kind: AnnouncementKinds.Label),
                    },
                });

            var sim = TheSim(e);
            if (sim != null)
                for (int i = 0; i < sim.list_2.Count; i++)
                {
                    int index = i;
                    b.AddItem(ControlId.Structural("ed.goal." + i), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => GoalLabel(index), kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => GoalState(index), kind: AnnouncementKinds.Value),
                        },
                    });
                }
            // The accessible Show Goal: opens the goal popup — the F1 view as text (required
            // files, register goal readouts, and the special-puzzle panels via PanelCapture).
            // NORMAL MODE ONLY: battles and the sandbox draw no Show Goal button and their F1
            // is inert (the game's own gate — EditorScreen draws it only for mode 0).
            if (GameMode(e) == 0)
                b.AddItem(ControlId.Structural("ed.goalbtn"), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => GameText.T("Show Goal"), kind: AnnouncementKinds.Label),
                    },
                    OnActivate = RequestGoalPopup,
                });
            b.PopContext();
        }

        // Logo transcriptions are language-invariant (one set of map textures ships for all
        // six game languages), so they live here rather than in ui.json. Keyed by puzzle id
        // (meta.string_0); add an entry when an audit finds a logo whose lettering says
        // something no spoken title/description carries.
        private static readonly System.Collections.Generic.Dictionary<string, string> NetworkLogos =
            new System.Collections.Generic.Dictionary<string, string>
            {
                // PB008 "UNKNOWN NETWORK 1": the brand + department line, screenshot-verified
                // 2026-08-23 — the art is the only place the network names itself.
                { "PB008", "НГТУ, ОТДЕЛ ПРИКЛАДНОЙ СЕМИОТИКИ" },
                // PB010B: the Workhouse brand plus the slogan the spoken title lacks,
                // screenshot-verified 2026-08-23.
                { "PB010B", "Workhouse. Easy work. Easy money." },
                // PB012: the bank's numeral mark + full brand name (the spoken title drops
                // "National"), screenshot-verified 2026-08-23.
                { "PB012", "1, Equity First National Bank" },
                // PB025: the same bank backdrop on the second Equity First job (title "Equity
                // First Bank", subtitle names the branch) — "National" and the numeral mark
                // are still lettering only; screenshot-verified 2026-09-06.
                { "PB025", "1, Equity First National Bank" },
                // PB014: the battle map's backdrop brand — the "News Network" subline is
                // lettering the spoken "KGOG-TV (Programming Hub)" lacks,
                // screenshot-verified 2026-08-24.
                { "PB014", "KGOG News Network" },
                // PB016: the decal's "PALO ALTO" brand line appears in NO spoken string
                // (title = "Digital Library Project" alone; grep-verified absent from
                // descriptions and strings.csv), screenshot-verified 2026-08-29.
                { "PB016", "Palo Alto Digital Library Project" },
                // PB018: the decal's full "Emerson's Restaurant Guide" carries a word the
                // spoken title ("Emerson's Guide") drops — the fuller-brand rule; the
                // "Street Smarts GIS" half is carried by the spoken subtitle.
                { "PB018", "Street Smarts GIS, Emerson's Restaurant Guide" },
                // PB020: the chip face engraved over the core host — its model line and
                // product name appear in no spoken string (the title carries only the brand),
                // screenshot-verified 2026-08-29. The "WONDERDISC" decal is a title
                // restatement and owes nothing.
                { "PB020", "Sawayama SYWDX85769D3, Reality Processor" },
                // PB023: the league's initialism mark (saltire + baseball art around three
                // letters) appears in no spoken string — the title carries only the long form.
                // A distinct lettered mark, not a title restatement (the PB012 numeral-mark
                // precedent); screenshot-verified 2026-08-30.
                { "PB023", "XLB" },
                // PB028: the same station backdrop as PB014 — "News Network" is lettering the
                // spoken title ("KGOG-TV") and subtitle ("Satellite Uplink") lack; grep-verified
                // absent from strings.csv, screenshot-verified 2026-09-06. The panel's own KGOG
                // mark restates the title and owes nothing.
                { "PB028", "KGOG News Network" },
                // PB033: the agency seal's lettering — the spoken title names only the
                // government, and no description/strings.csv text carries either line;
                // grep- and screenshot-verified 2026-09-20.
                { "PB033", "FEMA, Genetic Database" },
                // PB034 "UNKNOWN NETWORK 2": PB008's brand + department line again, and this
                // background bakes in a monitor whose screen is lettered too (a static
                // texture — textures/networks/unknown/background2 — nothing drives it);
                // screenshot-verified 2026-09-20.
                { "PB034", "НГТУ, ОТДЕЛ ПРИКЛАДНОЙ СЕМИОТИКИ, НЕ ПОДКЛЮЧЕН" },
                // PB053: the agency's initialism mark + system subline; the spoken title is
                // only the long form (the PB023 precedent). Screenshot-verified 2026-09-20.
                { "PB053", "MVA, Scheduling System" },
                // PB056: the seal's subline — the spoken title carries only the department.
                // Screenshot-verified 2026-09-20.
                { "PB056", "Department of Defense, USAF Secure Facility" },
                // PB057: the seal's monogram — a lettered mark the title never speaks (the
                // PB023 precedent); the ring restates the title and owes nothing. 2026-09-20.
                { "PB057", "EV" },
                // PB055: the GIS vendor's globe mark; the airline line restates the title.
                // Grep-verified absent from the game text, screenshot-verified 2026-09-20.
                { "PB055", "Globalyst" },
            };

        // GOAL-VIEW panel lettering: a special panel whose F1 rendering swaps its feed for a
        // LETTERED SPRITE (no text call for PanelCapture to record). Transcribed like
        // NetworkLogos (one texture set ships for all languages), spoken as the popup row
        // right after the captured panel caption. Keyed by puzzle id; no entry = no row.
        private static readonly System.Collections.Generic.Dictionary<string, string> GoalPanelLettering =
            new System.Collections.Generic.Dictionary<string, string>
            {
                // PB029B: the camera panel's goal-view frame, screenshot-verified 2026-09-20.
                // (Its live feed mirrors the spoken register plates; the same frame at run
                // time coincides with the spoken goal failure.)
                { "PB029B", "NO SIGNAL" },
                // PB056: the archive panel's frame texture bakes this in; a photo sprite
                // covers it once the logic's private bool_3 flips (PanelLetteringShown).
                { "PB056", "ACCESS DENIED" },
            };

        private static readonly FieldInfo ArchiveOpenField =
            Deobf.Field(typeof(SpecialPuzzleLogics.BonusHydroponix), "bool_3");

        /// <summary>False while a panel's baked lettering is painted over (PB056: the photo
        /// sprite drawn on top once the archive opens).</summary>
        private static bool PanelLetteringShown(GClass298 logic)
        {
            try
            {
                if (logic is SpecialPuzzleLogics.BonusHydroponix && ArchiveOpenField != null)
                    return !(bool)ArchiveOpenField.GetValue(logic);
            }
            catch { }
            return true;
        }

        // The GIS maps' COMPASS ROSE decal: four link-id plates around a star with a
        // drawn north arrow — the id→cardinal mapping the task text depends on ("move
        // east"), lettered only in the art. Ids in N,E,S,W order per puzzle;
        // screenshot-transcribed like NetworkLogos. No entry = no row.
        private static readonly System.Collections.Generic.Dictionary<string, int[]> CompassRoses =
            new System.Collections.Generic.Dictionary<string, int[]>
            {
                // PB018: N=800 (the drawn arrow sits on the 800 point), E=801, S=802, W=803.
                { "PB018", new[] { 800, 801, 802, 803 } },
                // PB021: same arrangement — the gold N sits on the 800 point (screen up-left),
                // then 801/802/803 clockwise; cross-checked against the grid's link axes,
                // screenshot-verified 2026-08-29.
                { "PB021", new[] { 800, 801, 802, 803 } },
                // PB055: the same rose (gold N on the 800 point, then clockwise) — and on this
                // map the rose is the ONLY place the ids are lettered; cross-checked against
                // the model (800 runs to the host drawn up-left), screenshot-verified 2026-09-20.
                { "PB055", new[] { 800, 801, 802, 803 } },
            };

        private static int[] CompassRose()
        {
            try
            {
                var meta = Meta(Editor);
                int[] ids;
                return meta != null && CompassRoses.TryGetValue(meta.string_0, out ids) ? ids : null;
            }
            catch { return null; }
        }

        private static string NetworkLogoText()
        {
            try
            {
                var meta = Meta(Editor);
                string text;
                return meta != null && NetworkLogos.TryGetValue(meta.string_0, out text) ? text : null;
            }
            catch { return null; }
        }

        // ---- the goal popup: everything the game's F1 view shows, as bare terse rows. Opening
        // forces the game's own show-goal flag (PanelCapture.ForceGoal — the screen visibly flips
        // to the F1 view, same as a sighted player holding the key) and arms the panel-text
        // capture; the open DEFERS two ticks so a full goal-view frame has been drawn and
        // published before the first row speaks. Enter/Backspace/Escape close. ----

        private bool _goalPopup;
        private int _goalPopupPending;
        private ControlId _goalReturnFocus;

        private void RequestGoalPopup()
        {
            if (_goalPopup || _goalPopupPending > 0) return;
            // F1 opens from anywhere — closing returns to wherever the user was.
            _goalReturnFocus = (Navigation.Active as GraphNavigator)?.FocusedNodeId;
            _goalPopupPending = 2;
            Patches.PanelCapture.SetArmed(true, forceGoal: true);
        }

        private void CloseGoalPopup()
        {
            _goalPopup = false;
            _goalPopupPending = 0;
            Patches.PanelCapture.SetArmed(false, false);
            Navigation.FocusNode(_goalReturnFocus ?? ControlId.Structural("ed.goalbtn"));
        }

        private bool BuildGoalPopup(GraphBuilder b)
        {
            var rows = GoalRowTexts(Editor);
            if (rows.Count == 0) return false;
            // Bare rows, no context, no position counts — the file-popup precedent (user rule).
            // Tables are raw-wired grids, so every vertical seam between rows is wired here
            // explicitly: each row's bottom edge goes Down to the next row's entry, the next
            // row's top edge goes Up to this row's exit.
            b.BeginStop("goalpop");
            var edges = new ColumnGrid.Edges[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Table != null)
                {
                    edges[i] = AddGoalTable(b, row.Table, "ed.gpop." + i);
                    continue;
                }
                if (row.Grid != null)
                {
                    edges[i] = AddGoalGrid(b, row.Grid, "ed.gpop." + i);
                    continue;
                }
                var id = ControlId.Structural("ed.gpop." + i);
                edges[i] = ColumnGrid.Edges.Single(id);
                b.AddItem(id, new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    SpeaksOwnPosition = true,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => row.Text, kind: AnnouncementKinds.Label),
                    },
                    // A goal-FILE row opens the same values popup a live file row does —
                    // every value past the row's 60-value cap browsable one per row.
                    OnActivate = row.Required >= 0
                        ? () => OpenGoalFilePopup(row.Host, row.Required, id)
                        : (Action)CloseGoalPopup,
                    OnSecondary = CloseGoalPopup,
                });
            }
            for (int i = 0; i + 1 < rows.Count; i++)
            {
                foreach (var from in edges[i].Bottom) b.Connect(from, GraphDir.Down, edges[i + 1].EnterTop);
                foreach (var from in edges[i + 1].Top) b.Connect(from, GraphDir.Up, edges[i].EnterBottom);
            }
            return true;
        }

        // ---- a captured panel TABLE as a ColumnGrid (user design, 2026-09-28): each column a
        // container labeled by its header, up/down within it, left/right across. Cells speak
        // bare like every popup row; a drawn-but-empty cell (the HDI-10 log pads its IN column
        // beside multi-value outputs) and an empty column say "N/A". A header-less column (the
        // DISC log's merged headers) is named by its ordinal. ----

        private ColumnGrid.Edges AddGoalTable(GraphBuilder b, PanelTable table, string key)
        {
            var columns = new System.Collections.Generic.List<ColumnGrid.Column>();
            for (int c = 0; c < table.Columns.Count; c++)
            {
                var source = table.Columns[c];
                string header = GameText.Speech(source.Header);
                if (string.IsNullOrWhiteSpace(header))
                    header = Loc.T("editor.goal.table.column", new { n = c + 1 });
                var column = new ColumnGrid.Column { Header = header, ContextId = ControlId.Structural(key + ".col" + c) };
                int count = Math.Max(1, source.Values.Count);
                for (int r = 0; r < count; r++)
                    column.Cells.Add(new ColumnGrid.Cell
                    {
                        Id = ControlId.Structural(key + "." + c + "." + r),
                        Vtable = ColumnGrid.TextCell(
                            r < source.Values.Count ? TableCellSpeech(source.Values[r]) : Loc.T("text.na"),
                            CloseGoalPopup, CloseGoalPopup),
                    });
                columns.Add(column);
            }
            ColumnGrid.Edges edges;
            ColumnGrid.Build(b, columns, out edges); // every column has a cell: always declares
            return edges;
        }

        // ---- a drawn cell GRID (the highway sign's target message, user design 2026-09-30):
        // row-major, every cell present (blanks included), up/down = same column in the next
        // row, left/right = along the row. Cells carry their own coordinates, so no contexts. ----

        private ColumnGrid.Edges AddGoalGrid(GraphBuilder b, string[][] grid, string key)
        {
            var ids = new ControlId[grid.Length][];
            for (int r = 0; r < grid.Length; r++)
            {
                ids[r] = new ControlId[grid[r].Length];
                for (int c = 0; c < grid[r].Length; c++)
                {
                    ids[r][c] = ControlId.Structural(key + "." + r + "." + c);
                    b.AddNode(ids[r][c], ColumnGrid.TextCell(grid[r][c], CloseGoalPopup, CloseGoalPopup));
                }
            }
            for (int r = 0; r < ids.Length; r++)
                for (int c = 0; c < ids[r].Length; c++)
                {
                    if (c > 0) b.Connect(ids[r][c], GraphDir.Left, ids[r][c - 1]);
                    if (c + 1 < ids[r].Length) b.Connect(ids[r][c], GraphDir.Right, ids[r][c + 1]);
                    if (r > 0) b.Connect(ids[r][c], GraphDir.Up, ids[r - 1][Math.Min(c, ids[r - 1].Length - 1)]);
                    if (r + 1 < ids.Length) b.Connect(ids[r][c], GraphDir.Down, ids[r + 1][Math.Min(c, ids[r + 1].Length - 1)]);
                }
            var last = ids[ids.Length - 1];
            return new ColumnGrid.Edges { Top = ids[0], Bottom = last, EnterTop = ids[0][0], EnterBottom = last[0] };
        }

        /// <summary>A table cell's words: empty = "N/A" (user wording for table cells — the
        /// comma-join rows say "blank"), a redaction bar its name, anything else raw.</summary>
        private static string TableCellSpeech(string value)
            => string.IsNullOrWhiteSpace(value) ? Loc.T("text.na") : ValueSpeech(value);

        /// <summary>One popup row: the text, and for goal-FILE rows the (host, required-file)
        /// indexes the values popup re-resolves by — the sim rebuilds every frame while
        /// editing, so nothing from it is cached.</summary>
        private struct GoalRow
        {
            public string Text;
            public int Host;
            public int Required; // -1 on every non-file row
            public PanelTable Table; // a captured panel table (Text unused) — built as a grid
            public string[][] Grid;  // a cell grid, row-major, cells fully composed (Text unused)

            public static GoalRow Plain(string text)
                => new GoalRow { Text = text, Host = -1, Required = -1 };
        }

        /// <summary>The popup's rows: required files (the model, structured), the register goal
        /// readouts and host statuses (the GClass298 virtual API — vmethod_9's consume arg stays
        /// false, a pure read; per-puzzle logics throw for registers they don't own), then the
        /// captured panel lines. All game text.</summary>
        private System.Collections.Generic.List<GoalRow> GoalRowTexts(EditorScreen e)
        {
            var rows = new System.Collections.Generic.List<GoalRow>();
            try
            {
                var sim = TheSim(e);
                if (sim == null) return rows;
                var logic = sim.method_43();
                for (int h = 0; h < sim.list_0.Count; h++)
                {
                    var host = sim.list_0[h];
                    // Hosts still hidden UNDER the goal view keep their secrets there too.
                    if (HostHidden(host, true)) continue;
                    for (int r = 0; r < host.list_0.Count; r++)
                    {
                        var required = host.list_0[r];
                        string id = required.maybe_0.method_0()
                            ? required.maybe_0.method_2().ToString() : GameText.T("NEW");
                        // A file in an off-team hand has no plate and no window on the map,
                        // and the F1 view windows it neither (its id never enters the window
                        // set) — but the F1 MAP still draws its goal GHOST as a card WITH the
                        // id plate at the holder's cell (the ghost is unheld, so the plate loop
                        // letters it; PB024's dropped weapon files, audit 2026-09-06). Speak
                        // exactly that: id + host, no values (on screen for no one) and no
                        // values popup.
                        if (required.maybe_0.method_0())
                        {
                            var live = FindFileAt(id, h);
                            if (live != null && HeldByOffTeam(live, HolderOf(live), e)
                                && !GoalViewWindows(sim, required))
                            {
                                rows.Add(GoalRow.Plain(Loc.T("editor.goal.file.plain", new
                                {
                                    id,
                                    host = GoalRowHost(logic, host),
                                })));
                                continue;
                            }
                        }
                        var values = new System.Collections.Generic.List<string>();
                        int total = required.exaValue_0.Length;
                        bool prose = IsProse(required.exaValue_0);
                        for (int i = 0; i < total && i < FileValuesSpoken; i++)
                        {
                            string t = required.exaValue_0[i].method_2(true);
                            values.Add(prose ? t : ValueSpeech(t));
                        }
                        string joined = JoinValueRows(values, total,
                            RequiredFileColumns(sim, required), prose);
                        rows.Add(new GoalRow
                        {
                            Text = Loc.T("editor.goal.file", new
                            {
                                id,
                                host = GoalRowHost(logic, host),
                                values = joined,
                            }),
                            Host = h,
                            Required = r,
                        });
                    }
                }
                if (logic != null)
                    foreach (var host in sim.list_0)
                    {
                        if (HostHidden(host, true)) continue;
                        foreach (var reg in host.list_2)
                        {
                            string id = RegName(reg);
                            string value = null;
                            if (reg.genum160_0 == (GEnum160)0) // the game only shows values on readable plates
                                try { value = logic.vmethod_9(reg, true, e.method_24(), false).method_2(true); }
                                catch { } // per-puzzle logics throw for registers they don't own
                            else if (reg.genum160_0 == (GEnum160)1)
                                value = Loc.T("editor.reg.writeonly");
                            rows.Add(GoalRow.Plain(string.IsNullOrEmpty(value)
                                ? Loc.T("editor.goal.register.plain", new { id, host = GoalRowHost(logic, host) })
                                : Loc.T("editor.goal.register", new { id, host = GoalRowHost(logic, host), value })));
                        }
                        try
                        {
                            // Prefix with the MAP-side name: HostName(goal:true) would resolve to
                            // this same override, reading "X: X".
                            var status = logic.vmethod_10(host, true);
                            if (status.method_0())
                                rows.Add(GoalRow.Plain(HostName(host) + ": " + GameText.Speech(status.method_2())));
                        }
                        catch { }
                    }
                AddHighwaySignRows(logic, rows);
                // PB053's board: the captured value is bare — its baked caption leads it.
                if (logic is SpecialPuzzleLogics.BonusNth && Patches.PanelCapture.Lines.Count > 0)
                    rows.Add(GoalRow.Plain(ServingBoardCaption));
                foreach (var line in Patches.PanelCapture.Lines)
                    rows.Add(line.Table != null
                        ? new GoalRow { Table = line.Table, Host = -1, Required = -1 }
                        : GoalRow.Plain(GameText.Speech(line.Text)));
                string art;
                var gmeta = Meta(e);
                if (gmeta != null && GoalPanelLettering.TryGetValue(gmeta.string_0, out art)
                    && PanelLetteringShown(logic))
                    rows.Add(GoalRow.Plain(art));
            }
            catch { }
            return rows;
        }

        /// <summary>The host a goal-popup row is "at". The goal-view name — except where the
        /// logic's goal-view vmethod_10 override replaces the lettering with STATUS text (the
        /// pagers' display strip shows the target message there): that text is its own
        /// "{host}: {status}" row, and using it as the location read "#DATA at {message}"
        /// (PB035B, audit 2026-09-20). Those rows keep the map-side name.</summary>
        private static string GoalRowHost(GClass298 logic, SimHost host)
        {
            try
            {
                if (logic != null && logic.vmethod_10(host, true).method_0()) return HostName(host);
            }
            catch { }
            return HostName(host, true);
        }

        /// <summary>True when the F1 view opens a WINDOW for this required file even though an
        /// off-team EXA holds the live one: the goal view builds its window set from the diff
        /// report, and a FileIsMissing entry adds the spec's EntityID (EditorScreen's Class107
        /// callbacks) — so the file's id AND values are on screen there (PB054's dropped
        /// inventories, audit 2026-09-20). Without such an entry only the ghost's id plate is
        /// drawn (PB024) and the row stays id-only. method_21 is the entry's own public
        /// tagged-union dispatch; every other case is a no-op here.</summary>
        private static bool GoalViewWindows(Sim sim, SimRequiredFile required)
        {
            bool found = false;
            try
            {
                foreach (var entry in sim.list_5)
                    entry.method_21(_ => { }, _ => { },
                        missing => { if (ReferenceEquals(missing.simRequiredFile_0, required)) found = true; },
                        _ => { }, _ => { }, _ => { }, _ => { });
            }
            catch { }
            return found;
        }

        /// <summary>PanelCapture's map-plane filter (see PanelCapture.IsHostName): is this
        /// string one of the current map's spoken host names?</summary>
        internal static bool IsSpokenHostName(string text)
        {
            try
            {
                var sim = TheSim(Editor);
                if (sim == null || string.IsNullOrWhiteSpace(text)) return false;
                foreach (var h in sim.list_0)
                    if (string.Equals(HostName(h), text.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        // The highway sign (SFCTA) draws its content as glyph SPRITES from a font atlas — no
        // text for PanelCapture to record — and in the goal view those glyphs render the TARGET
        // message. Read it from the model instead: HighwaySign.string_1, the sign as one flat
        // 3x9 string, laid out as the drawn cell grid (blank cells included). Cells speak
        // "{col}, {row}, {char}", 0-based — the numbers a #DATA write addresses, the same
        // words the editor's live sign grid speaks.
        private static readonly FieldInfo SignTargetField =
            Deobf.Field(typeof(SpecialPuzzleLogics.HighwaySign), "string_1");
        // The sign's geometry (9 columns x 3 rows, static readonly game-side) — drawn as a
        // countable cell grid in the DIGICAM FEED panel, so its dimensions are sighted-visible;
        // the leading row states them (columns are the 0..8 range a #DATA write addresses).
        private static readonly FieldInfo SignColsField =
            Deobf.Field(typeof(SpecialPuzzleLogics.HighwaySign), "int_0");
        private static readonly FieldInfo SignRowsField =
            Deobf.Field(typeof(SpecialPuzzleLogics.HighwaySign), "int_1");

        private static void AddHighwaySignRows(GClass298 logic, System.Collections.Generic.List<GoalRow> rows)
        {
            try
            {
                var sign = logic as SpecialPuzzleLogics.HighwaySign;
                if (sign == null || SignTargetField == null) return;
                var text = SignTargetField.GetValue(sign) as string;
                if (text == null) return;
                int cols = 9, signRows = 3;
                try { if (SignColsField != null) cols = (int)SignColsField.GetValue(null); } catch { }
                try { if (SignRowsField != null) signRows = (int)SignRowsField.GetValue(null); } catch { }
                if (text.Length < cols * signRows) return; // malformed target: no partial grid
                rows.Add(GoalRow.Plain(Loc.T("editor.goal.sign.size", new { rows = signRows, cols })));
                var grid = new string[signRows][];
                for (int row = 0; row < signRows; row++)
                {
                    grid[row] = new string[cols];
                    for (int col = 0; col < cols; col++)
                        grid[row][col] = Loc.T("editor.goal.sign.cell", new
                        { col, row, text = CharSpeech(text[row * cols + col].ToString()) });
                }
                rows.Add(new GoalRow { Grid = grid, Host = -1, Required = -1 });
            }
            catch { }
        }

        // Mirrors the checklist draw: label (+ " (n/m)" when the goal has progress), and the
        // tick/cross state — neutral until the sim has actually run a cycle.
        private static string GoalLabel(int index)
        {
            try
            {
                var e = Editor;
                var sim = TheSim(e);
                if (sim == null || index >= sim.list_2.Count) return null;
                var goal = sim.list_2[index];
                string label = GameText.Speech(goal.imethod_0());
                var state = goal.imethod_1(sim, sim.list_5);
                if (state.int_1 > 1) label = label + " (" + state.int_0 + "/" + state.int_1 + ")";
                return label;
            }
            catch { return null; }
        }

        private static string GoalState(int index)
        {
            try
            {
                var e = Editor;
                var sim = TheSim(e);
                if (sim == null || index >= sim.list_2.Count) return null;
                bool neutral = !e.method_0() || sim.method_52() < 1;
                var state = sim.list_2[index].imethod_1(sim, sim.list_5);
                if (state.genum152_0 == (GEnum152)0 || neutral) return null;
                return state.genum152_0 == (GEnum152)1 ? Loc.T("value.complete") : Loc.T("value.failed");
            }
            catch { return null; }
        }
    }
}

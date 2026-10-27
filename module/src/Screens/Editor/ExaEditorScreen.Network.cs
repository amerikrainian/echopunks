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
        // ---- network topology (task 6): a hosts stop (scroll over hosts, selection follows
        // focus) and a links stop listing the SELECTED host's outgoing links — "800, INBOX",
        // prefixed "One way" when the far side has no return id (the game's own marker: link ids
        // are drawn per side only where they exist). ----

        private int _selectedHost;

        // Maps whose style-0 (plate-less) link ids are lettered NOWHERE on screen — see BuildLinks.
        private static readonly System.Collections.Generic.HashSet<string> UnletteredStyle0Maps =
            new System.Collections.Generic.HashSet<string> { "PB058" };

        private void BuildHosts(GraphBuilder b, EditorScreen e)
        {
            var sim = TheSim(e);
            if (sim == null || sim.list_0.Count == 0) return;
            LandOnSelectedHost("hosts");
            b.BeginStop("hosts");
            b.PushContext(Loc.T("editor.hosts"));
            for (int i = 0; i < sim.list_0.Count; i++)
            {
                int index = i;
                b.AddItem(ControlId.Structural("ed.host." + i), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => HostName(HostAt(index)), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => HostOccupants(index), kind: AnnouncementKinds.Value),
                    },
                    Selected = () => _selectedHost == index,
                    OnSelect = () => SelectHost(index),
                });
            }
            b.PopContext();
        }

        // The ONE host-selection path (hosts rows, grid travel, link traversal): the host-scoped
        // stops rebuild from _selectedHost. A change from anywhere but grid travel ends the
        // grid's trail (Backspace would otherwise walk a route you are no longer on).
        private void SelectHost(int index, bool fromGrid = false)
        {
            if (!fromGrid && index != _selectedHost) _gridTrail.Clear();
            _selectedHost = index;
        }

        // The hosts and grid stops are two views of ONE selection, changed from three places —
        // so while focus is OUTSIDE such a stop it keeps no remembered row (memory beats Selected
        // in StopLanding) and entering it always lands on the selected host. Forgetting at
        // selection time wasn't enough: a link traversal's vanishing row sends the reconcile
        // fallback to the nearest survivor (the previous stop's last node) and REMEMBERS it there.
        private static void LandOnSelectedHost(string stopKey)
        {
            if (!Equals(Navigation.FocusedStopKey, stopKey)) Navigation.ForgetStop(stopKey);
        }

        // ---- the host grid: every host as a node; an arrow TRAVELS the link drawn leaving the
        // host that way and speaks it like its links row ("800, INBOX"). The frame is the map
        // grid's own, rotated 45 degrees off the isometric screen (the GIS compass rose's frame:
        // Up = +y = screen up-left, Right = +x = up-right, Down = down-right, Left = down-left).
        // Directions come from the link's DRAWN axis on every map, the compass maps included
        // (user decision, 2026-10-27 — the rose's lettering disagrees only on the home link,
        // 800 = "north" yet drawn running east). Several links one way open a chooser; Backspace
        // walks the trail of grid travels back. Selection is shared with the hosts stop, so the
        // links/files/registers stops follow the grid. ----

        // The grid's travel trail: the hosts each grid travel left, newest last. Any selection
        // change from elsewhere (hosts rows, a links-stop traversal) clears it.
        private readonly System.Collections.Generic.List<int> _gridTrail = new System.Collections.Generic.List<int>();

        private void BuildGrid(GraphBuilder b, EditorScreen e)
        {
            var sim = TheSim(e);
            if (sim == null || sim.list_0.Count == 0) return;

            LandOnSelectedHost("grid");
            b.BeginStop("grid");
            b.PushContext(Loc.T("editor.grid"));
            for (int i = 0; i < sim.list_0.Count; i++)
            {
                int index = i;
                b.AddNode(GridId(i), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => HostName(HostAt(index)), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => GridDirections(index), kind: AnnouncementKinds.Value),
                    },
                    Selected = () => _selectedHost == index,
                    OnSelect = () => SelectHost(index),
                    // No edges: every arrow resolves here, against the live links.
                    OnArrow = dir => GridArrow(index, dir),
                    OnSecondary = () => GridBack(index),
                });
            }
            b.PopContext();
        }

        private static ControlId GridId(int hostIndex) => ControlId.Structural("ed.grid." + hostIndex);

        private static readonly string[] GridDirKeys =
            { "editor.grid.dir.up", "editor.grid.dir.right", "editor.grid.dir.down", "editor.grid.dir.left" };

        // The ways out of a grid host — "up right left", space-joined (user spec) — the directions an arrow travels
        // from here (ids left to the travel itself; user spec, 2026-10-27). Null when none.
        private static string GridDirections(int hostIndex)
        {
            var links = LinksFrom(Editor, hostIndex);
            if (links == null) return null;
            var parts = new System.Collections.Generic.List<string>();
            for (int d = 0; d < 4; d++)
            {
                int dir = d;
                if (links.Exists(l => l.Dir == dir && l.Dest >= 0)) parts.Add(Loc.T(GridDirKeys[d]));
            }
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }

        // N,E,S,W — the LinkView.Dir order.
        private static int GridDirIndex(GraphDir dir)
        {
            switch (dir)
            {
                case GraphDir.Up: return 0;
                case GraphDir.Right: return 1;
                case GraphDir.Down: return 2;
                case GraphDir.Left: return 3;
                default: return -1;
            }
        }

        private bool GridArrow(int from, GraphDir dir)
        {
            int d = GridDirIndex(dir);
            var links = LinksFrom(Editor, from);
            if (d < 0 || links == null) return false;
            var candidates = links.FindAll(l => l.Dir == d && l.Dest >= 0);
            if (candidates.Count == 0) return false; // nothing drawn that way: the plain edge
            // In drawn order along the host's edge, read in the arrow frame: left to right for
            // Up/Down links, top to bottom for Left/Right (host.list_1 order is authoring order —
            // PB012's ATM row came out 806, 805, 804, 801, 800).
            candidates.Sort((x, y) => x.Along != y.Along ? x.Along.CompareTo(y.Along) : x.Index.CompareTo(y.Index));
            if (candidates.Count == 1)
            {
                GridTravel(from, candidates[0].Dest, candidates[0].Row);
                return true;
            }
            OpenGridChoice(from, d, candidates);
            return true;
        }

        private void GridTravel(int from, int to, string spoken)
        {
            _gridTrail.Add(from);
            GoToGridHost(to, spoken);
        }

        private void GoToGridHost(int to, string spoken)
        {
            SelectHost(to, fromGrid: true);
            Navigation.FocusNode(GridId(to), announce: false);
            string dirs = GridDirections(to);
            Speech.Tts.Speak(dirs == null ? spoken : spoken + ", " + dirs, interrupt: true);
        }

        // Backspace: back to the host the last grid travel left, whether or not a link leads
        // there (one-way links). Spoken like a travel when one does, else just the host name.
        private void GridBack(int at)
        {
            if (_gridTrail.Count == 0)
            {
                Speech.Tts.Speak(Loc.T("editor.grid.noback"), interrupt: true);
                return;
            }
            int prev = _gridTrail[_gridTrail.Count - 1];
            _gridTrail.RemoveAt(_gridTrail.Count - 1);
            string spoken = null;
            var links = LinksFrom(Editor, at);
            if (links != null)
            {
                var back = links.Find(l => l.Dest == prev);
                if (back != null) spoken = back.Row;
            }
            GoToGridHost(prev, spoken ?? HostName(HostAt(prev)));
        }

        // ---- the grid chooser: several links leave the host the pressed way (a wide host with
        // a row of links along one edge). A modal list of their link rows; Enter travels,
        // Backspace/Escape returns to the grid host. ----

        private int _gridChoiceFrom = -1, _gridChoiceDir = -1;
        private System.Collections.Generic.List<int> _gridChoiceLinks; // host.list_1 indexes

        private bool GridChoiceOpen => _gridChoiceLinks != null;

        private void OpenGridChoice(int from, int dir, System.Collections.Generic.List<LinkView> candidates)
        {
            _gridChoiceFrom = from;
            _gridChoiceDir = dir;
            _gridChoiceLinks = candidates.ConvertAll(l => l.Index);
            Navigation.ForgetStop("gridpop");
            Navigation.FocusStop("gridpop");
        }

        private void ClearGridChoice()
        {
            _gridChoiceLinks = null;
            _gridChoiceFrom = _gridChoiceDir = -1;
        }

        private void CancelGridChoice()
        {
            int from = _gridChoiceFrom;
            ClearGridChoice();
            if (from >= 0) Navigation.FocusNode(GridId(from));
        }

        private bool BuildGridChoice(GraphBuilder b)
        {
            int from = _gridChoiceFrom, dir = _gridChoiceDir;
            var links = LinksFrom(Editor, from);
            if (links == null) return false;
            // Re-resolved per build like every row: a link that stops existing (or turns) drops out.
            var rows = links.FindAll(l => _gridChoiceLinks.Contains(l.Index) && l.Dir == dir && l.Dest >= 0);
            if (rows.Count == 0) return false;
            b.BeginStop("gridpop");
            foreach (var row in rows)
            {
                var link = row;
                b.AddItem(ControlId.Structural("ed.gridpop." + link.Index), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[] { new NodeAnnouncement(() => link.Row, kind: AnnouncementKinds.Label) },
                    OnActivate = () =>
                    {
                        ClearGridChoice();
                        GridTravel(from, link.Dest, link.Row);
                    },
                    OnSecondary = CancelGridChoice,
                });
            }
            return true;
        }

        // Which side of `from` the host `to` is drawn on, in the grid frame — the dominant axis
        // of the centre-to-centre delta. Only the LAST resort of LinkDirection (every link
        // carries a drawn axis). Equal axes = -1 (no honest side to pick).
        private static int DrawnDirection(SimHost from, SimHost to)
        {
            try
            {
                Range2 a = from.range2_0, c = to.range2_0;
                // Doubled centres: Start + End, so odd sizes stay integral.
                int dx = (c.Start.int_0 + c.End.int_0) - (a.Start.int_0 + a.End.int_0);
                int dy = (c.Start.int_1 + c.End.int_1) - (a.Start.int_1 + a.End.int_1);
                if (Math.Abs(dx) == Math.Abs(dy)) return -1;
                if (Math.Abs(dy) > Math.Abs(dx)) return dy > 0 ? 0 : 2;
                return dx > 0 ? 1 : 3;
            }
            catch { return -1; }
        }

        // The way a link leaves `from` (0..3 = N,E,S,W), off the link's own drawn geometry. Links
        // are never diagonal (Sim's builders throw on one), so each has an axis: a TWO-cell link
        // carries its direction host_0 -> host_1 (GEnum177 1..4 = +x,+y,-x,-y — reversed from the
        // far end); a ONE-cell link (GEnum177 0 — the plate-less art-map links) carries only the
        // drawn bridge's axis (GEnum168 0 = x, 1 = y — EditorScreen's bridge draw and the move
        // animation both read it so), signed by where the far host sits along it.
        private static int LinkDirection(SimHostLink link, SimHost from, SimHost to)
        {
            try
            {
                int g = (int)link.genum177_0;
                if (g != 0)
                {
                    if (!link.method_0(from)) g = g > 2 ? g - 2 : g + 2;
                    switch (g)
                    {
                        case 1: return 1;
                        case 2: return 0;
                        case 3: return 3;
                        default: return 2;
                    }
                }
                Range2 a = from.range2_0, c = to.range2_0;
                int dx = (c.Start.int_0 + c.End.int_0) - (a.Start.int_0 + a.End.int_0);
                int dy = (c.Start.int_1 + c.End.int_1) - (a.Start.int_1 + a.End.int_1);
                int axis = (int)link.genum168_0;
                if (axis == 0 && dx != 0) return dx > 0 ? 1 : 3;
                if (axis == 1 && dy != 0) return dy > 0 ? 0 : 2;
            }
            catch { }
            return DrawnDirection(from, to);
        }

        // One link as seen FROM a host — exactly the links stop's rows (same gating, same words),
        // which the grid travels and speaks too.
        private sealed class LinkView
        {
            public int Index;   // in host.list_1
            public int Dest;    // sim.list_0 index of the far host (-1 = not a listed host)
            public int Dir;     // 0..3 = N,E,S,W (-1 = no honest side)
            public string Row;  // "One way, 800, INBOX, locked"
            public int Along;   // position along the host's edge, in reading order for Dir
        }

        private static System.Collections.Generic.List<LinkView> LinksFrom(EditorScreen e, int hostIndex)
        {
            var sim = e == null ? null : TheSim(e);
            if (sim == null || hostIndex < 0 || hostIndex >= sim.list_0.Count) return null;
            var host = sim.list_0[hostIndex];
            Team team;
            try { team = e.method_24(); }
            catch { return null; }

            var views = new System.Collections.Generic.List<LinkView>();
            for (int i = 0; i < host.list_1.Count; i++)
            {
                var link = host.list_1[i];
                Maybe<int> localId;
                try { localId = link.method_2(host).method_2(team); }
                catch { continue; }
                // A LOCKED link draws as a red line even with its ids cleared — a visible
                // "connection exists, currently closed". A linkless, unlocked side is nothing
                // from here when the FAR side carries the id (the one-way link is heard from
                // there) — but a connector blank at BOTH ends is drawn (a ramp with an empty
                // plate on each side) and would be heard from nowhere, so both sides speak
                // the empty plate as their id, the "blank" word empty drawn values already
                // use (PB028's terminal↔storage corridor, audit 2026-09-06).
                bool locked = false;
                try { locked = link.bool_0; } catch { }
                // PLATE-LESS links: the plate draw is gated on the link's plate style
                // (EditorScreen: method_3(cell) != 0 && id present) — a style-0 link draws its
                // connector with NO id plate at either end. But the artists use style 0 exactly
                // where something ELSE letters the ids: a LINK LEGEND decal with direction
                // arrows (PB016, PB027, PB029B, PB030), the compass rose (PB018, PB021, PB055),
                // or the game's own hardcoded mid-link lettering (Puzzles.puzzle_29 = PB019) —
                // there the ids are readable and stay spoken. Only a map verified to letter
                // them NOWHERE blanks them ("blank, destination", never "One way"): PB058,
                // whose internal bus ids are the puzzle's to discover from a file. All 48
                // puzzles swept 2026-09-20 (probe `model` prints each link's plate style); the
                // first cut blanked every style-0 link and silenced the legend maps.
                bool plateless = false;
                try
                {
                    plateless = (int)link.genum177_0 == 0
                        && UnletteredStyle0Maps.Contains(Meta(e)?.string_0 ?? "");
                }
                catch { }
                if (plateless && !locked) localId = GStruct10.gstruct10_0;
                SimHost dest;
                try { dest = link.method_1(host); }
                catch { continue; }
                bool farHasId = false;
                try { farHasId = link.method_2(dest).method_2(team).method_0(); } catch { }
                if (plateless && !locked) farHasId = false;
                bool bothBlank = !localId.method_0() && !farHasId && !locked;
                if (!localId.method_0() && !locked && !bothBlank) continue;
                // The flag's only DRAWN form is the red link tile of the flat-tile maps
                // (meta.bool_1 — EditorScreen's color2); on an art map a flagged link that
                // carries an id is lettered and traversed like any other (PB056's home link,
                // audit 2026-09-20), so "locked" there would describe nothing on screen. The
                // id-less flagged rows (the modems' undialed lines) keep the word.
                bool flatTiles = false;
                try { flatTiles = Meta(e).bool_1; } catch { }
                bool speakLocked = locked && (flatTiles || !localId.method_0());
                string idPart = localId.method_0() ? localId.method_2() + ", "
                    : bothBlank ? Loc.T("text.blank") + ", " : "";
                bool oneWay = localId.method_0() && !farHasId;
                int dir = LinkDirection(link, host, dest);
                int along = 0;
                try
                {
                    var cell = link.method_0(host) ? link.index2_0 : link.index2_1;
                    along = dir == 0 || dir == 2 ? cell.int_0 : -cell.int_1;
                }
                catch { }
                views.Add(new LinkView
                {
                    Index = i,
                    Dest = sim.list_0.IndexOf(dest),
                    Dir = dir,
                    Along = along,
                    Row = (oneWay ? Loc.T("editor.link.oneway") + ", " : "")
                        + idPart + HostName(dest)
                        + (speakLocked ? ", " + Loc.T("editor.link.locked") : ""),
                });
            }
            return views;
        }

        private void BuildLinks(GraphBuilder b, EditorScreen e)
        {
            var sim = TheSim(e);
            if (sim == null || _selectedHost >= sim.list_0.Count) return;
            var host = sim.list_0[_selectedHost];
            var links = LinksFrom(e, _selectedHost);
            if (links == null) return;

            b.BeginStop("links");
            b.PushContext(Loc.T("editor.links", new { host = HostName(host) }));
            foreach (var view in links)
            {
                var link = view;
                b.AddItem(ControlId.Structural("ed.link." + _selectedHost + "." + link.Index), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => link.Row, kind: AnnouncementKinds.Label),
                    },
                    // Enter = traverse, "as if you scrolled to the destination": it becomes the
                    // selected host AND the hosts stop's remembered row (stop landings prefer
                    // memory, so Shift+Tab must land on it), then focus re-parks on ITS links —
                    // that landing is the one the differ announces.
                    OnActivate = () =>
                    {
                        if (link.Dest < 0) return;
                        SelectHost(link.Dest);
                        Navigation.FocusNode(ControlId.Structural("ed.host." + link.Dest), announce: false);
                        Navigation.FocusStop("links");
                    },
                });
            }
            b.PopContext();
        }

        private static SimHost HostAt(int index)
        {
            try
            {
                var sim = TheSim(Editor);
                return sim != null && index < sim.list_0.Count ? sim.list_0[index] : null;
            }
            catch { return null; }
        }

        // Terse occupancy with capacity: "1 / 9, XA" — occupied cells against the host's usable
        // cells (a full host blocks entry; the game shows this only as the grid filling up).
        private static string HostOccupants(int index)
        {
            var host = HostAt(index);
            if (host == null) return null;
            // The cover box shows nothing inside — but its cover word ("LOCKED") is drawn, so
            // when the label carries the drawn name the caption speaks as the row's value.
            if (HostHidden(host, false)) return HiddenStateValue(host);
            try
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var entity in host.method_8())
                {
                    var exa = entity as SimExa;
                    if (exa != null)
                    {
                        // A holder is drawn WITH its file (the card in its hand) while the file
                        // itself leaves this list (held = not in method_8) — say so, or the card
                        // a sighted player sees on every scripted NPC is silent (PB024, 2026-08-30).
                        bool holding = false;
                        try { holding = exa.maybe_3.method_0(); } catch { }
                        parts.Add(holding
                            ? Loc.T("editor.exa.holding", new { exa = ExaDisplayName(exa) })
                            : ExaDisplayName(exa));
                        continue;
                    }
                    var file = entity as SimFile;
                    if (file != null) parts.Add(Loc.T("editor.file", new { id = FileId(file) }));
                }
                string capacity = null;
                try { capacity = parts.Count + " / " + host.method_0(); } catch { }
                // Hardware registers, after the count — their cells are already outside method_0's
                // capacity. Label first, the way the map stacks its badge over the plate ("CNS #NERV").
                foreach (var reg in host.list_2)
                {
                    string name = RegName(reg);
                    if (name != null) parts.Add(name);
                }
                if (parts.Count == 0) return capacity;
                return capacity == null
                    ? string.Join(", ", parts)
                    : capacity + ", " + string.Join(", ", parts);
            }
            catch { return null; }
        }

        /// <summary>A hardware register as spoken: the game's badge label when the puzzle set one,
        /// then the register name — "CNS #NERV". (Internal: the exec log's enemy-write rows
        /// name plates through it too.)</summary>
        internal static string RegName(GClass265 reg)
        {
            try
            {
                return reg.maybe_0.method_0()
                    ? reg.maybe_0.method_2() + " " + reg.string_0
                    : reg.string_0;
            }
            catch { return null; }
        }
    }
}

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
        // stops rebuild from _selectedHost.
        private void SelectHost(int index) => _selectedHost = index;

        // The hosts and grid stops are two views of ONE selection, changed from three places —
        // so while focus is OUTSIDE such a stop it keeps no remembered row (memory beats Selected
        // in StopLanding) and entering it always lands on the selected host. Forgetting at
        // selection time wasn't enough: a link traversal's vanishing row sends the reconcile
        // fallback to the nearest survivor (the previous stop's last node) and REMEMBERS it there.
        private static void LandOnSelectedHost(string stopKey)
        {
            if (!Equals(Navigation.FocusedStopKey, stopKey)) Navigation.ForgetStop(stopKey);
        }

        // ---- the GIS grid (compass maps only — those with a CompassRoses entry): every host as
        // a node, the arrows traveling the link lettered for that direction (Up = the rose's
        // north id, Right = east, Down = south, Left = west). Names only; selection follows focus,
        // so the links/files/registers stops follow the grid. ----

        // N,E,S,W — the CompassRoses id order.
        private static readonly GraphDir[] CompassDirs = { GraphDir.Up, GraphDir.Right, GraphDir.Down, GraphDir.Left };

        private void BuildGrid(GraphBuilder b, EditorScreen e)
        {
            var rose = CompassRose();
            var sim = TheSim(e);
            if (rose == null || sim == null || sim.list_0.Count == 0) return;
            Team team;
            try { team = e.method_24(); }
            catch { return; }

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
                    },
                    Selected = () => _selectedHost == index,
                    OnSelect = () => SelectHost(index),
                });
            }
            for (int i = 0; i < sim.list_0.Count; i++)
            {
                var host = sim.list_0[i];
                var slots = new int[] { -1, -1, -1, -1 };
                var unlettered = new System.Collections.Generic.List<SimHost>();
                foreach (var link in host.list_1)
                {
                    Maybe<int> id;
                    SimHost dest;
                    try
                    {
                        id = link.method_2(host).method_2(team);
                        dest = link.method_1(host);
                    }
                    catch { continue; }
                    if (!id.method_0()) continue; // no id on this side = not traversable from here
                    int d = Array.IndexOf(rose, id.method_2());
                    if (d < 0) { unlettered.Add(dest); continue; }
                    if (slots[d] < 0) slots[d] = sim.list_0.IndexOf(dest);
                }
                // Ids the rose doesn't letter (the home RETURN link, -1) take the side their host
                // is drawn on, if the lettered ids left it free (PB021's SJ0: 800/801/802 + -1 west).
                foreach (var dest in unlettered)
                {
                    int d = DrawnDirection(host, dest);
                    if (d >= 0 && slots[d] < 0) slots[d] = sim.list_0.IndexOf(dest);
                }
                for (int d = 0; d < 4; d++)
                    if (slots[d] >= 0) b.Connect(GridId(i), CompassDirs[d], GridId(slots[d]));
            }
            b.PopContext();
        }

        private static ControlId GridId(int hostIndex) => ControlId.Structural("ed.grid." + hostIndex);

        // Which side of `from` the host `to` is drawn on, in the map grid's own frame (the frame
        // the rose letters: +y north, +x east) — the dominant axis of the centre-to-centre delta.
        // NOT used for lettered ids: the home link is 800 (north) on all three compass maps yet
        // drawn running east. Equal axes = -1 (no honest side to pick).
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

        private void BuildLinks(GraphBuilder b, EditorScreen e)
        {
            var sim = TheSim(e);
            if (sim == null || _selectedHost >= sim.list_0.Count) return;
            var host = sim.list_0[_selectedHost];
            Team team;
            try { team = e.method_24(); }
            catch { return; }

            b.BeginStop("links");
            b.PushContext(Loc.T("editor.links", new { host = HostName(host) }));
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
                var dest = link.method_1(host);
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
                string other = HostName(dest);
                bool oneWay = localId.method_0() && !farHasId;
                int destIndex = sim.list_0.IndexOf(dest);
                b.AddItem(ControlId.Structural("ed.link." + _selectedHost + "." + i), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => (oneWay ? Loc.T("editor.link.oneway") + ", " : "")
                            + idPart + other
                            + (speakLocked ? ", " + Loc.T("editor.link.locked") : ""),
                            kind: AnnouncementKinds.Label),
                    },
                    // Enter = traverse, "as if you scrolled to the destination": it becomes the
                    // selected host AND the hosts stop's remembered row (stop landings prefer
                    // memory, so Shift+Tab must land on it), then focus re-parks on ITS links —
                    // that landing is the one the differ announces.
                    OnActivate = () =>
                    {
                        if (destIndex < 0) return;
                        SelectHost(destIndex);
                        Navigation.FocusNode(ControlId.Structural("ed.host." + destIndex), announce: false);
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

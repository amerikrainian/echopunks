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
        // ---- the highway sign as an explorable GRID (user design, 2026-08-22): a Tab stop
        // scoped to the SELECTED host like links/files/registers (user rule, 2026-08-22 —
        // host-scoped info always follows selection, never a general stop): present only when
        // the selected host owns the sign's registers. One node per cell, up/down = rows with
        // the column preserved, left/right = columns.
        // Cells speak "{col}, {row}" then the character (or "blank") — 0-based, the numbers a
        // #DATA write addresses (user rule, 2026-09-30: the coordinates are what a program
        // computes, so hearing them beats counting arrow presses). No counts. Values are the LIVE
        // sign (per-frame sim rebuild while editing = the initial road-work message;
        // persistent while armed = whatever the program has written so far), re-resolved on
        // every announce and declared Live, so the focused cell re-announces when a
        // #DATA/#CLRS write changes it — the hardware-register precedent. The game's drawn
        // row/column/character protocol status is deliberately NOT voiced (zine-documented;
        // user decision, 2026-08-22). ----

        private static readonly FieldInfo SignCellsField =
            Deobf.Field(typeof(SpecialPuzzleLogics.HighwaySign), "int_3");

        private void BuildSign(GraphBuilder b, EditorScreen e)
        {
            if (BuildServingBoard(b, e)) return;
            var sign = LiveSign(e);
            if (sign == null) return;
            var sim = TheSim(e);
            if (sim == null || _selectedHost >= sim.list_0.Count) return;
            var host = sim.list_0[_selectedHost];
            try { if (!host.list_2.Contains(sign.gclass265_0)) return; } catch { return; }
            if (HostHidden(host, false)) return; // hidden host = the sign is off the map too
            int cols = 9, signRows = 3;
            try { if (SignColsField != null) cols = (int)SignColsField.GetValue(null); } catch { }
            try { if (SignRowsField != null) signRows = (int)SignRowsField.GetValue(null); } catch { }
            b.BeginStop("sign");
            b.PushContext(Loc.T("editor.sign", new { host = HostName(host) }), positions: false);
            for (int r = 0; r < signRows; r++)
            {
                b.StartRow("sign"); // SHARED row key = column-preserving up/down (VerticalTarget)
                for (int c = 0; c < cols; c++)
                {
                    int cell = r * cols + c, row = r, col = c;
                    b.AddItem(ControlId.Structural("ed.sign." + r + "." + c), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        SpeaksOwnPosition = true, // coordinates replace counts (user rule)
                        Announcements = new[]
                        {
                            // Coordinates static, contents Live: a #DATA write re-speaks only
                            // the character, not the address.
                            new NodeAnnouncement(() => Loc.T("editor.sign.cell", new { col, row }),
                                kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => CellText(cell), live: true,
                                kind: AnnouncementKinds.Value),
                        },
                    });
                }
                b.EndRow();
            }
            b.PopContext();
        }

        // ---- PB053's queue board: the map letters a wall display over the clerk's counter
        // host — baked "NOW SERVING" art plus ONE live text value the logic draws every frame
        // (BonusNth.string_0, or the game's literal ERROR once bool_1 trips). Sighted players
        // read it off the map at any time, so it is a live node, not popup-only (this is
        // map state, not a zine-documented protocol status). Same host-scoped "sign" stop as
        // the highway sign, on the host whose cells the board stands in; the caption is a
        // language-invariant art transcription like NetworkLogos. Audit 2026-09-20. ----
        private const string ServingBoardCaption = "NOW SERVING";

        private bool BuildServingBoard(GraphBuilder b, EditorScreen e)
        {
            var sim = TheSim(e);
            if (sim == null || !(sim.method_43() is SpecialPuzzleLogics.BonusNth)) return false;
            if (_selectedHost >= sim.list_0.Count) return true;
            var host = sim.list_0[_selectedHost];
            if (host.string_0 != "private" || HostHidden(host, false)) return true;
            b.BeginStop("sign");
            b.PushContext(Loc.T("editor.sign", new { host = HostName(host) }), positions: false);
            b.AddItem(ControlId.Structural("ed.board"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                SpeaksOwnPosition = true,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => ServingBoardCaption, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(ServingBoardValue, live: true, kind: AnnouncementKinds.Value),
                },
            });
            b.PopContext();
            return true;
        }

        private static string ServingBoardValue()
        {
            try
            {
                var logic = TheSim(Editor)?.method_43() as SpecialPuzzleLogics.BonusNth;
                if (logic == null) return null;
                return logic.bool_1 ? "ERROR" : logic.string_0; // the draw's own ternary
            }
            catch { return null; }
        }

        /// <summary>Re-resolved per call — the edit-time per-frame sim rebuild replaces the
        /// HighwaySign instance (and its cell array) constantly; captured instances go stale.</summary>
        private static SpecialPuzzleLogics.HighwaySign LiveSign(EditorScreen e)
        {
            try { return TheSim(e)?.method_43() as SpecialPuzzleLogics.HighwaySign; }
            catch { return null; }
        }

        private static string CellText(int cell)
        {
            try
            {
                var sign = LiveSign(Editor);
                if (sign == null || SignCellsField == null) return null;
                var cells = SignCellsField.GetValue(sign) as int[];
                if (cells == null || cell >= cells.Length) return null;
                var font = SpecialPuzzleLogics.HighwaySign.string_0;
                return CharSpeech(font[Math.Max(0, Math.Min(cells[cell], font.Length - 1))]);
            }
            catch { return null; }
        }

        // The sign font is " A-Z 0-9 . ? !" — letters/digits speak as-is; the space cell and
        // lone punctuation get speakable names (a bare "." is silence to most TTS voices).
        private static string CharSpeech(string ch)
        {
            switch (ch)
            {
                case " ": return Loc.T("text.blank");
                case ".": return Loc.T("text.dot");
                case "?": return Loc.T("text.question");
                case "!": return Loc.T("text.exclamation");
                default: return ch;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Echopunks.UI.Graph;

namespace Echopunks.UI
{
    /// <summary>
    /// Side-by-side COLUMNS as one raw-wired grid (user design, 2026-09-28): each column is a
    /// context labeled by its header — the path-diff announcer speaks it whenever focus crosses
    /// into the column — holding its cells top-down; up/down walk a column, left/right cross to
    /// the same row of the neighbouring column (clamped when it is shorter). Used by the goal
    /// popup's panel tables and the histogram panels (Cycles / Size / Activity).
    /// </summary>
    internal static class ColumnGrid
    {
        internal sealed class Column
        {
            public string Header;
            /// <summary>Explicit context id: columns can share a header text, and the announcer
            /// diffs parent chains by id.</summary>
            public ControlId ContextId;
            public List<Cell> Cells = new List<Cell>();
        }

        internal struct Cell
        {
            public ControlId Id;
            public NodeVtable Vtable;
        }

        /// <summary>A block's vertical seams: the nodes whose Up/Down leave it, and where
        /// arriving from above/below lands (the first column's top/bottom).</summary>
        internal struct Edges
        {
            public ControlId[] Top, Bottom;
            public ControlId EnterTop, EnterBottom;

            public static Edges Single(ControlId id)
                => new Edges { Top = new[] { id }, Bottom = new[] { id }, EnterTop = id, EnterBottom = id };
        }

        /// <summary>Declares the grid into the current stop. Columns without cells are skipped;
        /// returns false (nothing declared) when none has any. <paramref name="tableRole"/>, when
        /// given, wraps the columns in one label-less container carrying that role word: the
        /// path-diff announcer speaks it once on the way in and never between its columns, so a
        /// landing reads "table, {header}, {cell}" and says left/right is there to take.</summary>
        public static bool Build(GraphBuilder b, IList<Column> columns, out Edges edges,
            string tableRole = null, ControlId tableId = null)
        {
            edges = default(Edges);
            var cols = new List<Column>();
            foreach (var c in columns) if (c != null && c.Cells.Count > 0) cols.Add(c);
            if (cols.Count == 0) return false;

            bool table = !string.IsNullOrEmpty(tableRole);
            if (table) b.PushContext(null, tableRole, positions: false, id: tableId);
            var ids = new ControlId[cols.Count][];
            for (int c = 0; c < cols.Count; c++)
            {
                b.PushContext(cols[c].Header, positions: false, id: cols[c].ContextId);
                ids[c] = new ControlId[cols[c].Cells.Count];
                for (int r = 0; r < cols[c].Cells.Count; r++)
                {
                    var cell = cols[c].Cells[r];
                    ids[c][r] = cell.Id;
                    b.AddNode(cell.Id, cell.Vtable);
                }
                b.PopContext();
            }
            if (table) b.PopContext();

            var top = new ControlId[cols.Count];
            var bottom = new ControlId[cols.Count];
            for (int c = 0; c < cols.Count; c++)
            {
                var col = ids[c];
                top[c] = col[0];
                bottom[c] = col[col.Length - 1];
                for (int r = 0; r < col.Length; r++)
                {
                    if (r > 0) b.Connect(col[r], GraphDir.Up, col[r - 1]);
                    if (r + 1 < col.Length) b.Connect(col[r], GraphDir.Down, col[r + 1]);
                    if (c > 0) b.Connect(col[r], GraphDir.Left, ids[c - 1][Math.Min(r, ids[c - 1].Length - 1)]);
                    if (c + 1 < cols.Count)
                        b.Connect(col[r], GraphDir.Right, ids[c + 1][Math.Min(r, ids[c + 1].Length - 1)]);
                }
            }
            edges = new Edges { Top = top, Bottom = bottom, EnterTop = top[0], EnterBottom = bottom[0] };
            return true;
        }

        /// <summary>A bare read-only text cell (no role, no position — grid cells never count).</summary>
        public static NodeVtable TextCell(string text, Action activate = null, Action secondary = null)
            => new NodeVtable
            {
                ControlType = ControlTypes.Text,
                SpeaksOwnPosition = true,
                Announcements = new[] { new NodeAnnouncement(() => text, kind: AnnouncementKinds.Label) },
                OnActivate = activate,
                OnSecondary = secondary,
            };
    }
}

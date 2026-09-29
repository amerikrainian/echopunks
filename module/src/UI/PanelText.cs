using System.Collections.Generic;

namespace Echopunks.UI
{
    /// <summary>One string the game drew, with its position. BCL-pure (unit-tested) — the capture
    /// patch translates the game's Vector2 to bare floats before anything reaches this file.</summary>
    internal struct PanelCell
    {
        public string Text;
        public float X;
        public float Y;
        /// <summary>Set on a whole TABLE recorded structurally (the game's shared table draw):
        /// the cell stands at the header row's left edge and reads as one table line.</summary>
        public PanelTable Table;
        /// <summary>Horizontal extent right of X (a table's column span) — block splitting
        /// measures gaps from the right edge. 0 for text.</summary>
        public float Width;

        public PanelCell(string text, float x, float y)
        {
            Text = text;
            X = x;
            Y = y;
            Table = null;
            Width = 0f;
        }

        public PanelCell(PanelTable table, float x, float y, float width)
        {
            Text = null;
            X = x;
            Y = y;
            Table = table;
            Width = width;
        }
    }

    /// <summary>One drawn table column: its header text (may be empty — the DISC log's merged
    /// headers) and its values top-down as drawn (an empty string is a drawn-but-empty cell).</summary>
    internal sealed class PanelColumn
    {
        public string Header;
        public List<string> Values = new List<string>();
    }

    internal sealed class PanelTable
    {
        public List<PanelColumn> Columns = new List<PanelColumn>();

        /// <summary>Content identity, for collapsing a table drawn twice between two ticks.</summary>
        public string Signature()
        {
            var parts = new List<string>();
            foreach (var c in Columns) parts.Add((c.Header ?? "") + "\u0001" + string.Join("\u0002", c.Values));
            return string.Join("\u0003", parts);
        }
    }

    /// <summary>One reading-order line of a panel: plain text, or a whole table.</summary>
    internal sealed class PanelLine
    {
        public string Text;
        public PanelTable Table;

        public static PanelLine Of(string text) => new PanelLine { Text = text };
        public static PanelLine Of(PanelTable table) => new PanelLine { Table = table };
    }

    /// <summary>
    /// Reconstructs reading order from positioned text: the special-puzzle panels (I/O logs, status
    /// readouts) are drawn as loose strings, but their layout is a grid — table rows share a y,
    /// columns an x. One special-logic draw can also paint several spatially DISTINCT elements
    /// (PB020: the track-request table AND the disc code strip ~1700 units to its left), so cells
    /// first split into BLOCKS on large x-projection gaps — y-clustering across unrelated elements
    /// shuffled their cells into one spoken row. Blocks read top-down (game coordinates are y-up),
    /// ties left-to-right; within a block, cells cluster into rows by y descending, cells within a
    /// row read left to right joined with ", ", and a multi-line string becomes one row per line.
    /// Exact repeats collapse (a panel drawn twice between two of our ticks must not double its
    /// rows). TABLES arrive whole (PanelCapture records the game's shared table draw
    /// structurally; its cells never reach the text path): one cell at the header row that
    /// becomes its own table line, breaking any text run it shares a row with.
    /// </summary>
    internal static class PanelText
    {
        /// <summary>Same-row cells CHAIN while consecutive y's stay within this: table cells share
        /// a y exactly and rows step 41.5 apart (never chain); PB020's disc code strip is drawn
        /// diagonally, one glyph per call stepping 14.17 down — chaining keeps it one spoken row.</summary>
        private const float RowTolerance = 15f;

        /// <summary>A horizontal gap this wide splits cells into separate blocks. Table columns
        /// step at most ~206 apart, so any one table stays whole.</summary>
        private const float BlockGap = 600f;

        public static List<PanelLine> Assemble(IEnumerable<PanelCell> cells)
        {
            var kept = new List<PanelCell>();
            var seen = new HashSet<string>();
            foreach (var cell in cells)
            {
                string content;
                if (cell.Table != null)
                {
                    if (cell.Table.Columns.Count == 0) continue;
                    content = "\u0001table\u0001" + cell.Table.Signature();
                }
                else if (string.IsNullOrWhiteSpace(cell.Text)) continue;
                else content = cell.Text;
                if (!seen.Add(content + "\n" + cell.X + "\n" + cell.Y)) continue;
                kept.Add(cell);
            }

            var lines = new List<PanelLine>();
            foreach (var block in SplitBlocks(kept)) AssembleBlock(block, lines);
            return lines;
        }

        private static List<List<PanelCell>> SplitBlocks(List<PanelCell> cells)
        {
            var blocks = new List<List<PanelCell>>();
            if (cells.Count == 0) return blocks;
            var byX = new List<PanelCell>(cells);
            byX.Sort((a, b) => a.X.CompareTo(b.X));
            var current = new List<PanelCell> { byX[0] };
            float right = byX[0].X + byX[0].Width;
            for (int i = 1; i < byX.Count; i++)
            {
                if (byX[i].X - right > BlockGap)
                {
                    blocks.Add(current);
                    current = new List<PanelCell>();
                }
                current.Add(byX[i]);
                right = System.Math.Max(right, byX[i].X + byX[i].Width);
            }
            blocks.Add(current);
            blocks.Sort((a, b) =>
            {
                float ay = MaxY(a), by = MaxY(b);
                return ay != by ? by.CompareTo(ay) : MinX(a).CompareTo(MinX(b));
            });
            return blocks;
        }

        private static float MaxY(List<PanelCell> block)
        {
            float m = block[0].Y;
            foreach (var c in block) if (c.Y > m) m = c.Y;
            return m;
        }

        private static float MinX(List<PanelCell> block)
        {
            float m = block[0].X;
            foreach (var c in block) if (c.X < m) m = c.X;
            return m;
        }

        private static void AssembleBlock(List<PanelCell> kept, List<PanelLine> lines)
        {
            kept.Sort((a, b) => a.Y != b.Y ? b.Y.CompareTo(a.Y) : a.X.CompareTo(b.X));
            for (int i = 0; i < kept.Count;)
            {
                int end = i + 1;
                while (end < kept.Count && kept[end - 1].Y - kept[end].Y <= RowTolerance) end++;
                var row = kept.GetRange(i, end - i);
                row.Sort((a, b) => a.X.CompareTo(b.X));
                // Text runs join left to right; a table ends the run and stands alone.
                var texts = new List<string>();
                foreach (var cell in row)
                {
                    if (cell.Table == null)
                    {
                        texts.Add(cell.Text.Trim());
                        continue;
                    }
                    FlushText(texts, lines);
                    lines.Add(PanelLine.Of(cell.Table));
                }
                FlushText(texts, lines);
                i = end;
            }
        }

        private static void FlushText(List<string> texts, List<PanelLine> lines)
        {
            if (texts.Count == 0) return;
            foreach (var line in string.Join(", ", texts).Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) lines.Add(PanelLine.Of(trimmed));
            }
            texts.Clear();
        }
    }
}

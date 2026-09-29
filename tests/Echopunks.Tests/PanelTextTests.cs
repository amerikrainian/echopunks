using System.Collections.Generic;
using Echopunks.UI;
using Xunit;

namespace Echopunks.Tests
{
    /// <summary>
    /// The panel-text assembler over captured draw calls. Geometry mirrors the game's special-puzzle
    /// panels: a title line on top, a header row, then table rows stepping 41.5 down (y-up
    /// coordinates), columns at fixed x offsets.
    /// </summary>
    public class PanelTextTests
    {
        [Fact]
        public void TwoColumnTableReadsTopDownRowMajor()
        {
            // The HDI-10 I/O LOG shape, deliberately shuffled — assembly must not depend on draw order.
            var cells = new List<PanelCell>
            {
                new PanelCell("-73", 3329f, 2031f),
                new PanelCell("IN (CNS)", 3329f, 2081f),
                new PanelCell("HDI-10 I/O LOG", 3227f, 2155f),
                new PanelCell("50", 3535f, 1989.5f),
                new PanelCell("OUT (ARM)", 3535f, 2081f),
                new PanelCell("-73", 3535f, 2031f),
                new PanelCell("477", 3329f, 1989.5f),
            };
            Assert.Equal(new[]
            {
                "HDI-10 I/O LOG",
                "IN (CNS), OUT (ARM)",
                "-73, -73",
                "477, 50",
            }, Texts(PanelText.Assemble(cells)));
        }

        [Fact]
        public void MultiLineStringBecomesOneRowPerLine()
        {
            // The UPLINK STATUS panel draws its whole readout as a single '\n'-joined string.
            var cells = new List<PanelCell> { new PanelCell("AZIM: 23°\nELEV: 45°\nLINK: LOCKED", 100f, 500f) };
            Assert.Equal(new[] { "AZIM: 23°", "ELEV: 45°", "LINK: LOCKED" }, Texts(PanelText.Assemble(cells)));
        }

        [Fact]
        public void ExactRepeatsCollapse()
        {
            // A panel drawn twice between two of our ticks must not double its rows.
            var cells = new List<PanelCell>
            {
                new PanelCell("300", 10f, 50f),
                new PanelCell("300", 10f, 50f),
                new PanelCell("300", 10f, 8.5f), // same VALUE on the next row stays
            };
            Assert.Equal(new[] { "300", "300" }, Texts(PanelText.Assemble(cells)));
        }

        [Fact]
        public void NearYSharesARowFarYDoesNot()
        {
            var cells = new List<PanelCell>
            {
                new PanelCell("header", 200f, 96f), // headers sit 8 above their column, same row
                new PanelCell("label", 10f, 100f),
                new PanelCell("next row", 10f, 58.5f),
            };
            Assert.Equal(new[] { "label, header", "next row" }, Texts(PanelText.Assemble(cells)));
        }

        [Fact]
        public void BlankCellsAndBlankLinesDrop()
        {
            // Merged header rows carry empty LocStrings (the DISC log's unnamed columns).
            var cells = new List<PanelCell>
            {
                new PanelCell("", 10f, 100f),
                new PanelCell("  ", 50f, 100f),
                new PanelCell("TRACK REQUESTS", 90f, 100f),
                new PanelCell(" \n ", 10f, 50f),
            };
            Assert.Equal(new[] { "TRACK REQUESTS" }, Texts(PanelText.Assemble(cells)));
        }

        [Fact]
        public void UnequalColumnsLeaveShortRowsSingleCelled()
        {
            // The live (unforced) view: IN revealed further than OUT has been written.
            var cells = new List<PanelCell>
            {
                new PanelCell("-73", 0f, 100f),
                new PanelCell("-73", 206f, 100f),
                new PanelCell("477", 0f, 58.5f),
            };
            Assert.Equal(new[] { "-73, -73", "477" }, Texts(PanelText.Assemble(cells)));
        }

        [Fact]
        public void FarApartElementsSplitIntoBlocks()
        {
            // The PB020 goal view: the track-request table at x~3380 and the disc code strip
            // ~1700 units left of it, overlapping in y. Without block segmentation the strip's
            // glyphs shuffled into the table's rows ("9, 201, OK").
            var cells = new List<PanelCell>
            {
                new PanelCell("TRACK REQUESTS", 3466f, 2082f),
                new PanelCell("209", 3382f, 2031f),
                new PanelCell("OK", 3556f, 2031f),
                new PanelCell("204", 3382f, 1989.5f),
                new PanelCell("OK", 3556f, 1989.5f),
                new PanelCell("8", 1254f, 2031f),  // strip glyphs share the table's y range
                new PanelCell("0", 1278.5f, 2016.8f),
                new PanelCell("3", 1303f, 2002.7f),
            };
            Assert.Equal(new[]
            {
                "TRACK REQUESTS",
                "209, OK",
                "204, OK",
                "8, 0, 3",
            }, Texts(PanelText.Assemble(cells)));
        }

        [Fact]
        public void DiagonalGlyphRunChainsIntoOneRow()
        {
            // The disc code strip draws one glyph per call along an isometric diagonal
            // (x +24.5, y -14.17 each) — consecutive-cell chaining keeps it one spoken row,
            // while 41.5-stepped table rows stay separate.
            var cells = new List<PanelCell>();
            for (int i = 0; i < 15; i++)
                cells.Add(new PanelCell(i % 2 == 0 ? "7" : "-", 1254f + 24.5f * i, 1670f - 14.17f * i));
            Assert.Equal(
                new[] { "7, -, 7, -, 7, -, 7, -, 7, -, 7, -, 7, -, 7" },
                Texts(PanelText.Assemble(cells)));
        }

        [Fact]
        public void BlocksReadTopDownByTheirTopmostCell()
        {
            // The table's title sits above the strip's topmost glyph, so the table block
            // reads first even though the strip is further left.
            var cells = new List<PanelCell>
            {
                new PanelCell("strip", 1254f, 1670f),
                new PanelCell("TITLE", 3294f, 2153f),
                new PanelCell("row", 3382f, 2031f),
            };
            Assert.Equal(new[] { "TITLE", "row", "strip" }, Texts(PanelText.Assemble(cells)));
        }

        [Fact]
        public void TableReadsAsOneLineInReadingOrder()
        {
            // The HDI-10 goal view: title above, the table recorded whole at its header row,
            // blank IN cells kept (they pad the rows of multi-value outputs).
            var table = Table(
                Column("IN (CNS)", "-42", ""),
                Column("OUT (SA-N)", "40", "-70"),
                Column("OUT (AV-N)", "-70", "40"));
            var cells = new List<PanelCell>
            {
                new PanelCell(table, 3128f, 2080f, 406f),
                new PanelCell("HDI-10 I/O LOG", 3026f, 2154f),
            };
            var lines = PanelText.Assemble(cells);
            Assert.Equal(2, lines.Count);
            Assert.Equal("HDI-10 I/O LOG", lines[0].Text);
            Assert.Same(table, lines[1].Table);
            Assert.Equal(new[] { "-42", "" }, lines[1].Table.Columns[0].Values);
        }

        [Fact]
        public void TableBreaksATextRunItSharesARowWith()
        {
            var table = Table(Column("A", "1"));
            var cells = new List<PanelCell>
            {
                new PanelCell("left", 0f, 100f),
                new PanelCell(table, 100f, 100f, 0f),
                new PanelCell("right", 300f, 100f),
            };
            var lines = PanelText.Assemble(cells);
            Assert.Equal(3, lines.Count);
            Assert.Equal("left", lines[0].Text);
            Assert.Same(table, lines[1].Table);
            Assert.Equal("right", lines[2].Text);
        }

        [Fact]
        public void RepeatedTableCollapses()
        {
            var cells = new List<PanelCell>
            {
                new PanelCell(Table(Column("A", "1", "2")), 10f, 100f, 0f),
                new PanelCell(Table(Column("A", "1", "2")), 10f, 100f, 0f),
            };
            Assert.Single(PanelText.Assemble(cells));
        }

        [Fact]
        public void BlockGapMeasuresFromTheTableRightEdge()
        {
            // Text 700 right of the table's anchor but 100 past its last column stays in the
            // table's block (above the table, so it reads first).
            var cells = new List<PanelCell>
            {
                new PanelCell(Table(Column("A", "1"), Column("B", "2")), 0f, 100f, 600f),
                new PanelCell("caption", 700f, 200f),
                new PanelCell("far", 2000f, 300f),
            };
            var lines = PanelText.Assemble(cells);
            Assert.Equal("far", lines[0].Text);
            Assert.Equal("caption", lines[1].Text);
            Assert.NotNull(lines[2].Table);
        }

        private static List<string> Texts(List<PanelLine> lines)
        {
            var texts = new List<string>();
            foreach (var line in lines) texts.Add(line.Text);
            return texts;
        }

        private static PanelColumn Column(string header, params string[] values)
            => new PanelColumn { Header = header, Values = new List<string>(values) };

        private static PanelTable Table(params PanelColumn[] columns)
            => new PanelTable { Columns = new List<PanelColumn>(columns) };
    }
}

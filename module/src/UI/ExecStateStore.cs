using System;
using System.Collections.Generic;

namespace Echopunks.UI
{
    /// <summary>
    /// The execution log's STATE COLUMNS (2026-10-27): what each player EXA's registers held as
    /// an instruction was dispatched (captured BEFORE it runs), stored as fixed-stride int
    /// records in chunked arrays — no per-entry objects, so the GC never scans them, and no text
    /// is built until a row is actually shown. A record is appended only when the EXA's state
    /// DIFFERS from that EXA's previous record; otherwise the entry points at the old one —
    /// blocked and waiting EXAs (most of a runaway run) cost nothing past the log's own pointer.
    ///
    /// Field encoding: a plain int is a number; <see cref="Absent"/> = nothing there ("None");
    /// <see cref="End"/> = a file cursor past the last value; anything from
    /// <see cref="Text(string)"/> = an interned string (keywords, host names, file ids). BCL-pure.
    /// </summary>
    internal sealed class ExecStateStore
    {
        public const int Absent = int.MinValue;
        public const int End = int.MinValue + 1;
        private const int TextBase = int.MinValue + 2;
        private const int TextLimit = TextBase + (1 << 30); // numbers below this can't occur

        private const int ChunkBits = 14; // 64 KB chunks, under the LOH threshold
        private const int ChunkSize = 1 << ChunkBits;
        private const int ChunkMask = ChunkSize - 1;

        private List<string> _strings = new List<string>();
        private Dictionary<string, int> _ids = new Dictionary<string, int>(StringComparer.Ordinal);
        private List<int[]> _chunks = new List<int[]>();
        private int _length; // ints in use
        private readonly Dictionary<int, int> _last = new Dictionary<int, int>(); // owner -> its newest record

        /// <summary>Ints per record; 0 until <see cref="Reset"/> sets a layout.</summary>
        public int Stride { get; private set; }

        /// <summary>Records held.</summary>
        public int Count { get; private set; }

        public void Reset(int stride)
        {
            Stride = stride > 0 ? stride : 0;
            Count = 0;
            _length = 0;
            _chunks = new List<int[]>();
            _strings = new List<string>();
            _ids = new Dictionary<string, int>(StringComparer.Ordinal);
            _last.Clear();
        }

        /// <summary>Encodes a string field (null = <see cref="Absent"/>).</summary>
        public int Text(string s)
        {
            if (s == null) return Absent;
            int id;
            if (!_ids.TryGetValue(s, out id))
            {
                id = _strings.Count;
                _strings.Add(s);
                _ids[s] = id;
            }
            return TextBase + id;
        }

        public static bool IsText(int v) => v >= TextBase && v < TextLimit;

        public static bool IsNumber(int v) => v >= TextLimit;

        /// <summary>The string an encoded text field holds, else null.</summary>
        public string TextOf(int v) => IsText(v) ? _strings[v - TextBase] : null;

        /// <summary>The record for <paramref name="owner"/>'s state <paramref name="fields"/>
        /// (exactly <see cref="Stride"/> ints): the owner's previous record when nothing changed,
        /// else a newly appended one. -1 when no layout is set.</summary>
        public int Record(int owner, int[] fields)
        {
            if (Stride == 0 || fields == null || fields.Length != Stride) return -1;
            int last;
            if (_last.TryGetValue(owner, out last) && Same(last, fields)) return last;
            int r = Append(fields);
            _last[owner] = r;
            return r;
        }

        /// <summary>Owners are only meaningful within one sim (entity numbers restart with each
        /// test run): forget them, so the next record of each starts fresh.</summary>
        public void ForgetOwners() => _last.Clear();

        public int Field(int record, int field)
        {
            if ((uint)record >= (uint)Count || (uint)field >= (uint)Stride) return Absent;
            int p = record * Stride + field;
            return _chunks[p >> ChunkBits][p & ChunkMask];
        }

        /// <summary>After the log shed old entries: keep only the records the survivors still
        /// point at, renumbered in first-reference order. <paramref name="remapAll"/> runs the
        /// given map over every surviving tag (GroupedLog.RemapTags). Strings stay — the table
        /// is bounded by the program and puzzle data, not by run length.</summary>
        public void Compact(Action<Func<int, int>> remapAll)
        {
            if (Stride == 0) return;
            var oldChunks = _chunks;
            int oldCount = Count;
            var remap = new Dictionary<int, int>();
            _chunks = new List<int[]>();
            _length = 0;
            Count = 0;
            var buf = new int[Stride];
            remapAll(old =>
            {
                if (old < 0 || old >= oldCount) return -1;
                int nu;
                if (!remap.TryGetValue(old, out nu))
                {
                    for (int f = 0; f < Stride; f++)
                    {
                        int p = old * Stride + f;
                        buf[f] = oldChunks[p >> ChunkBits][p & ChunkMask];
                    }
                    remap[old] = nu = Append(buf);
                }
                return nu;
            });
            _last.Clear(); // the next record of each owner starts fresh
        }

        private int Append(int[] fields)
        {
            for (int f = 0; f < fields.Length; f++)
            {
                int c = _length >> ChunkBits;
                if (c == _chunks.Count) _chunks.Add(new int[ChunkSize]);
                _chunks[c][_length & ChunkMask] = fields[f];
                _length++;
            }
            return Count++;
        }

        private bool Same(int record, int[] fields)
        {
            int p = record * Stride;
            for (int f = 0; f < fields.Length; f++, p++)
                if (_chunks[p >> ChunkBits][p & ChunkMask] != fields[f]) return false;
            return true;
        }
    }
}

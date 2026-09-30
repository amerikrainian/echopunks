using System;
using System.Collections;
using System.Collections.Generic;

namespace Echopunks.UI
{
    /// <summary>
    /// The shared core of the editor's run logs (the TEST LOG and the EXECUTION LOG): text
    /// entries grouped under keys in arrival order, UNCAPPED by design (user rule 2026-08-25 —
    /// the graph side materializes only a window of groups around the focused one, so the store
    /// can afford to keep the whole run). The entry cap is pure INSURANCE against the one
    /// unbounded case — a non-terminating solution left fast-forwarding unattended for hours
    /// (~7k entries/s) — set so high it never fires in real use ("never crash the game"): past
    /// it the OLDEST groups drop whole, silently, in chunks with one index rebuild each
    /// (amortized O(1) per add). BCL-pure; unit-tested through both wrappers.
    ///
    /// COMPACT STORAGE (2026-09-30, after a player's freeze report on a 1M-cycle run: its 8M
    /// entries were 8M string objects + 1M per-cycle lists, ~860 MB live with every full GC
    /// walking all of them, while the run held 9 DISTINCT strings): texts are INTERNED into a string table and entries are
    /// int ids in one chunked arrival-order sequence; a group is a contiguous run of it
    /// (<see cref="_start"/>). Only the NEWEST group can extend its run, so an add to an older
    /// group (the test log's out-of-order case) goes to that group's LATE list, read after the
    /// run — exactly the arrival order a per-group list kept. Chunks hold no references, so the
    /// GC never scans the entries, and they stay under the large-object threshold.
    /// </summary>
    internal sealed class GroupedLog<TKey> where TKey : IEquatable<TKey>
    {
        private const int ChunkBits = 14; // 16384 ints = 64 KB, under the 85 KB LOH threshold
        private const int ChunkSize = 1 << ChunkBits;
        private const int ChunkMask = ChunkSize - 1;
        private const int CompactThreshold = 4096; // string tables this small aren't worth compacting

        private static readonly string[] NoEntries = new string[0];

        private readonly int _maxEntries;

        public GroupedLog(int maxEntries)
        {
            _maxEntries = maxEntries > 0 ? maxEntries : 1;
        }

        // String table: each distinct text once.
        private List<string> _strings = new List<string>();
        private Dictionary<string, int> _ids = new Dictionary<string, int>(StringComparer.Ordinal);

        // Every entry's string id in arrival order, chunked; slots [0, _length) are in use, and
        // those before _start[0] belong to shed groups (the first chunk's dead head).
        private readonly List<int[]> _chunks = new List<int[]>();
        private int _length;

        private readonly List<TKey> _order = new List<TKey>();
        private readonly List<int> _start = new List<int>(); // group i's run = [_start[i], _start[i+1] or _length)
        private readonly Dictionary<TKey, int> _pos = new Dictionary<TKey, int>(); // key -> index in _order
        private readonly Dictionary<TKey, List<int>> _late = new Dictionary<TKey, List<int>>();
        private int _count;

        /// <summary>Groups holding entries, oldest first.</summary>
        public IReadOnlyList<TKey> Groups => _order;

        public bool IsEmpty => _order.Count == 0;

        /// <summary>Total entries across all groups.</summary>
        public int EntryCount => _count;

        /// <summary>Groups the insurance cap shed, oldest-first (realistically always 0; kept
        /// for the dev log line and the unit tests).</summary>
        public int DroppedGroups { get; private set; }

        /// <summary>Distinct texts held (tests and diagnostics).</summary>
        internal int DistinctTexts => _strings.Count;

        public void Clear()
        {
            _strings = new List<string>();
            _ids = new Dictionary<string, int>(StringComparer.Ordinal);
            _chunks.Clear();
            _length = 0;
            _order.Clear();
            _start.Clear();
            _pos.Clear();
            _late.Clear();
            _count = 0;
            DroppedGroups = 0;
        }

        public void Add(TKey key, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int id;
            if (!_ids.TryGetValue(text, out id))
            {
                id = _strings.Count;
                _strings.Add(text);
                _ids[text] = id;
            }
            int gi;
            if (!_pos.TryGetValue(key, out gi))
            {
                gi = _order.Count;
                _pos[key] = gi;
                _order.Add(key);
                _start.Add(_length);
            }
            if (gi == _order.Count - 1) Append(id);
            else
            {
                List<int> late;
                if (!_late.TryGetValue(key, out late)) _late[key] = late = new List<int>();
                late.Add(id);
            }
            _count++;
            if (_count > _maxEntries) DropOldest();
        }

        private void Append(int id)
        {
            int c = _length >> ChunkBits;
            if (c == _chunks.Count) _chunks.Add(new int[ChunkSize]);
            _chunks[c][_length & ChunkMask] = id;
            _length++;
        }

        private int RunEnd(int gi) => gi + 1 < _start.Count ? _start[gi + 1] : _length;

        private int GroupCount(int gi)
        {
            List<int> late;
            int n = RunEnd(gi) - _start[gi];
            return _late.TryGetValue(_order[gi], out late) ? n + late.Count : n;
        }

        // Runaway backstop: shed the oldest groups whole until 5% under the cap, so the drop
        // and its index rebuild run rarely.
        private void DropOldest()
        {
            int target = _maxEntries - _maxEntries / 20;
            int k = 0;
            while (k < _order.Count - 1 && _count > target)
            {
                TKey key = _order[k];
                _count -= GroupCount(k);
                _pos.Remove(key);
                _late.Remove(key);
                k++;
                DroppedGroups++;
            }
            if (k == 0) return; // only the newest group is left; it is never shed
            _order.RemoveRange(0, k);
            _start.RemoveRange(0, k);
            int deadChunks = _start[0] >> ChunkBits;
            if (deadChunks > 0)
            {
                _chunks.RemoveRange(0, deadChunks);
                int shift = deadChunks << ChunkBits;
                _length -= shift;
                for (int i = 0; i < _start.Count; i++) _start[i] -= shift;
            }
            for (int i = 0; i < _order.Count; i++) _pos[_order[i]] = i;
            if (_strings.Count > CompactThreshold) CompactStrings();
        }

        // Drop texts only shed groups used, so a runaway of ever-new texts stays bounded by the
        // cap too. Renumbers the survivors' ids; runs once per backstop drop.
        private void CompactStrings()
        {
            var remap = new int[_strings.Count];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var strings = new List<string>();
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            Func<int, int> map = old =>
            {
                int nu = remap[old];
                if (nu < 0)
                {
                    nu = remap[old] = strings.Count;
                    strings.Add(_strings[old]);
                    ids[_strings[old]] = nu;
                }
                return nu;
            };
            for (int p = _start[0]; p < _length; p++)
            {
                int[] chunk = _chunks[p >> ChunkBits];
                chunk[p & ChunkMask] = map(chunk[p & ChunkMask]);
            }
            foreach (var late in _late.Values)
                for (int i = 0; i < late.Count; i++) late[i] = map(late[i]);
            _strings = strings;
            _ids = ids;
        }

        /// <summary>The group's entries in arrival order (empty when absent). A snapshot VIEW
        /// over the store: read it right away — it is not valid across a later Add or Clear.</summary>
        public IReadOnlyList<string> Entries(TKey key)
        {
            int gi;
            if (!_pos.TryGetValue(key, out gi)) return NoEntries;
            List<int> late;
            _late.TryGetValue(key, out late);
            return new EntryView(this, _start[gi], RunEnd(gi) - _start[gi], late);
        }

        /// <summary>The group's index in <see cref="Groups"/>, or -1 when absent (never added,
        /// cleared, or shed by the backstop).</summary>
        public int IndexOf(TKey key)
        {
            int i;
            return _pos.TryGetValue(key, out i) ? i : -1;
        }

        private sealed class EntryView : IReadOnlyList<string>
        {
            private readonly GroupedLog<TKey> _log;
            private readonly int _start, _run, _count;
            private readonly List<int> _late;

            public EntryView(GroupedLog<TKey> log, int start, int run, List<int> late)
            {
                _log = log;
                _start = start;
                _run = run;
                _late = late;
                _count = run + (late != null ? late.Count : 0);
            }

            public int Count => _count;

            public string this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                    int id;
                    if (index < _run)
                    {
                        int p = _start + index;
                        id = _log._chunks[p >> ChunkBits][p & ChunkMask];
                    }
                    else id = _late[index - _run];
                    return _log._strings[id];
                }
            }

            public IEnumerator<string> GetEnumerator()
            {
                for (int i = 0; i < _count; i++) yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

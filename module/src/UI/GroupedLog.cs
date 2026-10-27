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
    ///
    /// TAGS (2026-10-27): every entry also carries one int, -1 unless the caller gives one, in a
    /// parallel chunk sequence (and parallel late lists) — the execution log's pointer into its
    /// state-record store (<see cref="ExecStateStore"/>). <see cref="Shed"/> raises after a
    /// backstop drop so the tag owner can compact what the survivors still reference.
    ///
    /// ORDERED KEYS (2026-10-27): given a key order, the group index is a BINARY SEARCH over
    /// <see cref="Groups"/> instead of a key dictionary — the execution log's (test, cycle) keys
    /// arrive in increasing order, and its dictionary was the store's largest single cost (a
    /// 597k-cycle run: ~20–35 MB of index for ~22 MB of entries). Adds hit the newest group
    /// first, so only reads and late adds search. A new key arriving OUT of order falls back to
    /// the dictionary for the rest of the run (until <see cref="Clear"/>), so correctness never
    /// depends on the order holding.
    /// </summary>
    internal sealed class GroupedLog<TKey> where TKey : IEquatable<TKey>
    {
        private const int ChunkBits = 14; // 16384 ints = 64 KB, under the 85 KB LOH threshold
        private const int ChunkSize = 1 << ChunkBits;
        private const int ChunkMask = ChunkSize - 1;
        private const int CompactThreshold = 4096; // string tables this small aren't worth compacting

        private static readonly string[] NoEntries = new string[0];
        private static readonly int[] NoTags = new int[0];

        private readonly int _maxEntries;
        private readonly IComparer<TKey> _keyOrder;

        public GroupedLog(int maxEntries, IComparer<TKey> keyOrder = null)
        {
            _maxEntries = maxEntries > 0 ? maxEntries : 1;
            _keyOrder = keyOrder;
            if (keyOrder == null) _pos = new Dictionary<TKey, int>();
        }

        // String table: each distinct text once.
        private List<string> _strings = new List<string>();
        private Dictionary<string, int> _ids = new Dictionary<string, int>(StringComparer.Ordinal);

        // Every entry's string id in arrival order, chunked; slots [0, _length) are in use, and
        // those before _start[0] belong to shed groups (the first chunk's dead head).
        private readonly List<int[]> _chunks = new List<int[]>();
        private readonly List<int[]> _tagChunks = new List<int[]>(); // parallel to _chunks
        private int _length;

        private readonly List<TKey> _order = new List<TKey>();
        private readonly List<int> _start = new List<int>(); // group i's run = [_start[i], _start[i+1] or _length)
        // key -> index in _order; null while the keys are ordered (binary search instead).
        private Dictionary<TKey, int> _pos;
        private readonly Dictionary<TKey, List<int>> _late = new Dictionary<TKey, List<int>>();
        private readonly Dictionary<TKey, List<int>> _lateTags = new Dictionary<TKey, List<int>>(); // parallel to _late
        private int _count;

        /// <summary>Raised after each backstop drop, once the shed groups are gone — the tag
        /// owner's cue to compact (see <see cref="RemapTags"/>).</summary>
        public event Action Shed;

        /// <summary>Groups holding entries, oldest first.</summary>
        public IReadOnlyList<TKey> Groups => _order;

        public bool IsEmpty => _order.Count == 0;

        /// <summary>Total entries across all groups.</summary>
        public int EntryCount => _count;

        /// <summary>Groups the insurance cap shed, oldest-first (realistically always 0; kept
        /// for the dev log line and the unit tests).</summary>
        public int DroppedGroups { get; private set; }

        /// <summary>Slots reserved by the group key and start lists (tests and diagnostics).</summary>
        internal int IndexCapacity => _order.Capacity + _start.Capacity;

        /// <summary>Distinct texts held (tests and diagnostics).</summary>
        internal int DistinctTexts => _strings.Count;

        public void Clear()
        {
            _strings = new List<string>();
            _ids = new Dictionary<string, int>(StringComparer.Ordinal);
            // Release capacity, not just contents: the store is static, so a list cleared in
            // place kept the biggest run's peak reserved for the whole session (a 599k-cycle
            // run left 12 MB of empty key/start capacity behind for every later run).
            _chunks.Clear();
            _chunks.Capacity = 0;
            _tagChunks.Clear();
            _tagChunks.Capacity = 0;
            _length = 0;
            _order.Clear();
            _order.Capacity = 0;
            _start.Clear();
            _start.Capacity = 0;
            _pos = _keyOrder == null ? new Dictionary<TKey, int>() : null;
            _late.Clear();
            _lateTags.Clear();
            _count = 0;
            DroppedGroups = 0;
        }

        public void Add(TKey key, string text) => Add(key, text, -1);

        public void Add(TKey key, string text, int tag)
        {
            if (string.IsNullOrEmpty(text)) return;
            int id;
            if (!_ids.TryGetValue(text, out id))
            {
                id = _strings.Count;
                _strings.Add(text);
                _ids[text] = id;
            }
            int gi = Find(key);
            if (gi < 0)
            {
                gi = _order.Count;
                if (_pos == null && gi > 0 && _keyOrder.Compare(key, _order[gi - 1]) < 0)
                {
                    // Out of order: index the rest of the run by dictionary.
                    _pos = new Dictionary<TKey, int>();
                    for (int i = 0; i < _order.Count; i++) _pos[_order[i]] = i;
                }
                if (_pos != null) _pos[key] = gi;
                _order.Add(key);
                _start.Add(_length);
            }
            if (gi == _order.Count - 1) Append(id, tag);
            else
            {
                List<int> late, lateTags;
                if (!_late.TryGetValue(key, out late))
                {
                    _late[key] = late = new List<int>();
                    _lateTags[key] = new List<int>();
                }
                lateTags = _lateTags[key];
                late.Add(id);
                lateTags.Add(tag);
            }
            _count++;
            if (_count > _maxEntries) DropOldest();
        }

        private void Append(int id, int tag)
        {
            int c = _length >> ChunkBits;
            if (c == _chunks.Count)
            {
                _chunks.Add(new int[ChunkSize]);
                _tagChunks.Add(new int[ChunkSize]);
            }
            _chunks[c][_length & ChunkMask] = id;
            _tagChunks[c][_length & ChunkMask] = tag;
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
                _pos?.Remove(key);
                _late.Remove(key);
                _lateTags.Remove(key);
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
                _tagChunks.RemoveRange(0, deadChunks);
                int shift = deadChunks << ChunkBits;
                _length -= shift;
                for (int i = 0; i < _start.Count; i++) _start[i] -= shift;
            }
            if (_pos != null) for (int i = 0; i < _order.Count; i++) _pos[_order[i]] = i;
            if (_strings.Count > CompactThreshold) CompactStrings();
            Shed?.Invoke();
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
            int gi = Find(key);
            if (gi < 0) return NoEntries;
            List<int> late;
            _late.TryGetValue(key, out late);
            return new EntryView(this, _start[gi], RunEnd(gi) - _start[gi], late);
        }

        /// <summary>The group's entry TAGS, index-aligned with <see cref="Entries"/> (empty when
        /// absent). The same snapshot-view rules apply.</summary>
        public IReadOnlyList<int> Tags(TKey key)
        {
            int gi = Find(key);
            if (gi < 0) return NoTags;
            List<int> late;
            _lateTags.TryGetValue(key, out late);
            return new TagView(this, _start[gi], RunEnd(gi) - _start[gi], late);
        }

        /// <summary>Rewrites every live entry's tag through <paramref name="map"/>, in arrival
        /// order per group run (late tags after) — the tag owner's compaction pass.</summary>
        public void RemapTags(Func<int, int> map)
        {
            int first = _start.Count > 0 ? _start[0] : _length;
            for (int p = first; p < _length; p++)
            {
                int[] chunk = _tagChunks[p >> ChunkBits];
                chunk[p & ChunkMask] = map(chunk[p & ChunkMask]);
            }
            foreach (var late in _lateTags.Values)
                for (int i = 0; i < late.Count; i++) late[i] = map(late[i]);
        }

        /// <summary>The group's index in <see cref="Groups"/>, or -1 when absent (never added,
        /// cleared, or shed by the backstop).</summary>
        public int IndexOf(TKey key)
        {
            return Find(key);
        }

        // The key's group index, or -1: the dictionary when one is kept, else the newest group
        // (the add hot path) and then a binary search over the ordered keys.
        private int Find(TKey key)
        {
            if (_pos != null)
            {
                int i;
                return _pos.TryGetValue(key, out i) ? i : -1;
            }
            int n = _order.Count;
            if (n == 0) return -1;
            if (_order[n - 1].Equals(key)) return n - 1;
            int lo = 0, hi = n - 2;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int c = _keyOrder.Compare(_order[mid], key);
                if (c == 0) return mid;
                if (c < 0) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }

        private sealed class TagView : IReadOnlyList<int>
        {
            private readonly GroupedLog<TKey> _log;
            private readonly int _start, _run, _count;
            private readonly List<int> _late;

            public TagView(GroupedLog<TKey> log, int start, int run, List<int> late)
            {
                _log = log;
                _start = start;
                _run = run;
                _late = late;
                _count = run + (late != null ? late.Count : 0);
            }

            public int Count => _count;

            public int this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                    if (index >= _run) return _late[index - _run];
                    int p = _start + index;
                    return _log._tagChunks[p >> ChunkBits][p & ChunkMask];
                }
            }

            public IEnumerator<int> GetEnumerator()
            {
                for (int i = 0; i < _count; i++) yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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

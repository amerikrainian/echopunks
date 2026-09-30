using System;
using System.Collections.Generic;
using System.Linq;
using Echopunks.UI;
using Xunit;

namespace Echopunks.Tests
{
    /// <summary>The compact run-log store against the plain one it replaced (kept here verbatim
    /// as the reference model): whatever the add pattern — newest-group appends, adds to older
    /// groups, backstop drops across chunk boundaries, text-table compaction, clears — every
    /// observable read must match exactly.</summary>
    public class GroupedLogTests
    {
        /// <summary>The pre-2026-09-30 GroupedLog: a string list per group.</summary>
        private sealed class ReferenceLog
        {
            private readonly int _maxEntries;
            private readonly List<int> _order = new List<int>();
            private readonly Dictionary<int, List<string>> _entries = new Dictionary<int, List<string>>();
            private readonly Dictionary<int, int> _pos = new Dictionary<int, int>();
            private int _count;

            public ReferenceLog(int maxEntries) { _maxEntries = maxEntries > 0 ? maxEntries : 1; }
            public IReadOnlyList<int> Groups => _order;
            public bool IsEmpty => _order.Count == 0;
            public int EntryCount => _count;
            public int DroppedGroups { get; private set; }

            public void Clear()
            {
                _order.Clear(); _entries.Clear(); _pos.Clear(); _count = 0; DroppedGroups = 0;
            }

            public void Add(int key, string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                List<string> list;
                if (!_entries.TryGetValue(key, out list))
                {
                    list = new List<string>();
                    _entries[key] = list;
                    _pos[key] = _order.Count;
                    _order.Add(key);
                }
                list.Add(text);
                _count++;
                if (_count > _maxEntries) DropOldest();
            }

            private void DropOldest()
            {
                int target = _maxEntries - _maxEntries / 20;
                int k = 0;
                while (k < _order.Count - 1 && _count > target)
                {
                    int key = _order[k];
                    _count -= _entries[key].Count;
                    _entries.Remove(key);
                    _pos.Remove(key);
                    k++;
                    DroppedGroups++;
                }
                _order.RemoveRange(0, k);
                for (int i = 0; i < _order.Count; i++) _pos[_order[i]] = i;
            }

            public IReadOnlyList<string> Entries(int key)
            {
                List<string> list;
                return _entries.TryGetValue(key, out list) ? list : (IReadOnlyList<string>)new string[0];
            }

            public int IndexOf(int key)
            {
                int i;
                return _pos.TryGetValue(key, out i) ? i : -1;
            }
        }

        private static void AssertSame(ReferenceLog want, GroupedLog<int> got, int keySpace)
        {
            Assert.Equal(want.IsEmpty, got.IsEmpty);
            Assert.Equal(want.EntryCount, got.EntryCount);
            Assert.Equal(want.DroppedGroups, got.DroppedGroups);
            Assert.Equal(want.Groups, got.Groups);
            for (int k = -1; k <= keySpace; k++)
            {
                Assert.Equal(want.IndexOf(k), got.IndexOf(k));
                var w = want.Entries(k);
                var g = got.Entries(k);
                Assert.Equal(w.Count, g.Count);
                for (int i = 0; i < w.Count; i++) Assert.Equal(w[i], g[i]);
                Assert.Equal(w, g.ToList()); // the enumerator too
            }
        }

        // Each pattern: (seed, cap, adds, keySpace, chance an add targets an OLDER key, distinct
        // texts, chance of a unique never-repeated text, clear every N adds or 0).
        [Theory]
        [InlineData(1, 1000000, 20000, 400, 0.0, 9, 0.0, 0)]        // the looping program: in order, few texts
        [InlineData(2, 1000000, 20000, 60, 0.3, 50, 0.1, 0)]        // out-of-order adds (the test log's case)
        [InlineData(3, 300, 20000, 2000, 0.05, 20, 0.0, 0)]         // tight backstop, many drops
        [InlineData(4, 50000, 200000, 20000, 0.01, 30, 0.0, 0)]     // drops across 16K-entry chunks
        [InlineData(5, 20000, 120000, 5000, 0.02, 10, 0.5, 0)]      // unique texts: compaction under drops
        [InlineData(6, 5, 2000, 3, 0.2, 4, 0.0, 0)]                 // cap far below one group
        [InlineData(7, 2000, 30000, 800, 0.1, 15, 0.2, 7000)]       // clears mid-stream
        public void MatchesThePlainStoreExactly(int seed, int cap, int adds, int keySpace,
            double olderChance, int distinct, double uniqueChance, int clearEvery)
        {
            var rng = new Random(seed);
            var want = new ReferenceLog(cap);
            var got = new GroupedLog<int>(cap);
            int newest = 0, unique = 0;
            for (int n = 1; n <= adds; n++)
            {
                int key;
                if (rng.NextDouble() < olderChance) key = rng.Next(0, newest + 1);
                else
                {
                    if (rng.Next(8) == 0 && newest < keySpace) newest++; // ~8 entries per group
                    key = newest;
                }
                string text = rng.NextDouble() < uniqueChance
                    ? "u" + unique++
                    : rng.Next(40) == 0 ? (rng.Next(2) == 0 ? null : "") : "t" + rng.Next(distinct);
                want.Add(key, text);
                got.Add(key, text);
                if (n % 997 == 0) AssertSame(want, got, keySpace);
                if (clearEvery > 0 && n % clearEvery == 0)
                {
                    want.Clear();
                    got.Clear();
                    newest = 0;
                    AssertSame(want, got, keySpace);
                }
            }
            AssertSame(want, got, keySpace);
        }

        [Fact]
        public void StoresEachDistinctTextOnce()
        {
            var log = new GroupedLog<int>(1000000);
            for (int c = 0; c < 10000; c++)
                for (int i = 0; i < 8; i++) log.Add(c, "XA:" + i + ": JUMP STARTUP");
            Assert.Equal(80000, log.EntryCount);
            Assert.Equal(8, log.DistinctTexts);
        }

        [Fact]
        public void CompactionBoundsTheTextTableUnderTheBackstop()
        {
            var log = new GroupedLog<int>(10000);
            for (int n = 0; n < 200000; n++) log.Add(n / 4, "wrote #X, now " + n);
            Assert.True(log.EntryCount <= 10000);
            // Every text is unique, so the live table can never need more than the entries it
            // holds plus the growth before the next compaction.
            Assert.True(log.DistinctTexts <= 10000 + 4096, "table held " + log.DistinctTexts);
            int lastGroup = log.Groups[log.Groups.Count - 1];
            Assert.Equal("wrote #X, now 199999", log.Entries(lastGroup)[log.Entries(lastGroup).Count - 1]);
        }
    }
}

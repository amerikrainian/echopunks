using System;
using System.Collections.Generic;
using Echopunks.UI;
using Xunit;

namespace Echopunks.Tests
{
    /// <summary>The execution log's state columns: change-only records, field encoding, and the
    /// tags surviving a backstop drop (records compacted, every surviving entry still reading
    /// the state it was captured with).</summary>
    public class ExecStateStoreTests
    {
        [Fact]
        public void Unchanged_state_reuses_the_owners_record()
        {
            var s = new ExecStateStore();
            s.Reset(3);
            int a = s.Record(7, new[] { 1, 2, 3 });
            int b = s.Record(7, new[] { 1, 2, 3 });
            int c = s.Record(8, new[] { 1, 2, 3 }); // another owner never shares
            int d = s.Record(7, new[] { 1, 2, 4 });
            int e = s.Record(7, new[] { 1, 2, 3 }); // back to an older state: only the LAST is compared
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
            Assert.NotEqual(a, d);
            Assert.NotEqual(a, e);
            Assert.Equal(4, s.Count);
            Assert.Equal(4, s.Field(d, 2));
        }

        [Fact]
        public void Forgetting_owners_starts_fresh_records()
        {
            var s = new ExecStateStore();
            s.Reset(1);
            int a = s.Record(1, new[] { 5 });
            s.ForgetOwners();
            Assert.NotEqual(a, s.Record(1, new[] { 5 }));
        }

        [Fact]
        public void Field_encoding_round_trips()
        {
            var s = new ExecStateStore();
            s.Reset(5);
            int kw = s.Text("KEYWORD");
            int r = s.Record(0, new[] { -9999, 9999, kw, ExecStateStore.Absent, ExecStateStore.End });
            Assert.True(ExecStateStore.IsNumber(s.Field(r, 0)));
            Assert.Equal(-9999, s.Field(r, 0));
            Assert.Equal(9999, s.Field(r, 1));
            Assert.True(ExecStateStore.IsText(s.Field(r, 2)));
            Assert.Equal("KEYWORD", s.TextOf(s.Field(r, 2)));
            Assert.Equal(kw, s.Text("KEYWORD")); // interned
            Assert.Equal(ExecStateStore.Absent, s.Field(r, 3));
            Assert.Equal(ExecStateStore.End, s.Field(r, 4));
            Assert.False(ExecStateStore.IsText(ExecStateStore.Absent));
            Assert.False(ExecStateStore.IsText(ExecStateStore.End));
            Assert.Equal(ExecStateStore.Absent, s.Text(null));
        }

        [Fact]
        public void Records_cross_chunk_boundaries()
        {
            var s = new ExecStateStore();
            s.Reset(13); // the sandbox stride: records straddle the 16384-int chunks
            var ids = new List<int>();
            for (int i = 0; i < 5000; i++)
            {
                var f = new int[13];
                for (int k = 0; k < 13; k++) f[k] = i * 13 + k;
                ids.Add(s.Record(i, f));
            }
            for (int i = 0; i < 5000; i++)
                for (int k = 0; k < 13; k++) Assert.Equal(i * 13 + k, s.Field(ids[i], k));
        }

        [Fact]
        public void Entries_without_state_read_minus_one()
        {
            var log = new ExecutionLog();
            log.Add(0, 0, "effect row");
            Assert.Equal(-1, log.StateRecords(log.Cycles[0])[0]);
        }

        [Fact]
        public void Backstop_drop_keeps_every_survivor_reading_its_own_state()
        {
            const int cap = 2000;
            var log = new ExecutionLog(cap);
            log.States.Reset(2);
            var rng = new Random(11);
            // expected[(cycle, index)] = the fields that entry was captured with
            var expected = new Dictionary<long, int[]>();
            int[] last = new int[4];
            for (int cycle = 0; cycle < 3000; cycle++)
            {
                for (int exa = 0; exa < 4; exa++)
                {
                    if (rng.Next(3) == 0) last[exa] = rng.Next(100); // mostly unchanged: shared records
                    var fields = new[] { exa, last[exa] };
                    int rec = log.States.Record(exa, fields);
                    int index = log.Cycles.Count > 0 && log.Cycles[log.Cycles.Count - 1].Cycle == cycle
                        ? log.Entries(log.Cycles[log.Cycles.Count - 1]).Count : 0;
                    log.Add(0, cycle, "XA: NOOP", rec);
                    expected[((long)cycle << 8) | (uint)index] = fields;
                }
                if (cycle % 7 == 0) log.Add(0, cycle, "enemy effect"); // tagless rows interleave
            }
            Assert.True(log.DroppedCycles > 0);
            Assert.True(log.States.Count <= log.EntryCount); // compaction bounded the records
            foreach (var k in log.Cycles)
            {
                var entries = log.Entries(k);
                var recs = log.StateRecords(k);
                Assert.Equal(entries.Count, recs.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] == "enemy effect") { Assert.Equal(-1, recs[i]); continue; }
                    var want = expected[((long)k.Cycle << 8) | (uint)i];
                    Assert.Equal(want[0], log.States.Field(recs[i], 0));
                    Assert.Equal(want[1], log.States.Field(recs[i], 1));
                }
            }
            // After compaction the owners start fresh, and new records still land correctly.
            int fresh = log.States.Record(0, new[] { 0, 12345 });
            log.Add(0, 5000, "XA: NOOP", fresh);
            var tail = log.Cycles[log.Cycles.Count - 1];
            Assert.Equal(12345, log.States.Field(log.StateRecords(tail)[0], 1));
        }

        [Fact]
        public void Late_adds_keep_their_tags()
        {
            var log = new GroupedLog<int>(1000);
            log.Add(1, "a", 10);
            log.Add(2, "b", 20);
            log.Add(1, "c", 30); // older group: the late list
            log.Add(2, "d");
            Assert.Equal(new[] { 10, 30 }, log.Tags(1));
            Assert.Equal(new[] { 20, -1 }, log.Tags(2));
            log.RemapTags(t => t < 0 ? t : t + 1);
            Assert.Equal(new[] { 11, 31 }, log.Tags(1));
            Assert.Equal(new[] { 21, -1 }, log.Tags(2));
        }
    }
}

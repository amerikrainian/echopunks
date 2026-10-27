using System;
using System.Collections.Generic;

namespace Echopunks.UI
{
    /// <summary>
    /// The per-run EXECUTION LOG behind the editor's "Execution log" stop: every instruction the
    /// player's EXAs execute, grouped by the cycle it executed on, in dispatch order (the sim's
    /// own turn order) — a <see cref="GroupedLog{TKey}"/> (uncapped, runaway backstop) keyed by
    /// (test, cycle): cycle numbers RESTART AT 0 on every test run and battle round, so a cycle
    /// number alone never identifies a group. Adds tracking for whether the run spanned more
    /// than one test, the cue for region labels to name theirs, and the STATE COLUMNS: each
    /// entry's tag points into <see cref="States"/> (-1 = no state, the enemy/NPC effect rows),
    /// compacted along with every backstop drop.
    /// </summary>
    internal sealed class ExecutionLog
    {
        // ~10M entries ≈ 25 minutes of NONSTOP fast-forward (observed ~7k entries/s) — silent
        // insurance only (see GroupedLog). Compact storage makes that ~40 MB of entry ids plus
        // the distinct texts (a looping program repeats a handful); realistic runs are hundreds
        // of times smaller.
        public const int DefaultMaxEntries = 10000000;

        public struct CycleKey : IEquatable<CycleKey>
        {
            public readonly int Test;
            public readonly int Cycle;
            public CycleKey(int test, int cycle) { Test = test; Cycle = cycle; }
            public bool Equals(CycleKey other) { return Test == other.Test && Cycle == other.Cycle; }
            public override bool Equals(object obj) { return obj is CycleKey && Equals((CycleKey)obj); }
            public override int GetHashCode() { return Test * 397 ^ Cycle; }
        }

        /// <summary>(test, cycle) ascending — the order keys arrive in, which lets the store
        /// index its groups by binary search (see GroupedLog).</summary>
        private sealed class CycleOrder : IComparer<CycleKey>
        {
            public static readonly CycleOrder Instance = new CycleOrder();
            public int Compare(CycleKey a, CycleKey b)
                => a.Test != b.Test ? a.Test.CompareTo(b.Test) : a.Cycle.CompareTo(b.Cycle);
        }

        private readonly GroupedLog<CycleKey> _log;
        private int _firstTest = -1;
        private bool _multi;

        public ExecutionLog(int maxEntries = DefaultMaxEntries)
        {
            _log = new GroupedLog<CycleKey>(maxEntries, CycleOrder.Instance);
            _log.Shed += () => States.Compact(_log.RemapTags);
        }

        /// <summary>The state records the entries' tags point at.</summary>
        public readonly ExecStateStore States = new ExecStateStore();

        /// <summary>Cycles holding entries, oldest first.</summary>
        public IReadOnlyList<CycleKey> Cycles => _log.Groups;

        public bool IsEmpty => _log.IsEmpty;

        /// <summary>Total entries across all cycles.</summary>
        public int EntryCount => _log.EntryCount;

        /// <summary>Cycles the insurance cap shed, oldest-first (realistically always 0).</summary>
        public int DroppedCycles => _log.DroppedGroups;

        /// <summary>True once entries from more than one test run were added — the cue for
        /// region labels to name their test. Sticky until <see cref="Clear"/> (a backstop drop
        /// of the older test keeps the labels explicit, which errs toward clarity).</summary>
        public bool MultiTest => _multi;

        public void Clear()
        {
            _log.Clear();
            States.Reset(0);
            _firstTest = -1;
            _multi = false;
        }

        public void Add(int test, int cycle, string text) => Add(test, cycle, text, -1);

        /// <summary>An entry whose <paramref name="state"/> is a <see cref="States"/> record
        /// (-1 = none).</summary>
        public void Add(int test, int cycle, string text, int state)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (test < 0) test = 0;
            if (_firstTest < 0) _firstTest = test;
            else if (test != _firstTest) _multi = true;
            _log.Add(new CycleKey(test, cycle), text, state);
        }

        public IReadOnlyList<string> Entries(CycleKey key) => _log.Entries(key);

        /// <summary>The cycle's state records, index-aligned with <see cref="Entries"/>.</summary>
        public IReadOnlyList<int> StateRecords(CycleKey key) => _log.Tags(key);

        /// <summary>The cycle's index in <see cref="Cycles"/>, or -1 when absent (never added,
        /// cleared, or shed by the backstop).</summary>
        public int IndexOf(CycleKey key) => _log.IndexOf(key);
    }
}

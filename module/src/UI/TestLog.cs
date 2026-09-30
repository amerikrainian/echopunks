using System.Collections.Generic;

namespace Echopunks.UI
{
    /// <summary>
    /// The per-run TEST LOG behind the editor's "Test log" stop: sim events (EXA errors, goal
    /// flips) grouped by the 0-based test run they landed on, in arrival order — a
    /// <see cref="GroupedLog{TKey}"/>: UNCAPPED (user rule 2026-08-25 — the old newest-24-tests
    /// ring made a 100-test sweep unreadable past its window; the graph side now materializes
    /// only a window of tests around the focused one, so the store keeps the whole run), with
    /// the core's silent insurance cap far past any real run's size.
    /// </summary>
    internal sealed class TestLog
    {
        // ~2M entries — silent insurance only (see GroupedLog), reachable only by an
        // unattended spawn-and-die loop left fast-forwarding for hours. Compact storage keeps
        // each distinct message once (such a loop repeats the same few errors).
        public const int DefaultMaxEntries = 2000000;

        private readonly GroupedLog<int> _log;

        public TestLog(int maxEntries = DefaultMaxEntries)
        {
            _log = new GroupedLog<int>(maxEntries);
        }

        /// <summary>Test indexes holding entries, oldest first.</summary>
        public IReadOnlyList<int> Tests => _log.Groups;

        public bool IsEmpty => _log.IsEmpty;

        /// <summary>Total entries across all tests.</summary>
        public int EntryCount => _log.EntryCount;

        /// <summary>Tests the insurance cap shed, oldest-first (realistically always 0).</summary>
        public int DroppedTests => _log.DroppedGroups;

        public void Clear() => _log.Clear();

        public void Add(int test, string text)
        {
            if (test < 0) test = 0;
            _log.Add(test, text);
        }

        public IReadOnlyList<string> Entries(int test) => _log.Entries(test);

        /// <summary>The test's index in <see cref="Tests"/>, or -1 when absent (never added,
        /// cleared, or shed by the backstop).</summary>
        public int IndexOf(int test) => _log.IndexOf(test);
    }
}

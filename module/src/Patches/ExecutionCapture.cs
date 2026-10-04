using System;
using System.Collections.Generic;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.Screens;
using Echopunks.UI;
using HarmonyLib;

namespace Echopunks.Patches
{
    /// <summary>
    /// Records every instruction the player's EXAs execute, cycle by cycle, into the editor's
    /// EXECUTION LOG stop. The seam is Sim.method_55 — the single per-EXA dispatch the cycle step
    /// (method_54) routes every normal-mode EXA through, once per cycle, with method_10() = the
    /// instruction being executed and method_52() = the cycle it executes on (the counter
    /// increments after the whole cycle, so the instruction the step echo announced pending at
    /// "Cycle n" lands in cycle n). A blocked instruction (M with no peer, LINK on a busy link)
    /// dispatches again next cycle and records again — the same line the sighted player watches
    /// stay highlighted. Special modes (link travel, dying — method_57) execute no instruction and
    /// record nothing. Enemy/NPC EXAs are filtered the way the game filters windows: no
    /// SolutionExa or another team = code that is not the player's to read.
    ///
    /// ENEMY VISIBLE EFFECTS (battle parity, 2026-08-25): another team's CODE is invisible (no
    /// window, no name tag — the team gate), but its EFFECTS are drawn on the map: the sprite
    /// traveling a link, a new sprite replicating in, grab/drop poses with the file icon (and
    /// its lettered id) vanishing/appearing, the kill pose plus the victim's explosion, and the
    /// death explosion that visibly drops whatever the corpse held (method_61 force-drops it).
    /// A prefix/postfix pair on Sim.method_54 — the whole-cycle step — snapshots every
    /// non-player EXA (host, held file, dead mark) plus the file population before the cycle and
    /// diffs after it, appending rows to the same (test, cycle) region the player's instructions
    /// land in. Self-contained per call, so a test advance or battle round (a FRESH Sim) can
    /// never fake "appeared" rows for the starting lineup. HARDWARE WRITES by off-team EXAs
    /// log too (user request 2026-08-29 — the drawn plate flips value; a domination battle IS
    /// watching territory plates flip): the vmethod_8 seam carries the writing EXA, so
    /// attribution is exact and our own writes (already instruction rows) never duplicate.
    /// NOT logged, because not drawn: enemy instructions and their INTERNAL register state
    /// (window-less), enemy M-bus bubbles (team-gated at draw), a WIPE of a held file (held =
    /// invisible), and victimless kill poses (mode-2 NPC terminals fire one every cycle they
    /// idle — pure spam). MAKE shows the grab pose but no icon vanishes and no id was ever
    /// drawn, so it logs as "made a file", id-less until dropped.
    /// </summary>
    internal static class ExecutionCapture
    {
        /// <summary>Main-thread only (the sim steps, our tick, and the graph build all run on the
        /// game's update thread) — no locking. The editor screen clears it on each arming.</summary>
        public static readonly ExecutionLog Store = new ExecutionLog();

        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(Deobf.Method(typeof(Sim), "method_55"),
                    prefix: new HarmonyMethod(typeof(ExecutionCapture), nameof(ExecutePrefix)));
                harmony.Patch(Deobf.Method(typeof(Sim), "method_54"),
                    prefix: new HarmonyMethod(typeof(ExecutionCapture), nameof(CyclePrefix)),
                    postfix: new HarmonyMethod(typeof(ExecutionCapture), nameof(CyclePostfix)));

                // ENEMY HARDWARE WRITES (user request 2026-08-29): every hardware register
                // write is deferred through the logic's vmethod_8 WITH THE WRITING EXA — the
                // exact attribution the value-diff approach can't give. Patch the base + every
                // override (the PanelCapture slot technique); the prefix records off-team
                // writers only (our own writes are already instruction rows), and the cycle
                // postfix speaks the plate's post-cycle value — the drawn flip, never the
                // enemy's code.
                var writeSlot = Expr.MethodOf(() =>
                    default(GClass298).vmethod_8(null, default(ExaValue), null));
                var writePrefix = new HarmonyMethod(typeof(ExecutionCapture), nameof(WritePrefix));
                harmony.Patch(writeSlot, prefix: writePrefix);
                Type[] types;
                try { types = typeof(GClass298).Assembly.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types; }
                int writeHooks = 1;
                foreach (var type in types)
                {
                    if (type == null || !type.IsSubclassOf(typeof(GClass298))) continue;
                    foreach (var m in type.GetMethods(System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        var baseDef = m.GetBaseDefinition();
                        if (baseDef == m || baseDef.DeclaringType != typeof(GClass298)) continue;
                        if (baseDef.MetadataToken != writeSlot.MetadataToken) continue;
                        harmony.Patch(m, prefix: writePrefix);
                        writeHooks++;
                    }
                }
                Log.Info("[patch] execution capture armed (" + writeHooks + " write hooks)");
            }
            catch (Exception ex) { Log.Error("[patch] execution capture failed to apply", ex); }
        }

        /// <summary>Fresh run: drop the log and the listing cache (a recompile makes new listing
        /// strings; stale ones must not accumulate across puzzles).</summary>
        public static void Clear()
        {
            // The insurance cap firing means someone left fast-forward running for ~25 minutes
            // straight — worth a log line for the "my early cycles are missing" report.
            if (Store.DroppedCycles > 0)
                Log.Info("[execlog] insurance cap shed " + Store.DroppedCycles + " oldest cycles last run");
            Store.Clear();
            _lines.Clear();
            _lastEffects.Clear();
        }

        // The visible enemy/NPC effect rows of the most recent cycle, for the step echo's "All
        // EXAs" scope (StepEchoScope) — a stepped cycle's rows, read on the tick after the step.
        private static readonly List<string> _lastEffects = new List<string>();

        public static IReadOnlyList<string> LastCycleEffects => _lastEffects;

        private static void Effect(int test, int cycle, string text)
        {
            Store.Add(test, cycle, text);
            _lastEffects.Add(text);
        }

        private static void ExecutePrefix(Sim __instance, SimExa __0)
        {
            try
            {
                if (__0 == null || !__0.maybe_2.method_0()) return; // player-authored EXAs only
                var editor = GameState.TopScreen() as EditorScreen;
                if (editor == null || !editor.method_0()) return; // a genuinely running editor sim
                try { if (__0.team_0 != editor.method_24()) return; } catch { return; } // never the opponent's code
                string instr = ListingLine(__0);
                if (instr == null) return;
                Store.Add(SimNarration.CurrentTest(editor), __instance.method_52(),
                    __0.string_0 + ": " + instr);
            }
            catch { }
        }

        // ---- enemy visible effects: snapshot before the cycle, diff after it ----

        private struct EnemySnap
        {
            public SimHost Host;
            public SimFile Held;
            public bool Dead;
        }

        // Main-thread only, reused across cycles (cleared per call) — a fast-forwarded run
        // steps thousands of cycles a second and these walks must not allocate per cycle.
        private static readonly Dictionary<SimExa, EnemySnap> _snapEnemies =
            new Dictionary<SimExa, EnemySnap>();
        private static readonly HashSet<SimFile> _snapFiles = new HashSet<SimFile>();
        private static readonly HashSet<SimFile> _curFiles = new HashSet<SimFile>();
        private static readonly HashSet<SimExa> _curEnemies = new HashSet<SimExa>();
        private static bool _snapValid;

        private static SimFile HeldFile(SimExa exa)
        {
            try { return exa.maybe_3.method_0() ? exa.maybe_3.method_2() : null; }
            catch { return null; }
        }

        /// <summary>An effect is only a row when its host is drawn — a hidden host's interior
        /// (occupants, files) is a covered black box for everyone.</summary>
        private static bool Hidden(SimHost host)
            => host == null || ExaEditorScreen.HostHidden(host, false);

        // Off-team hardware writes collected during the cycle (the deferred-write loop runs
        // inside method_54, between our prefix and postfix). Keyed by register — the last
        // writer wins, one row per plate per cycle.
        private static readonly Dictionary<GClass265, SimExa> _pendingWrites =
            new Dictionary<GClass265, SimExa>();

        private static void WritePrefix(GClass265 __0, SimExa __2)
        {
            try
            {
                if (!_snapValid || __0 == null || __2 == null) return; // armed cycles only
                var editor = GameState.TopScreen() as EditorScreen;
                if (editor == null) return;
                if (__2.team_0 == editor.method_24()) return; // own writes = instruction rows
                _pendingWrites[__0] = __2;
            }
            catch { }
        }

        private static SimHost HostOfRegister(Sim sim, GClass265 reg)
        {
            try
            {
                foreach (var host in sim.list_0)
                    if (host.list_2.Contains(reg)) return host;
            }
            catch { }
            return null;
        }

        private static void CyclePrefix(Sim __instance)
        {
            _snapValid = false;
            _pendingWrites.Clear();
            try
            {
                var editor = GameState.TopScreen() as EditorScreen;
                if (editor == null || !editor.method_0()) return;
                Team mine;
                try { mine = editor.method_24(); } catch { return; }
                _snapEnemies.Clear();
                _snapFiles.Clear();
                _lastEffects.Clear();
                foreach (var entity in __instance.list_1)
                {
                    var exa = entity as SimExa;
                    if (exa != null)
                    {
                        if (exa.team_0 != mine)
                            _snapEnemies[exa] = new EnemySnap
                            {
                                Host = exa.method_0(),
                                Held = HeldFile(exa),
                                Dead = exa.bool_0,
                            };
                        continue;
                    }
                    var file = entity as SimFile;
                    if (file != null) _snapFiles.Add(file);
                }
                _snapValid = true;
            }
            catch { }
        }

        private static void CyclePostfix(Sim __instance, GClass271 __result)
        {
            if (!_snapValid) return;
            _snapValid = false;
            try
            {
                var editor = GameState.TopScreen() as EditorScreen;
                if (editor == null || !editor.method_0()) return;
                Team mine;
                try { mine = editor.method_24(); } catch { return; }
                int test = SimNarration.CurrentTest(editor);
                // int_4 increments after the whole cycle — the postfix lands one past the
                // number the method_55 rows recorded for this same cycle.
                int cycle = __instance.method_52() - 1;

                _curFiles.Clear();
                _curEnemies.Clear();
                foreach (var entity in __instance.list_1)
                {
                    var file = entity as SimFile;
                    if (file != null) { _curFiles.Add(file); continue; }
                    var exa = entity as SimExa;
                    if (exa != null && exa.team_0 != mine) _curEnemies.Add(exa);
                }

                // Movement and file effects, in the sim's own entity order.
                foreach (var entity in __instance.list_1)
                {
                    var exa = entity as SimExa;
                    if (exa == null || !_curEnemies.Contains(exa)) continue;
                    var host = exa.method_0();
                    EnemySnap snap;
                    if (!_snapEnemies.TryGetValue(exa, out snap))
                    {
                        // A sprite that wasn't there before the cycle: a REPL copy or an
                        // NPC spawn. (Round/test starts build a FRESH sim, so the starting
                        // lineup is always in the snapshot and never logs.)
                        if (!Hidden(host))
                            Effect(test, cycle, Loc.T("editor.execlog.appeared", new
                            {
                                exa = ExaEditorScreen.ExaDisplayName(exa),
                                host = ExaEditorScreen.HostName(host),
                            }));
                        continue;
                    }
                    if (!ReferenceEquals(host, snap.Host) && !(Hidden(host) && Hidden(snap.Host)))
                        Effect(test, cycle, Loc.T("editor.execlog.moved", new
                        {
                            exa = ExaEditorScreen.ExaDisplayName(exa),
                            from = ExaEditorScreen.HostName(snap.Host),
                            to = ExaEditorScreen.HostName(host),
                        }));
                    var held = HeldFile(exa);
                    if (held != null && !ReferenceEquals(held, snap.Held) && !Hidden(host))
                        Effect(test, cycle, _snapFiles.Contains(held)
                            ? Loc.T("editor.execlog.grabbed", new
                            {
                                exa = ExaEditorScreen.ExaDisplayName(exa),
                                host = ExaEditorScreen.HostName(host),
                                id = ExaEditorScreen.FileId(held),
                            })
                            : Loc.T("editor.execlog.made", new
                            {
                                exa = ExaEditorScreen.ExaDisplayName(exa),
                                host = ExaEditorScreen.HostName(host),
                            }));
                    // A held file gone from the sim entirely was WIPEd in hand — invisible.
                    if (snap.Held != null && held == null && _curFiles.Contains(snap.Held)
                        && !Hidden(host))
                        Effect(test, cycle, Loc.T("editor.execlog.dropped", new
                        {
                            exa = ExaEditorScreen.ExaDisplayName(exa),
                            host = ExaEditorScreen.HostName(host),
                            id = ExaEditorScreen.FileId(snap.Held),
                        }));
                }

                // Enemy hardware writes: the DRAWN effect is the plate's value flipping —
                // speak the post-cycle plate exactly as the viewing player's map shows it
                // (vmethod_9, pure read, viewing team). The writer's code stays unspoken.
                if (_pendingWrites.Count > 0)
                {
                    foreach (var kv in _pendingWrites)
                    {
                        var host = HostOfRegister(__instance, kv.Key);
                        if (Hidden(host)) continue;
                        string value = null;
                        try
                        {
                            value = __instance.method_43()
                                .vmethod_9(kv.Key, false, mine, false).method_2(true);
                        }
                        catch { }
                        Effect(test, cycle, Loc.T("editor.execlog.wrote", new
                        {
                            exa = ExaEditorScreen.ExaDisplayName(kv.Value),
                            host = ExaEditorScreen.HostName(host),
                            reg = ExaEditorScreen.RegName(kv.Key),
                            value = value ?? "?",
                        }));
                    }
                    _pendingWrites.Clear();
                }

                // Kills — the cycle step returns its (killer, victim) pairs; the victim's own
                // death row follows from the diff below (or the test log when it's ours).
                if (__result != null && __result.list_0 != null)
                    foreach (var pair in __result.list_0)
                    {
                        var killer = pair.simExa_0;
                        if (killer == null || killer.team_0 == mine) continue;
                        if (!pair.maybe_0.method_0()) continue; // pose with no victim
                        var host = killer.method_0();
                        if (Hidden(host)) continue;
                        Effect(test, cycle, Loc.T("editor.execlog.killed", new
                        {
                            exa = ExaEditorScreen.ExaDisplayName(killer),
                            host = ExaEditorScreen.HostName(host),
                            victim = ExaEditorScreen.ExaDisplayName(pair.maybe_0.method_2()),
                        }));
                    }

                // Deaths: marked in place (errors/kills — the sim deletes the EXA next cycle)
                // or already removed mid-cycle (HALT). A snapshot already marked dead was
                // reported on the cycle that marked it.
                foreach (var kv in _snapEnemies)
                {
                    if (kv.Value.Dead) continue;
                    var exa = kv.Key;
                    bool present = _curEnemies.Contains(exa);
                    if (present && !exa.bool_0) continue;
                    var host = present ? exa.method_0() : kv.Value.Host;
                    if (Hidden(host)) continue;
                    // The corpse's held file drops where it died (visible icon) — named
                    // unless the file left the sim with it.
                    var dropped = kv.Value.Held;
                    if (dropped != null && !_curFiles.Contains(dropped)) dropped = null;
                    Effect(test, cycle, dropped != null
                        ? Loc.T("editor.execlog.died.file", new
                        {
                            exa = ExaEditorScreen.ExaDisplayName(exa),
                            host = ExaEditorScreen.HostName(host),
                            id = ExaEditorScreen.FileId(dropped),
                        })
                        : Loc.T("editor.execlog.died", new
                        {
                            exa = ExaEditorScreen.ExaDisplayName(exa),
                            host = ExaEditorScreen.HostName(host),
                        }));
                }
            }
            catch { }
        }

        // The executing listing (macro-expanded, one line per instruction) is a cached string on
        // the compile result — but splitting it per record would allocate on every instruction of
        // a fast-forwarded run. Split once per listing, keyed by the string's identity.
        private static readonly Dictionary<string, string[]> _lines =
            new Dictionary<string, string[]>(ReferenceComparer.Instance);

        private static string ListingLine(SimExa exa)
        {
            try
            {
                var instr = exa.method_10();
                if (!instr.maybe_0.method_0()) return null;
                string listing = exa.method_9();
                if (listing == null) return null;
                string[] lines;
                if (!_lines.TryGetValue(listing, out lines))
                {
                    lines = listing.Split('\n');
                    for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].Trim();
                    if (_lines.Count > 64) _lines.Clear(); // runaway guard; refills next record
                    _lines[listing] = lines;
                }
                int idx = instr.maybe_0.method_2();
                return idx >= 0 && idx < lines.Length && lines[idx].Length > 0 ? lines[idx] : null;
            }
            catch { return null; }
        }

        private sealed class ReferenceComparer : IEqualityComparer<string>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(string a, string b) { return ReferenceEquals(a, b); }
            public int GetHashCode(string s)
            { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(s); }
        }
    }
}

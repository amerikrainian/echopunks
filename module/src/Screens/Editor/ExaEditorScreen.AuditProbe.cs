#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Echopunks.Game;
using Echopunks.UI;
using Echopunks.UI.Graph;

namespace Echopunks.Screens
{
    // DEV-ONLY parity-audit probe (DEBUG builds; reached from the dev server's /eval by
    // reflection on the live module generation). Typed against the deob names, so an audit
    // never hand-writes obfuscated reflection: one entry point, string in, string out.
    //   list            campaign rows (main + side): id, kind, puzzle, title, visible, done
    //   open <id>       open that row's puzzle in the editor (the desktop's own public opener)
    //   leave           pop the top game screen
    //   model           hosts / links (both side ids) / files / registers / goals / windows
    //   host <n>        select host n (the host-scoped stops follow it)
    //   dump            every node of the navigator's CURRENT render, fully composed
    //   act <idpart>    run OnActivate of the first node whose id contains idpart
    //   close           close the mod-side popups
    public sealed partial class ExaEditorScreen
    {
        public static string Audit(string cmd, string arg)
        {
            try
            {
                switch (cmd)
                {
                    case "list": return AuditList();
                    case "open": return AuditOpen(arg);
                    case "leave": return GameApi.PopScreen() ? "popped" : "pop failed";
                    case "model": return AuditModel();
                    case "host": return AuditHost(int.Parse(arg));
                    case "dump": return AuditDump();
                    case "act": return AuditAct(arg);
                    case "close": return AuditClose();
                    case "wins": return AuditWins();
                    // devwin: exactly what the game's Ctrl+Shift+F11 dev skip does in a normal puzzle
                    // (synthetic Shift chords never reach SDL) — the completion screen, zero scores.
                    // markseen <id>: set a story flag (saveData.method_12(id, 0)) — "ember-7" unlocks
                    // the real Chat/Tasks tabs. BOTH WRITE THE SAVE: back it up, restore after.
                    case "devwin":
                        return GameApi.PushScreen(new PuzzleCompletionScreen(Editor, Editor.solution_0, 0, 0, 0, false))
                            ? "completion pushed" : "push failed";
                    case "markseen":
                        GameLogic.gameLogic_0.saveData_0.method_12(arg, 0);
                        return "marked " + arg;
                    // unlockall: WRITES THE SAVE (back it up). See AuditUnlockAll.
                    case "unlockall": return AuditUnlockAll();
                    // lockcodes: PB056's lock combinations (public int_2) — audit-only, to drive the
                    // unlock path in a scripted run pass.
                    case "lockcodes":
                    {
                        var hy = TheSim(Editor)?.method_43() as SpecialPuzzleLogics.BonusHydroponix;
                        return hy == null ? "not that puzzle" : string.Join(",", hy.int_2);
                    }
                    // code <text, '|' = newline>: replace the FIRST EXA's program (edit-time; the
                    // per-frame rebuild compiles it). step <n>: advance n cycles through the game's
                    // own start/advance seam. reset: the game's reset. For scripted run passes —
                    // the solution file this touches is the audit's throwaway one.
                    case "code":
                        Editor.solution_0.list_0[0].string_1 = arg.Replace('|', (char)10);
                        Invoke(DirtyMethod, Editor);
                        return "code set (" + Editor.solution_0.list_0[0].string_1.Split((char)10).Length + " lines)";
                    case "step":
                        AuditScreen.StepSim(); // the user's own F2 path, narration included
                        return "stepped";
                    case "reset": AuditScreen.ResetSim(); return "reset";
                    // devflag on|off: the game's in-memory "show everything" flag (GClass1.bool_5) —
                    // reveals every campaign row and the CHATSUBO Chat/Tasks tabs; nothing is saved.
                    case "devflag": GClass1.bool_5 = arg == "on"; return "bool_5 = " + GClass1.bool_5;
                    // credits: push the credits roll; creditseek <t>: set its clock. POP it (leave)
                    // before ~73s or the game itself marks CreditsSeen in config.cfg.
                    case "credits": return GameApi.PushScreen(new GClass252()) ? "credits pushed" : "push failed";
                    case "creditseek":
                    {
                        var roll = GameState.TopScreen() as GClass252;
                        if (roll == null) return "credits not on top";
                        Deobf.Field(typeof(GClass252), "float_0").SetValue(roll, float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
                        return "clock = " + arg;
                    }
                }
                return "unknown command";
            }
            catch (Exception ex) { return "ERR " + ex; }
        }

        private static ExaEditorScreen AuditScreen => ScreenManager.Current as ExaEditorScreen;

        private static string AuditList()
        {
            var sb = new StringBuilder();
            var desktop = GameState.TopScreen() as DesktopScreen;
            var lists = new[] { GClass61.gclass290_0, GClass61.gclass290_1 };
            for (int l = 0; l < lists.Length; l++)
            {
                var items = lists[l]?.list_0;
                if (items == null) continue;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    string puzzle = "-";
                    try { if (it.maybe_1.method_0()) puzzle = (PuzzleMetaMethod.Invoke(null, new object[] { it.maybe_1.method_2() }) as GClass361)?.string_0; } catch { }
                    string vis = "?", done = "?";
                    try { if (desktop != null) vis = desktop.method_6(it) ? "vis" : "HIDDEN"; } catch { }
                    try { done = it.method_0() ? "done" : "open"; } catch { }
                    sb.Append(l).Append('.').Append(i).Append('\t').Append(it.string_0).Append('\t')
                      .Append((int)it.genum20_0).Append('\t').Append(puzzle).Append('\t')
                      .Append(vis).Append('\t').Append(done).Append('\t').Append(it.locString_0).Append('\n');
                }
            }
            return sb.ToString();
        }

        // Reveal every campaign row (main + side) and mark every dialogue seen. The game has no
        // "unlocked" state: a row appears once its predecessor is COMPLETE (GClass61's reveal
        // walk), so every puzzle row is marked complete the score-less way the game itself uses
        // for over-size solves (SolvedWithBadSize — no fake Best.* entries, no histogram marker).
        // Meetings complete via CutsceneComplete.<id>.0; every intro/outro is marked seen; chat
        // ids (rows with post-completion chat) join ChatProgress in campaign order. The optional
        // program wins (solitaire/sandbox/arcade) are left open — they gate nothing. Then the live
        // desktop's marker set is refilled silently (imethod_3) and the mod's desktop watcher is
        // made to re-prime, so nothing speaks a burst of "new task" lines; config flushed to disk.
        private static string AuditUnlockAll()
        {
            var save = GameLogic.gameLogic_0.saveData_0;
            var chat = new List<string>(save.method_14());
            int puzzles = 0, meetings = 0, scenes = 0, chats = 0;
            foreach (var list in new[] { GClass61.gclass290_0, GClass61.gclass290_1 })
            {
                foreach (var it in list.list_0)
                {
                    int kind = (int)it.genum20_0;
                    if (kind == 0 && !it.method_0()) { save.method_1(it.maybe_1.method_2()); puzzles++; }
                    if (kind == 1 && !save.method_13(it.string_0, 0)) { save.method_12(it.string_0, 0); meetings++; }
                    if (it.method_1() && !save.method_13(it.string_0, 0)) { save.method_12(it.string_0, 0); scenes++; }
                    if (it.method_2() && !save.method_13(it.string_0, 1)) { save.method_12(it.string_0, 1); scenes++; }
                    if (it.vignette_0.list_2.Count > 0 && !chat.Contains(it.string_0)) { chat.Add(it.string_0); chats++; }
                }
            }
            save.method_15(chat.ToArray());

            foreach (var screen in GameState.ScreenStack())
            {
                var desktop = screen as DesktopScreen;
                if (desktop != null) desktop.imethod_3();
            }
            foreach (var s in ScreenManager.Registered)
            {
                var hub = s as DesktopHubScreen;
                if (hub != null) hub.Reprime();
            }
            save.configFile_0.method_6();
            return "puzzles completed " + puzzles + ", meetings " + meetings + ", scenes seen " + scenes
                + ", chats added " + chats;
        }

        private static string AuditOpen(string id)
        {
            foreach (var list in new[] { GClass61.gclass290_0, GClass61.gclass290_1 })
            {
                if (list?.list_0 == null) continue;
                foreach (var it in list.list_0)
                {
                    if (it.string_0 != id) continue;
                    if (!it.maybe_1.method_0()) return "row has no puzzle";
                    DesktopScreen.smethod_0(it.maybe_1.method_2(), false);
                    return "opened " + it.maybe_1.method_2();
                }
            }
            return "no such row";
        }

        private static string M<T>(Maybe<T> m)
        {
            try { return m.method_0() ? Convert.ToString(m.method_2()) : "-"; }
            catch { return "!"; }
        }

        private static string AuditModel()
        {
            var e = Editor;
            if (e == null) return "no editor";
            var sim = TheSim(e);
            var meta = Meta(e);
            var team = e.method_24();
            var sb = new StringBuilder();
            sb.Append("puzzle id=").Append(meta?.string_0)
              .Append(" title=").Append(meta?.locString_0).Append(" mode=").Append((int)meta.genum18_0)
              .Append(" nameMode=").Append((int)meta.genum145_0)
              .Append(" logic=").Append(sim.method_43()?.GetType().FullName)
              .Append(" editing=").Append(Editing(e)).Append(" team=").Append(team).Append('\n');
            for (int h = 0; h < sim.list_0.Count; h++)
            {
                var host = sim.list_0[h];
                string hid = "?", hidGoal = "?", cap = "?", over = "?";
                try { hid = host.method_10(false).ToString(); } catch { }
                try { hidGoal = host.method_10(true).ToString(); } catch { }
                try { cap = host.method_0().ToString(); } catch { }
                try { over = Convert.ToString(host.locString_0); } catch { }
                sb.Append("HOST ").Append(h).Append(" '").Append(host.string_0).Append("' plate=")
                  .Append((int)host.genum154_0).Append(" range=").Append(host.range2_0.Start).Append("..").Append(host.range2_0.End)
                  .Append(" cap=").Append(cap).Append(" hidden=").Append(hid).Append('/').Append(hidGoal)
                  .Append(" loc='").Append(over).Append("' bools=")
                  .Append(host.bool_0 ? 1 : 0).Append(host.bool_1 ? 1 : 0).Append(host.bool_2 ? 1 : 0)
                  .Append(host.bool_3 ? 1 : 0).Append(host.bool_4 ? 1 : 0).Append(host.bool_5 ? 1 : 0)
                  .Append(" required=").Append(host.list_0.Count)
                  .Append("  => SPOKEN '").Append(HostName(host)).Append("'\n");
                for (int i = 0; i < host.list_1.Count; i++)
                {
                    var link = host.list_1[i];
                    var dest = link.method_1(host);
                    sb.Append("  LINK ").Append(i).Append(" local=").Append(M(link.method_2(host).method_2(team)))
                      .Append(" far=").Append(M(link.method_2(dest).method_2(team)))
                      .Append(" locked=").Append(link.bool_0).Append(" plate=").Append((int)link.genum177_0).Append(" -> ")
                      .Append(sim.list_0.IndexOf(dest)).Append(" '").Append(dest.string_0).Append("'\n");
                }
                foreach (var reg in host.list_2)
                    sb.Append("  REG ").Append(reg.string_0).Append(" badge=").Append(M(reg.maybe_0))
                      .Append(" kind=").Append((int)reg.genum160_0).Append(" flag=").Append(reg.bool_0)
                      .Append(" at=").Append(reg.index2_0).Append('\n');
                try
                {
                    foreach (var ent in host.method_8())
                    {
                        var exa = ent as SimExa;
                        var file = ent as SimFile;
                        if (exa != null) sb.Append("  OCC exa '").Append(ExaDisplayName(exa)).Append("' team=").Append(exa.team_0)
                            .Append(" holds=").Append(exa.maybe_3.method_0()).Append('\n');
                        else if (file != null) sb.Append("  OCC file ").Append(FileId(file)).Append('\n');
                        else sb.Append("  OCC ").Append(ent?.GetType().Name).Append('\n');
                    }
                }
                catch (Exception ex) { sb.Append("  OCC err ").Append(ex.Message).Append('\n'); }
            }
            foreach (var ent in sim.list_1)
            {
                var file = ent as SimFile;
                if (file == null) continue;
                SimHost at = null;
                SimExa holder = null;
                try { holder = HolderOf(file); at = holder != null ? holder.method_0() : file.method_0(); } catch { }
                var vals = file.list_0.Select(v => v.method_2(true)).ToList();
                sb.Append("FILE ").Append(FileId(file)).Append(" host=").Append(sim.list_0.IndexOf(at))
                  .Append(" holder=").Append(holder != null ? ExaDisplayName(holder) : "-")
                  .Append(" width=").Append(file.int_0).Append(" bools=")
                  .Append(file.bool_0 ? 1 : 0).Append(file.bool_1 ? 1 : 0).Append(file.bool_2 ? 1 : 0).Append(file.bool_3 ? 1 : 0)
                  .Append(" kind=").Append((int)file.genum142_0).Append(" n=").Append(vals.Count)
                  .Append(" [").Append(string.Join(",", vals.Take(12))).Append(vals.Count > 12 ? ",…" : "").Append("]\n");
            }
            for (int g = 0; g < sim.list_2.Count; g++)
                sb.Append("GOAL ").Append(g).Append(' ').Append(sim.list_2[g].GetType().Name).Append(" '")
                  .Append(sim.list_2[g].imethod_0()).Append("'\n");
            sb.Append("ghosts(list_6)=").Append(sim.list_6.Count).Append('\n');
            return sb.ToString();
        }

        private static string AuditHost(int n)
        {
            var s = AuditScreen;
            if (s == null) return "editor screen not focused";
            s.SelectHost(n);
            return "selected host " + n;
        }

        private static string AuditDump()
        {
            var render = (Navigation.Active as GraphNavigator)?.CurrentRender;
            if (render == null) return "no render";
            var sb = new StringBuilder();
            object stop = null;
            foreach (var node in render.Order)
            {
                if (!Equals(node.StopKey, stop)) { stop = node.StopKey; sb.Append("== ").Append(stop).Append('\n'); }
                string text;
                try { text = GraphAnnouncer.ComposeFull(node); } catch (Exception ex) { text = "ERR " + ex.Message; }
                sb.Append(node.Focusable ? "  " : "  (ctx) ").Append(node.Id).Append(" :: ").Append(text).Append('\n');
            }
            return sb.ToString();
        }

        private static string AuditAct(string idPart)
        {
            var render = (Navigation.Active as GraphNavigator)?.CurrentRender;
            if (render == null) return "no render";
            foreach (var node in render.Order)
            {
                if (node.Id == null || node.Id.ToString().IndexOf(idPart, StringComparison.Ordinal) < 0) continue;
                if (node.Vtable?.OnActivate == null) return "node has no OnActivate: " + node.Id;
                Navigation.FocusNode(node.Id, announce: false);
                node.Vtable.OnActivate();
                return "activated " + node.Id;
            }
            return "no node matches";
        }

        // The game's window-STATE cache keys (insertion-ordered, never pruned): an id the
        // goal view windows shows up here once that view has drawn.
        private static string AuditWins()
        {
            var dict = WindowListField.GetValue(Editor) as System.Collections.IDictionary;
            if (dict == null) return "no dictionary";
            var sb = new StringBuilder();
            foreach (EntityID key in dict.Keys) sb.Append(key.Number).Append(key.Hostname.method_0() ? "@" + key.Hostname.method_2() : "").Append(" | ");
            return sb.ToString();
        }

        private static string AuditClose()
        {
            var s = AuditScreen;
            if (s == null) return "editor screen not focused";
            if (s.FilePopupOpen) { s._popupFile = null; s._popupGoalHost = s._popupGoalRequired = -1; return "file popup closed"; }
            if (s._goalPopup) { s.CloseGoalPopup(); return "goal popup closed"; }
            return "nothing open";
        }
    }
}
#endif

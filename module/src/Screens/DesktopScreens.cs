using System;
using System.Collections.Generic;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.UI;
using Echopunks.UI.Graph;

namespace Echopunks.Screens
{
    /// <summary>
    /// The in-game desktop (game type DesktopScreen — name-preserved), modeled as one screen with
    /// four Tab stops: the AXIOM organizer task list, the selected task's detail pane, the CHATSUBO
    /// window (chat log + user list, and the side-jobs list once the game unlocks its tabs), and
    /// the right-edge program launchers + close. The desktop is the first screen where the game has
    /// its OWN keyboard handling (arrows/Enter/Home/End move the task selection) — the mod mirrors
    /// that model: task rows SELECT as focus scrolls over them (NodeVtable.OnSelect, the same
    /// method_9 the game's arrow keys call) and OPEN on Enter (method_11, the double-click path),
    /// while Patches/GameKeySuppression keeps the game's own reader from double-handling the keys.
    /// Labels are the game's LocStrings throughout; ours only where the game draws art.
    /// </summary>
    public sealed class DesktopHubScreen : Screen
    {
        public DesktopHubScreen() { Wrap = true; }

        public override string Key => "desktop";
        public override string ScreenName => Loc.T("screen.DesktopScreen");
        public override bool IsActive() => GameState.TopScreen() is DesktopScreen;

        // Covered by cutscenes/solitaire/editor pushes constantly — keep focus + chat watermarks.
        public override bool KeepStateOnPop => true;

        public override object InitialFocusStop => "tasks";

        // The desktop's private half, resolved by deob name (DesktopScreen keeps its real name):
        // method_9(CampaignItem) = select (the arrow-key/click path), method_11() = open selected
        // (the double-click path), list_0 = the CHATSUBO message log, bool_2 = the Chat/Tasks tab
        // flag (true = Tasks), tuple_0 = the fixed 10-character user roster.
        private static readonly MethodInfo SelectMethod = Deobf.Method(typeof(DesktopScreen), "method_9");
        private static readonly MethodInfo OpenMethod = Deobf.Method(typeof(DesktopScreen), "method_11");
        private static readonly FieldInfo ChatLogField = Deobf.Field(typeof(DesktopScreen), "list_0");
        private static readonly FieldInfo ChatTabField = Deobf.Field(typeof(DesktopScreen), "bool_2");
        private static readonly FieldInfo RosterField = Deobf.Field(typeof(DesktopScreen), "tuple_0");

        /// <summary>The live desktop instance anywhere on the game's screen stack, else null.</summary>
        private static DesktopScreen Desktop
        {
            get
            {
                var stack = GameState.ScreenStack();
                if (stack == null) return null;
                for (int i = stack.Count - 1; i >= 0; i--)
                    if (stack[i] is DesktopScreen d) return d;
                return null;
            }
        }

        /// <summary>The selected task — global game state, not screen state.</summary>
        private static CampaignItem SelectedTask => GameLogic.gameLogic_0?.campaignItem_0;

        private static void Select(CampaignItem item)
        {
            var d = Desktop;
            if (d == null || SelectMethod == null || item == null) return;
            try { SelectMethod.Invoke(d, new object[] { item }); }
            catch (Exception ex) { Log.Error("[desktop] select failed", ex); }
        }

        // HACK*MATCH (campaign type 5, the arcade item) is deliberately NOT made accessible: a
        // real-time falling-block game whose speed would need rewriting to play by ear (user
        // decision, 2026-09-26). Every keyboard open path (task row, details button, launcher)
        // speaks the notice instead; the game's own mouse paths are untouched.
        // (No const of the game's enum type: the remapper's Cecil writer would then have to
        // resolve the constant's declaring assembly, and the reload fails.)
        private static bool IsArcade(CampaignItem item)
        {
            try { return item != null && item.genum20_0 == (GEnum20)5; }
            catch { return false; }
        }

        private static void Open(CampaignItem item)
        {
            var d = Desktop;
            if (d == null || OpenMethod == null) return;
            if (IsArcade(item ?? SelectedTask))
            {
                Speech.Tts.Speak(Loc.T("desktop.arcade.inaccessible"), interrupt: true);
                return;
            }
            try
            {
                if (item != null && !ReferenceEquals(SelectedTask, item))
                    SelectMethod?.Invoke(d, new object[] { item });
                OpenMethod.Invoke(d, null);
            }
            catch (Exception ex) { Log.Error("[desktop] open failed", ex); }
        }

        private static List<GClass24> ChatLog(DesktopScreen d)
        {
            try { return ChatLogField?.GetValue(d) as List<GClass24>; }
            catch { return null; }
        }

        private static bool TasksTabSelected(DesktopScreen d)
        {
            try { return ChatTabField != null && (bool)ChatTabField.GetValue(d); }
            catch { return false; }
        }

        private static void SetTasksTab(DesktopScreen d, bool tasks)
        {
            try { ChatTabField?.SetValue(d, tasks); }
            catch (Exception ex) { Log.Error("[desktop] tab flip failed", ex); }
        }

        // The CHATSUBO Chat/Tasks tabs exist only after this story beat (the game's own gate).
        private static bool ChatTabsUnlocked()
        {
            try
            {
                var gl = GameLogic.gameLogic_0;
                // The game's own gate, dev clause included (DesktopScreen: ember-7 || GClass1.bool_5).
                return gl != null && (gl.saveData_0.method_13("ember-7", 0) || GClass1.bool_5);
            }
            catch { return false; }
        }

        private static bool Visible(DesktopScreen d, CampaignItem item)
        {
            try { return d.method_6(item); }
            catch { return false; }
        }

        // The Tasks tab's drawn check is the DESKTOP's completion marker (private method_7:
        // a GClass38 marker for the item, or the dev flag) — it lands when the desktop
        // announces the completion, which can trail the save's own flag.
        private static readonly MethodInfo CompletedMarkerMethod = Deobf.Method(typeof(DesktopScreen), "method_7");

        private static bool SideJobChecked(CampaignItem item)
        {
            try
            {
                var d = GameState.TopScreen() as DesktopScreen;
                if (d != null && CompletedMarkerMethod != null)
                    return (bool)CompletedMarkerMethod.Invoke(d, new object[] { item });
            }
            catch { }
            return Completed(item);
        }

        private static bool Completed(CampaignItem item)
        {
            try { return item.method_0(); }
            catch { return false; }
        }

        /// <summary>A character's chat display name (the game's own map; its font-escape
        /// backslashes stripped: "selenium\_wolf" speaks as "selenium_wolf").</summary>
        private static string CharacterName(VignetteCharacter c)
        {
            try
            {
                string s;
                if (Vignette.dictionary_0.TryGetValue(c, out s)) return s.Replace("\\", "");
            }
            catch { }
            return c.ToString();
        }

        /// <summary>A chat line's text, through the game's own substitutions (profanity mask via
        /// Vignette.smethod_0; the run-to-instruction combo placeholder), massaged for speech.</summary>
        private static string ChatText(GClass24 line)
        {
            try
            {
                string s = Vignette.smethod_0(line.locString_0.ToString());
                if (s.Contains("<INSERT_RUN_TO_INSTRUCTION_KEY_COMBO>"))
                {
                    bool ctrl = GClass1.genum7_0 == GEnum7.Linux || GClass1.genum7_0 == GEnum7.Stadia;
                    s = s.Replace("<INSERT_RUN_TO_INSTRUCTION_KEY_COMBO>",
                        GameText.T(ctrl ? "Ctrl + Click" : "Alt + Click"));
                }
                return GameText.Speech(s);
            }
            catch { return null; }
        }

        private static string ChatLineSpoken(GClass24 line)
        {
            string text = ChatText(line);
            if (string.IsNullOrEmpty(text)) return null;
            if (line.vignetteCharacter_0 == VignetteCharacter.SystemMessage) return text;
            return Loc.T("desktop.chat.message", new { name = CharacterName(line.vignetteCharacter_0), text });
        }

        // ---- graph ----

        public override void Build(GraphBuilder b)
        {
            var d = Desktop;
            if (d == null) return;

            BuildTasks(b, d);
            BuildDetails(b);
            BuildChatsubo(b, d);
            BuildPrograms(b, d);
        }

        /// <summary>A side job's POSTER: the Tasks tab draws each row as handle + title
        /// (Vignette.dictionary_0 of the puzzle's vignetteCharacter_1 — the same substitution
        /// the home-host plate uses), so the row speaks "handle, title". Null for the main
        /// list, whose rows draw the title alone.</summary>
        private static string SideJobPoster(CampaignItem item)
        {
            try
            {
                if (!item.maybe_1.method_0()) return null;
                var meta = PuzzleMetaMethod?.Invoke(null, new object[] { item.maybe_1.method_2() }) as GClass361;
                return meta == null ? null : CharacterName(meta.vignetteCharacter_1);
            }
            catch { return null; }
        }

        private static readonly MethodInfo PuzzleMetaMethod =
            Deobf.Method(typeof(Puzzle).Assembly.GetType("Puzzles"), "smethod_2");

        private static void TaskRow(GraphBuilder b, CampaignItem item, string idPrefix)
        {
            bool side = idPrefix == "side.";
            b.AddItem(ControlId.Referenced(item, idPrefix + item.string_0), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() =>
                    {
                        string title = GameText.Speech(item.locString_0.ToString());
                        string poster = side ? SideJobPoster(item) : null;
                        return string.IsNullOrEmpty(poster) ? title : poster + ", " + title;
                    }, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => (side ? SideJobChecked(item) : Completed(item)) ? Loc.T("value.complete") : null,
                        kind: AnnouncementKinds.Value),
                },
                // Selection follows focus — never spoken; engine-only, for stop landings.
                Selected = () => ReferenceEquals(SelectedTask, item),
                OnSelect = () => Select(item),  // scrolling over a task selects it, like the game's own arrows
                OnActivate = () => Open(item),  // Enter = the double-click open path
            });
        }

        private void BuildTasks(GraphBuilder b, DesktopScreen d)
        {
            b.BeginStop("tasks");
            b.PushContext(GameText.T("AXIOM PERSONAL ORGANIZER"));
            var list = GClass61.gclass290_0?.list_0;
            if (list != null)
                foreach (var item in list)
                {
                    if (!Visible(d, item)) continue;
                    TaskRow(b, item, "task.");
                    if (ReferenceEquals(SelectedTask, item))
                        b.SetStart(ControlId.Referenced(item, "task." + item.string_0));
                }
            b.PopContext();
        }

        private void BuildDetails(GraphBuilder b)
        {
            var sel = SelectedTask;
            if (sel == null) return;
            b.BeginStop("details");
            b.PushContext(Loc.T("desktop.details"));

            b.AddItem(ControlId.Structural("details.title"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.Speech(SelectedTask?.locString_0.ToString()),
                        kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => GameText.Speech(SelectedTask?.locString_1.ToString()),
                        kind: AnnouncementKinds.Value),
                },
            });
            b.AddItem(ControlId.Structural("details.desc"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.Speech(SelectedTask?.locString_2.ToString()),
                        kind: AnnouncementKinds.Label),
                },
            });

            // The typed footer line the game draws with an icon: a street address for cutscene
            // tasks (a literal in the game's draw code, mirrored here — there is no data source to
            // read it from), the target hostname for hacking tasks, a file name otherwise.
            string footerLabel = null;
            Func<string> footerValue = null;
            var kind = sel.genum20_0;
            if (kind == (GEnum20)1)
            {
                footerLabel = Loc.T("desktop.detail.location");
                footerValue = () => "605 Eddy St. #801, San Francisco, CA";
            }
            else if (kind == (GEnum20)0 && sel.maybe_0.method_0())
            {
                footerLabel = Loc.T("desktop.detail.host");
                footerValue = () => { var s = SelectedTask; return s != null && s.maybe_0.method_0() ? s.maybe_0.method_2() : null; };
            }
            else if (sel.maybe_0.method_0())
            {
                footerLabel = Loc.T("desktop.detail.file");
                footerValue = () => { var s = SelectedTask; return s != null && s.maybe_0.method_0() ? s.maybe_0.method_2() : null; };
            }
            if (footerValue != null)
            {
                string label = footerLabel;
                var value = footerValue;
                b.AddItem(ControlId.Structural("details.footer"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => label, kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(value, kind: AnnouncementKinds.Value),
                    },
                });
            }

            // The detail pane's right half for an UNSOLVED puzzle: the game's own placeholder
            // line (the solved variant — histograms + leaderboards — is still deferred).
            // Gate mirrors the draw exactly: a puzzle item with no recorded solve.
            bool boardsHint = false;
            try
            {
                boardsHint = kind == (GEnum20)0 && sel.maybe_1.method_0()
                    && !GameLogic.gameLogic_0.saveData_0.method_6(sel.maybe_1.method_2(), false);
            }
            catch { }
            if (boardsHint)
                b.AddItem(ControlId.Structural("details.boards"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(
                            () => GameText.TSpeech("Solve this puzzle to view histograms and leaderboards."),
                            kind: AnnouncementKinds.Label),
                    },
                });

            // The action button, labeled by the game per task type ("PLAY CUTSCENE", "CONNECT TO
            // NETWORK", ...) — the same open path as Enter on the task row.
            b.AddItem(ControlId.Structural("details.action"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() =>
                    {
                        try { return GameText.Speech(SelectedTask?.method_6().ToString()); }
                        catch { return null; }
                    }, kind: AnnouncementKinds.Label),
                },
                OnActivate = () => Open(null),
            });
            // The EMBER-2 panel's INTRO / OUTRO buttons (mouse-only in the game): replay the
            // selected task's story vignettes. A button exists when the task HAS that vignette
            // (CampaignItem.method_1/method_2) and lights up once it has been seen
            // (saveData.method_13(id, part)) — Ember2CutsceneScreen.smethod_0's own gates; the
            // click pushes the same player with the desktop's panel origin.
            ReplayButton(b, "details.intro", "Intro", 0);
            ReplayButton(b, "details.outro", "Outro", 1);
            b.PopContext();
        }

        private static bool HasVignette(CampaignItem item, int part)
        {
            try { return item != null && (part == 0 ? item.method_1() : item.method_2()); }
            catch { return false; }
        }

        private static bool VignetteSeen(CampaignItem item, int part)
        {
            try { return GameLogic.gameLogic_0.saveData_0.method_13(item.string_0, part); }
            catch { return false; }
        }

        private static void ReplayButton(GraphBuilder b, string id, string gameKey, int part)
        {
            if (!HasVignette(SelectedTask, part)) return;
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T(gameKey), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => VignetteSeen(SelectedTask, part) ? null : Loc.T("value.unavailable"),
                        kind: AnnouncementKinds.Enabled),
                },
                OnActivate = () =>
                {
                    var item = SelectedTask;
                    if (!HasVignette(item, part)) return;
                    if (!VignetteSeen(item, part))
                    {
                        Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                        return;
                    }
                    GameApi.PushScreen(new Ember2CutsceneScreen(item.string_0, part, DesktopScreen.vector2_0,
                        true, false, GStruct10.gstruct10_0));
                },
            });
        }

        private void BuildChatsubo(GraphBuilder b, DesktopScreen d)
        {
            b.BeginStop("chat");
            b.PushContext(GameText.T("CHATSUBO"));

            bool tabs = ChatTabsUnlocked();
            bool tasksTab = tabs && TasksTabSelected(d);
            if (tabs)
            {
                b.StartRow("chat.tabs");
                ChatTab(b, d, "chat.tab.chat", "Chat", tasks: false);
                ChatTab(b, d, "chat.tab.tasks", "Tasks", tasks: true);
                b.EndRow();
            }

            if (tasksTab)
            {
                // The side-jobs list (the game's second campaign).
                var side = GClass61.gclass290_1?.list_0;
                if (side != null)
                {
                    int hiddenSlots = 0;
                    foreach (var item in side)
                    {
                        if (!Visible(d, item)) { hiddenSlots++; continue; }
                        TaskRow(b, item, "side.");
                    }
                    // The game draws every side job's SLOT: a revealed job as a lettered row, an
                    // unrevealed one as a dim empty frame — so a sighted player sees how many are
                    // still to come. One trailing text row carries that count (user decision,
                    // 2026-09-20: wording ours, the fact the game's).
                    if (hiddenSlots > 0)
                    {
                        int n = hiddenSlots;
                        b.AddItem(ControlId.Structural("side.hidden"), new NodeVtable
                        {
                            ControlType = ControlTypes.Text,
                            Announcements = new[]
                            {
                                new NodeAnnouncement(() => n == 1
                                    ? Loc.T("desktop.sidejobs.hidden.one")
                                    : Loc.T("desktop.sidejobs.hidden", new { n }),
                                    kind: AnnouncementKinds.Label),
                            },
                        });
                    }
                }
            }
            else
            {
                BuildChatLog(b, d);
            }
            b.PopContext();

            // The user roster is its own Tab stop (user request, 2026-08-22): Tab order runs
            // tasks -> details -> chat -> users -> programs -> close. Its "EXAPUNKS (n)" channel
            // header is context enough — no CHATSUBO wrapper, so Tab between chat and users
            // doesn't re-announce the window.
            if (!tasksTab)
            {
                b.BeginStop("users");
                BuildUserList(b, d);
            }
        }

        private static void ChatTab(GraphBuilder b, DesktopScreen d, string id, string gameKey, bool tasks)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T(gameKey), kind: AnnouncementKinds.Label),
                },
                // Selection follows focus — never spoken; engine-only, for stop landings.
                Selected = () => TasksTabSelected(d) == tasks,
                OnSelect = () => SetTasksTab(d, tasks),
                OnActivate = () => SetTasksTab(d, tasks),
            });
        }


        private void BuildChatLog(GraphBuilder b, DesktopScreen d)
        {
            var log = ChatLog(d);
            if (log == null || log.Count == 0) return;
            // A log reads oldest -> newest, THE WHOLE HISTORY (the game persists every played
            // conversation and rebuilds the log each desktop — sighted players scroll all the
            // way back, so a cap here loses real content; user report, 2026-08-22). Positions
            // would be noise on a growing stream.
            b.PushContext(Loc.T("desktop.chat.log"), positions: false);
            for (int i = 0; i < log.Count; i++)
            {
                var line = log[i];
                b.AddItem(ControlId.Referenced(line, "chat.line." + i), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => ChatLineSpoken(line), kind: AnnouncementKinds.Label),
                    },
                });
            }
            b.PopContext();
        }

        private void BuildUserList(GraphBuilder b, DesktopScreen d)
        {
            Tuple<VignetteCharacter, bool>[] roster;
            try { roster = RosterField?.GetValue(d) as Tuple<VignetteCharacter, bool>[]; }
            catch { roster = null; }
            if (roster == null) return;

            // Nivas hides until a reveal-marker line is in the log — the game's own gate.
            bool nivasRevealed = false;
            var log = ChatLog(d);
            if (log != null)
                foreach (var line in log)
                    if (line.genum164_0 == (GEnum164)1) { nivasRevealed = true; break; }

            var shown = new List<Tuple<VignetteCharacter, bool>>();
            foreach (var t in roster)
            {
                if (t.Item1 == VignetteCharacter.Nivas && !nivasRevealed) continue;
                shown.Add(t);
            }

            // The channel header the game draws: "EXAPUNKS (n)".
            b.PushContext("EXAPUNKS (" + shown.Count + ")");
            foreach (var t in shown)
            {
                var c = t.Item1;
                bool op = t.Item2;
                b.AddItem(ControlId.Structural("chat.user." + c), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => CharacterName(c), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => op ? Loc.T("desktop.user.operator") : null,
                            kind: AnnouncementKinds.Value),
                    },
                });
            }
            b.PopContext();
        }

        // The right-edge launcher icons: each opens a special campaign item, visible only once
        // unlocked. Labels are the items' own titles.
        private static readonly string[] ProgramIds = { "solitaire-win", "sandbox-win", "arcade-win", "custom-win" };

        private void BuildPrograms(GraphBuilder b, DesktopScreen d)
        {
            b.BeginStop("programs");
            b.PushContext(Loc.T("desktop.programs"));
            foreach (var id in ProgramIds)
            {
                CampaignItem item = null;
                try
                {
                    var maybe = GClass61.smethod_4(id);
                    if (maybe.method_0()) item = maybe.method_2();
                }
                catch { }
                if (item == null || !Visible(d, item)) continue;
                var prog = item;
                // STRUCTURAL identity, not Referenced: the campaign item is already the
                // task row's backing object, and focus recovery follows an object to the
                // FIRST node referencing it — a Referenced launcher snapped focus back to the
                // organizer row every frame (the ПАСЬЯНС double-focus, 2026-08-23).
                b.AddItem(ControlId.Structural("prog." + id), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => GameText.Speech(prog.locString_0.ToString()),
                            kind: AnnouncementKinds.Label),
                    },
                    OnActivate = () => Open(prog),
                });
            }
            b.PopContext();

            b.BeginStop("close");
            b.AddItem(ControlId.Structural("desktop.close"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T("desktop.close"), kind: AnnouncementKinds.Label),
                },
                OnActivate = () => GameApi.PopScreen(), // the X button's path; Escape also works natively
            });
        }

        // ---- live announcements: new chat lines and task events, spoken as they happen ----

        // Watermarks are primed per game-desktop INSTANCE (not per focus): the desktop survives
        // beneath cutscene/editor pushes and flushes queued chat when re-exposed — priming on
        // focus would swallow exactly those lines. A fresh instance (re-entered from the title)
        // primes silently past the constructor-replayed history.
        private object _primedFor;

#if DEBUG
        /// <summary>Audit probe: re-baseline the chat/task watermarks on the next update (after the
        /// probe rewrites the save, so the refilled marker set doesn't announce as a burst).</summary>
        internal void Reprime() => _primedFor = null;
#endif
        private int _chatSeen;
        private readonly HashSet<object> _markersSeen = new HashSet<object>();

        public override void OnUpdate()
        {
            var d = Desktop;
            if (d == null) return;

            if (!ReferenceEquals(_primedFor, d))
            {
                _primedFor = d;
                _chatSeen = ChatLog(d)?.Count ?? 0;
                _markersSeen.Clear();
                var markers = GameLogic.gameLogic_0?.hashSet_0;
                if (markers != null)
                    foreach (var m in markers) _markersSeen.Add(m);
                return;
            }

            AnnounceNewChat(d);
            AnnounceTaskEvents();
        }

        private void AnnounceNewChat(DesktopScreen d)
        {
            var log = ChatLog(d);
            if (log == null) return;
            if (log.Count < _chatSeen) { _chatSeen = log.Count; return; }
            for (int i = _chatSeen; i < log.Count; i++)
            {
                string s = ChatLineSpoken(log[i]);
                if (!string.IsNullOrEmpty(s)) Speech.Tts.Speak(s);
            }
            _chatSeen = log.Count;
        }

        private void AnnounceTaskEvents()
        {
            var markers = GameLogic.gameLogic_0?.hashSet_0;
            if (markers == null) return;
            foreach (var m in markers)
            {
                if (_markersSeen.Contains(m)) continue;
                _markersSeen.Add(m);
                try
                {
                    var appeared = m as GClass37;
                    if (appeared != null)
                    {
                        Speech.Tts.Speak(Loc.T("desktop.task.new",
                            new { title = GameText.Speech(appeared.campaignItem_0.locString_0.ToString()) }));
                        continue;
                    }
                    var done = m as GClass38;
                    if (done != null)
                        Speech.Tts.Speak(Loc.T("desktop.task.done",
                            new { title = GameText.Speech(done.campaignItem_0.locString_0.ToString()) }));
                }
                catch (Exception ex) { Log.Error("[desktop] task event announce failed", ex); }
            }
        }
    }
}

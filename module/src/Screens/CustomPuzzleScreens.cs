using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.UI;
using Echopunks.UI.Graph;

namespace Echopunks.Screens
{
    /// <summary>Axiom VirtualNetwork+ (CustomPuzzleScreen, name-preserved) — the custom-network
    /// manager behind the desktop's fourth launcher. Natively a mouse window: two tabs (Remote
    /// Networks = subscribed Workshop items, Personal Networks = the player's own scripts —
    /// JavaScript files in the game's user-data folder, edited OUTSIDE the game), a date-ordered
    /// row list (click = select, double-click = play; the game's own arrows only move an
    /// EXISTING selection and Enter plays it), a tab-specific bottom button (Browse Steam
    /// Workshop opens the Steam overlay; Create New Virtual Network writes the template
    /// script), a per-row menu button on the personal tab (GClass245: Edit / Test / Upload /
    /// Copy / Delete), the window's close X, and a detail pane: remote = the three histogram
    /// panels (the browser layout — LeaderboardRows) or the unsolved notice; personal = title,
    /// subtitle and description, or the script error text, plus a rendered map preview (art —
    /// not readable, the description carries the network's own words). Modeled as tabs
    /// (selection-follows-focus through the game's own tab switch, which also selects the
    /// tab's first row like the drawn click), rows (select = method_1 + the arrow sound; Enter
    /// = the game's play path, gated by the script error exactly as the game gates it;
    /// Backspace = delete, confirm-less like the menu item — announced), the menu verbs as an
    /// actions stop acting on the SELECTED network (Test/Upload gated the way the menu dims
    /// them), the close X as a row, and the detail pane as read-only rows. Escape stays native
    /// (the game's own close). Row states speak the game's own labels ("Solved", "Uploaded",
    /// "Script error"); the remote checkmark shares the personal tab's "Solved" word.</summary>
    public sealed class CustomNetworkScreen : Screen
    {
        public CustomNetworkScreen() { Wrap = true; }

        public override string Key => "custom.networks";
        public override string ScreenName => GameText.Speech(GameText.T("Axiom VirtualNetwork+"));
        public override bool IsActive() => GameState.TopScreen() is CustomPuzzleScreen;
        public override object InitialFocusStop => "networks";

        private const int RemoteTab = 1, PersonalTab = 2; // GEnum145 — the meta's kind

        // The screen's private half: maybe_0 = the selected network, genum145_0 = the tab;
        // method_0(tab) = the tab click (selects the tab's first row), method_1 = select,
        // method_3 = create, method_4..8 = the row menu's Edit/Test/Upload/Copy/Delete,
        // method_9 = the close X (sound + slide-out + pop).
        private static readonly FieldInfo SelectedField = Deobf.Field(typeof(CustomPuzzleScreen), "maybe_0");
        private static readonly FieldInfo TabField = Deobf.Field(typeof(CustomPuzzleScreen), "genum145_0");
        private static readonly MethodInfo SetTabMethod = Deobf.Method(typeof(CustomPuzzleScreen), "method_0");
        private static readonly MethodInfo SelectMethod = Deobf.Method(typeof(CustomPuzzleScreen), "method_1");
        private static readonly MethodInfo CreateMethod = Deobf.Method(typeof(CustomPuzzleScreen), "method_3");
        private static readonly MethodInfo EditMethod = Deobf.Method(typeof(CustomPuzzleScreen), "method_4");
        private static readonly MethodInfo TestMethod = Deobf.Method(typeof(CustomPuzzleScreen), "method_5");
        private static readonly MethodInfo UploadMethod = Deobf.Method(typeof(CustomPuzzleScreen), "method_6");
        private static readonly MethodInfo CopyMethod = Deobf.Method(typeof(CustomPuzzleScreen), "method_7");
        private static readonly MethodInfo DeleteMethod = Deobf.Method(typeof(CustomPuzzleScreen), "method_8");
        private static readonly MethodInfo CloseMethod = Deobf.Method(typeof(CustomPuzzleScreen), "method_9");
        // Puzzle → metadata lives on the INTERNAL static class Puzzles (name-preserved):
        // smethod_0 = every puzzle, smethod_2 = its metadata blob.
        private static readonly Type PuzzlesType = typeof(Puzzle).Assembly.GetType("Puzzles");
        private static readonly MethodInfo AllPuzzlesMethod = Deobf.Method(PuzzlesType, "smethod_0");
        private static readonly MethodInfo PuzzleMetaMethod = Deobf.Method(PuzzlesType, "smethod_2");
        // The manager's script-path helper — Edit hands this file to the OS file browser.
        private static readonly MethodInfo ScriptPathMethod = Deobf.Method(typeof(GClass287), "method_12");

        private static CustomPuzzleScreen Manager => GameState.TopScreen() as CustomPuzzleScreen;

        private static int Tab(CustomPuzzleScreen s)
        {
            try { return (int)(GEnum145)TabField.GetValue(s); }
            catch { return RemoteTab; }
        }

        private static Maybe<Puzzle> Selected(CustomPuzzleScreen s)
        {
            try { return (Maybe<Puzzle>)SelectedField.GetValue(s); }
            catch { return GStruct10.gstruct10_0; }
        }

        private static bool IsSelected(CustomPuzzleScreen s, Puzzle p)
        {
            var sel = Selected(s);
            return sel.method_0() && sel.method_2() == p;
        }

        private static GClass361 Meta(Puzzle p)
        {
            try { return PuzzleMetaMethod?.Invoke(null, new object[] { p }) as GClass361; }
            catch { return null; }
        }

        /// <summary>The tab's rows in drawn order: every puzzle whose metadata carries the tab's
        /// kind, oldest first (the game's OrderBy on the metadata date).</summary>
        private static List<Puzzle> Networks(CustomPuzzleScreen s)
        {
            try
            {
                int tab = Tab(s);
                var all = AllPuzzlesMethod?.Invoke(null, null) as IEnumerable<Puzzle>;
                if (all == null) return new List<Puzzle>();
                return all.Where(p => { var m = Meta(p); return m != null && (int)m.genum145_0 == tab; })
                    .OrderBy(p => Meta(p).dateTime_0)
                    .ToList();
            }
            catch { return new List<Puzzle>(); }
        }

        private static bool ScriptError(GClass361 m)
        {
            try { return m != null && m.maybe_0.method_0(); }
            catch { return false; }
        }

        private static string Text(LocString ls)
        {
            try { return GameText.Speech(ls?.ToString()); }
            catch { return null; }
        }

        private static ControlId RowId(Puzzle p) => ControlId.Structural("cn.row." + p.ID);

        // ---- states, exactly as the rows draw them ----

        /// <summary>Remote rows draw a bare checkmark when the save marks the puzzle solved.</summary>
        private static bool RemoteSolved(Puzzle p)
        {
            try { return GameLogic.gameLogic_0.saveData_0.method_6(p, bool_0: false); }
            catch { return false; }
        }

        /// <summary>Personal rows: "Solved" = the saved solve matches the CURRENT script's hash
        /// (the manager's method_19 — editing the script un-solves it), the gate on Upload too.</summary>
        private static bool PersonalSolved(Puzzle p)
        {
            try { return GameLogic.gameLogic_0.gclass287_0.method_19(p); }
            catch { return false; }
        }

        /// <summary>"Uploaded" draws only under a solved row: a published Workshop id exists
        /// and no upload is in flight.</summary>
        private static bool Uploaded(Puzzle p)
        {
            try
            {
                var mgr = GameLogic.gameLogic_0.gclass287_0;
                return PersonalSolved(p)
                    && GameLogic.gameLogic_0.saveData_0.method_9(p).method_0()
                    && !mgr.method_0();
            }
            catch { return false; }
        }

        private static string RowLabel(Puzzle p)
        {
            var m = Meta(p);
            if (m == null) return null;
            // A broken script draws the "Script error" flag where the title would be.
            return ScriptError(m) ? GameText.T("Script error") : Text(m.locString_0);
        }

        private static string RowValue(CustomPuzzleScreen s, Puzzle p)
        {
            var m = Meta(p);
            if (m == null || ScriptError(m)) return null;
            var parts = new List<string>();
            if (Tab(s) == RemoteTab)
            {
                string author = Text(m.locString_1);
                if (!string.IsNullOrWhiteSpace(author)) parts.Add(author);
                if (RemoteSolved(p)) parts.Add(GameText.T("Solved"));
            }
            else
            {
                if (PersonalSolved(p)) parts.Add(GameText.T("Solved"));
                if (Uploaded(p)) parts.Add(GameText.T("Uploaded"));
            }
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        public override void Build(GraphBuilder b)
        {
            var s = Manager;
            if (s == null || SelectedField == null || TabField == null) return;
            bool personal = Tab(s) == PersonalTab;

            b.BeginStop("tabs");
            b.StartRow();
            TabNode(b, "cn.tab.remote", "Remote Networks", RemoteTab);
            TabNode(b, "cn.tab.personal", "Personal Networks", PersonalTab);
            b.EndRow();

            var rows = Networks(s);
            if (rows.Count > 0)
            {
                b.BeginStop("networks");
                b.PushContext(Loc.T("custom.networks"));
                foreach (var p in rows)
                {
                    var row = p;
                    b.AddItem(RowId(row), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => RowLabel(row), kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => RowValue(Manager, row), kind: AnnouncementKinds.Value),
                        },
                        // Selection follows focus (engine-only Selected — never spoken; it
                        // drives stop landings onto the drawn highlight).
                        Selected = () => IsSelected(Manager, row),
                        OnSelect = () => SelectRow(row),
                        OnActivate = () => PlayRow(row),
                        OnSecondary = personal ? (Action)(() => DeleteRow(row)) : null,
                    });
                }
                b.PopContext();
            }

            b.BeginStop("actions");
            b.PushContext(Loc.T("custom.actions"), positions: false);
            if (personal)
                ActionButton(b, "cn.create", () => GameText.T("Create New Virtual Network"), null, null, CreateNetwork);
            else
                ActionButton(b, "cn.workshop", () => GameText.T("Browse Steam Workshop"), null, null, BrowseWorkshop);
            if (personal && Selected(s).method_0())
            {
                // The row menu's verbs on the SELECTED network (its title reads as the value
                // so the target is always audible); Test/Upload gate as the menu dims them.
                Func<string> target = () => SelectedTitle(Manager);
                ActionButton(b, "cn.edit", () => GameText.T("Edit"), target, null, EditSelected);
                ActionButton(b, "cn.test", () => GameText.T("Test"), target, () => TestEnabled(Manager), TestSelected);
                ActionButton(b, "cn.upload", () => GameText.T("Upload"), target, () => UploadEnabled(Manager), UploadSelected);
                ActionButton(b, "cn.copy", () => GameText.T("Copy"), target, null, CopySelected);
                ActionButton(b, "cn.delete", () => GameText.T("Delete"), target, null, DeleteSelected);
            }
            ActionButton(b, "cn.close", () => Loc.T("custom.close"), null, null, CloseWindow);
            b.PopContext();

            BuildDetails(b, s, personal);
        }

        private static void TabNode(GraphBuilder b, string id, string labelKey, int index)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T(labelKey), kind: AnnouncementKinds.Label),
                },
                // Tabs select as you arrow over them — the game's own tab click, which also
                // selects the tab's first row (so the rows stop lands on it).
                Selected = () => Tab(Manager) == index,
                OnSelect = () => SetTab(index),
                OnActivate = () => SetTab(index),
            });
        }

        private static void ActionButton(GraphBuilder b, string id, Func<string> label,
            Func<string> value, Func<bool> enabled, Action activate)
        {
            var anns = new List<NodeAnnouncement> { new NodeAnnouncement(label, kind: AnnouncementKinds.Label) };
            if (value != null) anns.Add(new NodeAnnouncement(value, kind: AnnouncementKinds.Value));
            if (enabled != null)
                anns.Add(new NodeAnnouncement(() => enabled() ? null : Loc.T("value.unavailable"),
                    kind: AnnouncementKinds.Enabled));
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = anns.ToArray(),
                OnActivate = activate,
            });
        }

        private static void BuildDetails(GraphBuilder b, CustomPuzzleScreen s, bool personal)
        {
            var sel = Selected(s);
            if (!sel.method_0()) return;
            var p = sel.method_2();
            var m = Meta(p);
            if (m == null) return;
            if (personal)
            {
                // The right pane's text block: title, subtitle, description — or the
                // script error flag and the compiler's message in their place.
                b.BeginStop("details");
                b.PushContext(Loc.T("custom.details"), positions: false);
                if (ScriptError(m))
                {
                    DetailRow(b, "cn.d.error", () => GameText.T("Script error"));
                    DetailRow(b, "cn.d.errortext", () => ErrorText(Manager));
                }
                else
                {
                    DetailRow(b, "cn.d.title", () => Text(MetaOf(Manager)?.locString_0));
                    DetailRow(b, "cn.d.subtitle", () => Text(MetaOf(Manager)?.locString_1));
                    DetailRow(b, "cn.d.description", () => Text(MetaOf(Manager)?.locString_2));
                }
                b.PopContext();
                return;
            }
            if (!RemoteSolved(p))
            {
                b.BeginStop("notice");
                DetailRow(b, "cn.notice",
                    () => GameText.TSpeech("Solve this puzzle to view histograms and leaderboards."));
                return;
            }
            // The three histogram panels for the selected network — the manager recomputes
            // dictionary_0 (scoreManager method_14) on every selection change. Same Theme
            // layout as the solution browser: no caption, marker = the panel's own maybe_2.
            if (s.dictionary_0 == null) return;
            LeaderboardRows.Build(b, s.dictionary_0.Values, caption: false, g => g.maybe_2);
        }

        private static void DetailRow(GraphBuilder b, string id, Func<string> text)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[] { new NodeAnnouncement(text, kind: AnnouncementKinds.Label) },
            });
        }

        private static GClass361 MetaOf(CustomPuzzleScreen s)
        {
            if (s == null) return null;
            var sel = Selected(s);
            return sel.method_0() ? Meta(sel.method_2()) : null;
        }

        private static string SelectedTitle(CustomPuzzleScreen s)
        {
            var m = MetaOf(s);
            return m == null ? null : (ScriptError(m) ? GameText.T("Script error") : Text(m.locString_0));
        }

        private static string ErrorText(CustomPuzzleScreen s)
        {
            try
            {
                var m = MetaOf(s);
                return m != null && m.maybe_0.method_0() ? GameText.Speech(m.maybe_0.method_2()) : null;
            }
            catch { return null; }
        }

        private static bool TestEnabled(CustomPuzzleScreen s) => !ScriptError(MetaOf(s));

        private static bool UploadEnabled(CustomPuzzleScreen s)
        {
            var sel = s != null ? Selected(s) : (Maybe<Puzzle>)GStruct10.gstruct10_0;
            return sel.method_0() && PersonalSolved(sel.method_2());
        }

        // ---- the game's own handlers ----

        private static void SetTab(int index)
        {
            var s = Manager;
            if (s == null || Tab(s) == index) return;
            try { SetTabMethod.Invoke(s, new object[] { (GEnum145)index }); }
            catch (Exception ex) { Log.Error("[custom] tab switch failed", ex); }
        }

        private static void SelectRow(Puzzle p)
        {
            var s = Manager;
            if (s == null || IsSelected(s, p)) return;
            try
            {
                try { GClass45.soundsNamespace_0.sound_43.smethod_1(1f); } catch { } // the arrow sound
                SelectMethod.Invoke(s, new object[] { p });
            }
            catch (Exception ex) { Log.Error("[custom] select failed", ex); }
        }

        private static void PlayRow(Puzzle p)
        {
            var s = Manager;
            if (s == null) return;
            try
            {
                SelectMethod.Invoke(s, new object[] { p });
                if (ScriptError(Meta(p)))
                {
                    // The game's Enter does nothing here; say why (its own drawn flag).
                    Speech.Tts.Speak(GameText.T("Script error"), interrupt: true);
                    return;
                }
                TestMethod.Invoke(s, null); // the double-click path: open in the editor
            }
            catch (Exception ex) { Log.Error("[custom] play failed", ex); }
        }

        private void DeleteRow(Puzzle p)
        {
            var s = Manager;
            if (s == null) return;
            try
            {
                string name = RowLabel(p);
                SelectMethod.Invoke(s, new object[] { p });
                DeleteMethod.Invoke(s, null); // moves the script to the deleted folder, no confirm
                _lastSelected = null;
                Speech.Tts.Speak(Loc.T("custom.deleted", new { name }), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[custom] delete failed", ex); }
        }

        private void CreateNetwork()
        {
            var s = Manager;
            if (s == null) return;
            try
            {
                CreateMethod.Invoke(s, null); // writes the template script, selects it
                FocusSelected(s);             // the row announce is the feedback
            }
            catch (Exception ex) { Log.Error("[custom] create failed", ex); }
        }

        private static void BrowseWorkshop()
        {
            try { GameLogic.gameLogic_0.gclass287_0.method_8(); } // the Steam overlay
            catch (Exception ex) { Log.Error("[custom] workshop failed", ex); }
        }

        private static void EditSelected()
        {
            var s = Manager;
            var sel = s != null ? Selected(s) : (Maybe<Puzzle>)GStruct10.gstruct10_0;
            if (!sel.method_0())
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                return;
            }
            try
            {
                EditMethod.Invoke(s, null); // the OS file browser, script selected — zero in-game feedback
                string path = null;
                try { path = ScriptPathMethod?.Invoke(GameLogic.gameLogic_0.gclass287_0, new object[] { sel.method_2() }) as string; }
                catch { }
                if (!string.IsNullOrEmpty(path))
                    Speech.Tts.Speak(Loc.T("custom.edit.opened", new { path }), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[custom] edit failed", ex); }
        }

        private static void TestSelected()
        {
            var s = Manager;
            if (s == null || !TestEnabled(s))
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                return;
            }
            try { TestMethod.Invoke(s, null); }
            catch (Exception ex) { Log.Error("[custom] test failed", ex); }
        }

        private static void UploadSelected()
        {
            var s = Manager;
            if (s == null || !UploadEnabled(s))
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                return;
            }
            try { UploadMethod.Invoke(s, null); } // pushes the game's Uploading dialog
            catch (Exception ex) { Log.Error("[custom] upload failed", ex); }
        }

        private void CopySelected()
        {
            var s = Manager;
            if (s == null || !Selected(s).method_0())
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                return;
            }
            try
            {
                CopyMethod.Invoke(s, null); // duplicate the script + select the copy
                FocusSelected(s);
            }
            catch (Exception ex) { Log.Error("[custom] copy failed", ex); }
        }

        private void DeleteSelected()
        {
            var s = Manager;
            var sel = s != null ? Selected(s) : (Maybe<Puzzle>)GStruct10.gstruct10_0;
            if (!sel.method_0())
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                return;
            }
            DeleteRow(sel.method_2());
        }

        private static void CloseWindow()
        {
            var s = Manager;
            if (s == null) return;
            try { CloseMethod.Invoke(s, null); } // the X: sound + slide-out + pop
            catch (Exception ex) { Log.Error("[custom] close failed", ex); }
        }

        private void FocusSelected(CustomPuzzleScreen s)
        {
            var sel = Selected(s);
            _lastSelected = sel.method_0() ? sel.method_2().ID : null;
            if (sel.method_0()) Navigation.FocusNode(RowId(sel.method_2()));
        }

        // ---- selection changes we didn't drive (a mouse click, the game's own arrows,
        // a create/copy landing) follow into focus while the rows stop is focused. ----

        private object _instance;
        private string _lastSelected;

        public override void OnUpdate()
        {
            var s = Manager;
            if (s == null) return;
            var sel = Selected(s);
            string id = sel.method_0() ? sel.method_2().ID : null;
            if (!ReferenceEquals(_instance, s))
            {
                _instance = s;
                _lastSelected = id;
                return;
            }
            if (id == _lastSelected) return;
            _lastSelected = id;
            if (id == null || !"networks".Equals(Navigation.FocusedStopKey)) return;
            var rowId = RowId(sel.method_2());
            var nav = Navigation.Active as GraphNavigator;
            if (nav != null && !rowId.Equals(nav.FocusedNodeId))
                Navigation.FocusNode(rowId);
        }
    }

    /// <summary>The personal row's context menu (deob GClass245, obfuscated live) — opened only
    /// by the mouse (the row's menu button), so a keyboard user reaches its verbs through the
    /// manager's actions stop instead; modeled so an opened menu is navigable rather than a
    /// silent trap: five rows under the game's own labels, Test/Upload dimmed as drawn, Enter
    /// runs the menu's own pop-then-act path. Escape stays native (the menu's own close).</summary>
    public sealed class NetworkMenuScreen : Screen
    {
        public override string Key => "custom.menu";
        public override string ScreenName => Loc.T("custom.menu");
        public override bool IsActive() => GameState.TopScreen() is GClass245;

        private static readonly FieldInfo TestEnabledField = Deobf.Field(typeof(GClass245), "bool_1");
        private static readonly FieldInfo UploadEnabledField = Deobf.Field(typeof(GClass245), "bool_2");
        private static readonly FieldInfo[] ActionFields =
        {
            Deobf.Field(typeof(GClass245), "action_0"),
            Deobf.Field(typeof(GClass245), "action_1"),
            Deobf.Field(typeof(GClass245), "action_2"),
            Deobf.Field(typeof(GClass245), "action_3"),
            Deobf.Field(typeof(GClass245), "action_4"),
        };
        private static readonly MethodInfo PopMethod = Deobf.Method(typeof(GClass245), "method_0");
        private static readonly string[] Labels = { "Edit", "Test", "Upload", "Copy", "Delete" };

        private static GClass245 Menu => GameState.TopScreen() as GClass245;

        private static bool Enabled(int index)
        {
            try
            {
                var m = Menu;
                if (m == null) return false;
                if (index == 1) return (bool)TestEnabledField.GetValue(m);
                if (index == 2) return (bool)UploadEnabledField.GetValue(m);
                return true;
            }
            catch { return false; }
        }

        public override void Build(GraphBuilder b)
        {
            if (Menu == null) return;
            b.BeginStop("menu");
            for (int i = 0; i < Labels.Length; i++)
            {
                int index = i;
                string key = Labels[i];
                b.AddItem(ControlId.Structural("cn.menu." + i), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => GameText.T(key), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => Enabled(index) ? null : Loc.T("value.unavailable"),
                            kind: AnnouncementKinds.Enabled),
                    },
                    OnActivate = () => Activate(index),
                });
            }
        }

        private static void Activate(int index)
        {
            var m = Menu;
            if (m == null) return;
            if (!Enabled(index))
            {
                Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                return;
            }
            try
            {
                var action = ActionFields[index]?.GetValue(m) as Action;
                PopMethod.Invoke(m, null); // the menu closes first, then acts — its own order
                action?.Invoke();
            }
            catch (Exception ex) { Log.Error("[custom] menu action failed", ex); }
        }
    }

    /// <summary>The Workshop upload dialog (deob GClass277, obfuscated live): "Uploading" with an
    /// animated ellipsis while the manager works, closing itself on success; on failure the
    /// manager's error text with an art-only OK button (Escape works too). Announce-only while
    /// uploading; the error speaks once as it lands and becomes a row with a close button.</summary>
    public sealed class NetworkUploadScreen : Screen
    {
        public override string Key => "custom.upload";
        public override string ScreenName => GameText.Speech(GameText.T("Uploading"));
        public override bool IsActive() => GameState.TopScreen() is GClass277;

        private static string ErrorText()
        {
            try
            {
                var err = GameLogic.gameLogic_0.gclass287_0.method_2();
                return err.method_0() ? GameText.Speech(err.method_2()) : null;
            }
            catch { return null; }
        }

        public override void Build(GraphBuilder b)
        {
            string error = ErrorText();
            if (error == null) return;
            b.BeginStop("error");
            b.AddItem(ControlId.Structural("cn.up.error"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[] { new NodeAnnouncement(() => ErrorText(), kind: AnnouncementKinds.Label) },
            });
            b.AddItem(ControlId.Structural("cn.up.close"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[] { new NodeAnnouncement(() => Loc.T("custom.close"), kind: AnnouncementKinds.Label) },
                OnActivate = () =>
                {
                    try { GameLogic.gameLogic_0.gclass287_0.method_22(); } // the OK button: clear the error, the dialog pops itself
                    catch (Exception ex) { Log.Error("[custom] upload close failed", ex); }
                },
            });
        }

        private string _spokenError;

        public override void OnFocus()
        {
            base.OnFocus();
            _spokenError = null;
        }

        public override void OnUpdate()
        {
            string error = ErrorText();
            if (error == null || error == _spokenError) return;
            _spokenError = error;
            Speech.Tts.Speak(error);
        }
    }
}

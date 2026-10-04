using System;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.UI;
using Echopunks.UI.Graph;
using SDL2;

namespace Echopunks.Screens
{
    /// <summary>
    /// The TEC Constellation II control panel — TYPED against the game via the remap pipeline. One
    /// game screen, three mod screens keyed to its private page field (home / Options / Controls) so
    /// page flips announce as screen changes and each page keeps its own focus memory. Widgets
    /// mirror the decompiled originals exactly: radios write the config cell plus the same apply
    /// call the game's button makes; sliders write the cell plus the live mixer statics; key
    /// bindings push the game's own capture subscreen. Labels come from the game (GameText); ours
    /// only where the game has no string. Native mouse + Escape handling untouched throughout.
    /// </summary>
    public abstract class ControlPanelPageScreen : Screen
    {
        private readonly int _page;

        protected ControlPanelPageScreen(int page)
        {
            _page = page;
            Wrap = true; // small closed screens: Tab cycles tabs → content → back → tabs
        }

        public override bool IsActive() => PanelState.Page == _page;

        // Covered by the key-capture overlay (or a page flip): keep focus/tab memory.
        public override bool KeepStateOnPop => true;

        /// <summary>The game's settings object (typed).</summary>
        protected static GClass17 S => GameLogic.gameLogic_0?.gclass17_0;

        // ---- shared widget declarations ----

        protected static void Button(GraphBuilder b, string id, Func<string> label, Action activate)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[] { new NodeAnnouncement(label, kind: AnnouncementKinds.Label) },
                OnActivate = activate,
            });
        }

        /// <summary>One option of a mutually-exclusive pair/list (the panel's standard widget).</summary>
        protected static void Radio(GraphBuilder b, string id, Func<string> label,
            Func<bool> selected, Action activate, Func<bool> enabled = null)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Safe(selected) ? Loc.T("value.selected") : null,
                        live: true, kind: AnnouncementKinds.Selected),
                    new NodeAnnouncement(() => enabled == null || Safe(enabled) ? null : Loc.T("value.unavailable"),
                        kind: AnnouncementKinds.Enabled),
                },
                StateText = () => Safe(selected) ? Loc.T("value.selected") : null,
                OnActivate = () =>
                {
                    if (enabled != null && !Safe(enabled)) { Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true); return; }
                    activate();
                },
            });
        }

        /// <summary>A labeled row holding a pair of radio options. Label/option texts are GAME loc keys.</summary>
        protected static void RadioRow(GraphBuilder b, string rowKey, string gameLabelKey,
            string idA, string gameOptKeyA, Func<bool> selA, Action actA,
            string idB, string gameOptKeyB, Func<bool> selB, Action actB,
            Func<bool> enabled = null)
        {
            b.PushContext(GameText.TSpeech(gameLabelKey));
            b.StartRow(rowKey);
            Radio(b, idA, () => GameText.TSpeech(gameOptKeyA), selA, actA, enabled);
            Radio(b, idB, () => GameText.TSpeech(gameOptKeyB), selB, actB, enabled);
            b.EndRow();
            b.PopContext();
        }

        /// <summary>A 0..1 volume slider — the setter must also mirror the live mixer value, exactly
        /// like the game's slider (see the call sites).</summary>
        protected static void VolumeSlider(GraphBuilder b, string id, string gameLabelKey,
            Func<float> get, Action<float> set)
        {
            Func<string> percent = () => Loc.T("value.percent", new { value = Math.Round(Safe(get) * 100f) });
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T(gameLabelKey), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(percent, kind: AnnouncementKinds.Value),
                },
                StateText = percent,
                OnAdjust = (sign, large) =>
                {
                    float step = large ? 0.1f : 0.05f;
                    float v = Math.Max(0f, Math.Min(1f, Safe(get) + sign * step));
                    set(v);
                },
            });
        }

        protected static void TextRow(GraphBuilder b, string id, Func<string> label, Func<string> value)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(value, kind: AnnouncementKinds.Value),
                },
            });
        }

        /// <summary>The tab strip (one horizontal row; activating a tab switches the panel's tab
        /// field, which rebuilds the content stop in place).</summary>
        protected static void Tabs(GraphBuilder b, params (string id, Func<string> label, int index)[] tabs)
        {
            b.BeginStop("tabs");
            b.StartRow();
            foreach (var t in tabs)
            {
                int index = t.index;
                var label = t.label;
                b.AddItem(ControlId.Structural(t.id), new NodeVtable
                {
                    ControlType = ControlTypes.Tab,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    },
                    // Selection follows focus, so "selected" is never spoken on tabs — the engine
                    // still needs the state for stop landings.
                    Selected = () => PanelState.Tab == index,
                    OnActivate = () => PanelState.SetTab(index),
                    OnSelect = () => PanelState.SetTab(index), // tabs select as you arrow over them
                });
            }
            b.EndRow();
        }

        protected static void BackButton(GraphBuilder b)
        {
            b.BeginStop("back");
            Button(b, "panel.back", () => GameText.T("Back"), () => PanelState.SetPage(0));
        }

        protected static bool Safe(Func<bool> f) { try { return f(); } catch { return false; } }
        protected static float Safe(Func<float> f) { try { return f(); } catch { return 0f; } }
    }

    /// <summary>The panel's private page/tab state — the ONE place typed access can't reach (private
    /// fields), resolved by deob name through the namemap (see Game/Deobf).</summary>
    internal static class PanelState
    {
        private static readonly FieldInfo PageField = Deobf.Field(typeof(ControlPanelScreen), "enum6_0");
        private static readonly FieldInfo TabField = Deobf.Field(typeof(ControlPanelScreen), "int_0");

        /// <summary>The live panel instance anywhere on the game's screen stack, else null.</summary>
        public static ControlPanelScreen Panel
        {
            get
            {
                var stack = GameState.ScreenStack();
                if (stack == null) return null;
                for (int i = stack.Count - 1; i >= 0; i--)
                    if (stack[i] is ControlPanelScreen p) return p;
                return null;
            }
        }

        /// <summary>0 = home, 1 = Options, 2 = Controls; -1 when the panel isn't up.</summary>
        public static int Page => PageOf(Panel);

        public static int Tab => TabOf(Panel);

        public static int PageOf(ControlPanelScreen p)
        {
            if (p == null || PageField == null) return -1;
            try { return Convert.ToInt32(PageField.GetValue(p)); } catch { return -1; }
        }

        public static int TabOf(ControlPanelScreen p)
        {
            if (p == null || TabField == null) return -1;
            try { return (int)TabField.GetValue(p); } catch { return -1; }
        }

        /// <summary>Exactly what the game's page buttons do: assign the fields (page flips reset the tab).</summary>
        public static void SetPage(int page)
        {
            var p = Panel;
            if (p == null || PageField == null || TabField == null) return;
            try
            {
                PageField.SetValue(p, Enum.ToObject(PageField.FieldType, page));
                TabField.SetValue(p, 0);
            }
            catch (Exception ex) { Log.Error("[panel] SetPage failed", ex); }
        }

        public static void SetTab(int tab)
        {
            var p = Panel;
            if (p == null || TabField == null) return;
            try { TabField.SetValue(p, tab); }
            catch (Exception ex) { Log.Error("[panel] SetTab failed", ex); }
        }
    }

    /// <summary>Page 0: the panel's home — Options / Controls / Exit, the clock, and Close.</summary>
    public sealed class ControlPanelHomeScreen : ControlPanelPageScreen
    {
        public ControlPanelHomeScreen() : base(0) { }
        public override string Key => "panel.home";
        public override string ScreenName => Loc.T("screen.controlpanel");

        public override void Build(GraphBuilder b)
        {
            Button(b, "panel.options", () => GameText.T("Options"), () => PanelState.SetPage(1));
            Button(b, "panel.controls", () => GameText.T("Controls"), () => PanelState.SetPage(2));
            Button(b, "panel.exit", () => GameText.T("Exit Game"), () => GameApi.QuitGame());
            Button(b, "panel.close", () => Loc.T("panel.close"), () => GameApi.PopScreen());
            TextRow(b, "panel.clock", () => Loc.T("panel.clock"), () => DateTime.Now.ToString("h:mm tt"));
        }
    }

    /// <summary>Page 1: Options — Display / Sound / Interface / Network tabs, plus the mod's own Mod
    /// tab (drawn by Patches/OptionsInjection with the game's widgets).</summary>
    public sealed class ControlPanelOptionsScreen : ControlPanelPageScreen
    {
        public ControlPanelOptionsScreen() : base(1) { }
        public override string Key => "panel.options";
        public override string ScreenName => GameText.T("Options");

        public override void Build(GraphBuilder b)
        {
            Tabs(b,
                ("tab.display", () => GameText.T("Display"), 0),
                ("tab.sound", () => GameText.T("Sound"), 1),
                ("tab.interface", () => GameText.T("Interface"), 2),
                ("tab.network", () => GameText.T("Network"), 3),
                ("tab.mod", () => Loc.T("panel.tab.mod"), Patches.OptionsInjection.ModTab));

            b.BeginStop("content");
            switch (PanelState.Tab)
            {
                case 0: BuildDisplay(b); break;
                case 1: BuildSound(b); break;
                case 2: BuildInterface(b); break;
                case 3: BuildNetwork(b); break;
                case Patches.OptionsInjection.ModTab: BuildMod(b); break;
            }
            BackButton(b);
        }

        // The mod's own settings (ModOptions — the same list the drawn Mod tab renders).
        private static void BuildMod(GraphBuilder b)
        {
            foreach (var row in ModOptions.Rows)
            {
                b.PushContext(row.Label());
                b.StartRow("opts");
                foreach (var opt in row.Options)
                    Radio(b, "mod." + opt.Id, opt.Label, opt.Selected, opt.Select);
                b.EndRow();
                b.PopContext();
            }
        }

        private static void BuildDisplay(GraphBuilder b)
        {
            Func<bool> fullscreen = () => S.gclass52_4.method_0();
            RadioRow(b, "opts", "Display Mode",
                "disp.fullscreen", "Fullscreen", fullscreen,
                () => { S.gclass52_4.method_2(true); GameLogic.gameLogic_0.method_40(bool_7: true); },
                "disp.windowed", "Windowed", () => !Safe(fullscreen),
                () => { S.gclass52_4.method_2(false); GameLogic.gameLogic_0.method_40(bool_7: false); });

            // Window sizes: selectable only while windowed and only when they fit the desktop.
            b.PushContext(GameText.T("Window Size"));
            var resolutions = GameLogic.index2_0;
            for (int i = 0; i < resolutions.Length; i += 2)
            {
                b.StartRow("res");
                for (int j = i; j < Math.Min(i + 2, resolutions.Length); j++)
                {
                    var res = resolutions[j];
                    string label = res.int_0 + " x " + res.int_1;
                    Radio(b, "res." + label,
                        () => label,
                        () => !Safe(fullscreen) && S.method_2() == res,
                        () => { S.method_3(res); GameLogic.gameLogic_0.bool_0 = true; },
                        () => !Safe(fullscreen) && GameLogic.gameLogic_0.method_42(res));
                }
                b.EndRow();
            }
            b.PopContext();

            Func<bool> capable = () => GameLogic.gameLogic_0.method_0();
            Func<bool> low = () => S.gclass52_18.method_0();
            RadioRow(b, "opts", "Display Quality",
                "qual.high", "High (4K)", () => Safe(capable) && !Safe(low),
                () => GameLogic.gameLogic_0.method_43(bool_7: false),
                "qual.low", "Low (2K)", () => Safe(low) || !Safe(capable),
                () => GameLogic.gameLogic_0.method_43(bool_7: true),
                capable);
        }

        private static void BuildSound(GraphBuilder b)
        {
            // The game's slider writes the cell AND the live mixer static — mirror both.
            VolumeSlider(b, "vol.sfx", "SFX Volume",
                () => S.gclass52_5.method_0(), v => { S.gclass52_5.method_2(v); GClass1.float_1 = v; });
            VolumeSlider(b, "vol.voice", "Voiceover Volume",
                () => S.gclass52_6.method_0(), v => { S.gclass52_6.method_2(v); GClass1.float_2 = v; });
            VolumeSlider(b, "vol.music", "Music Volume",
                () => S.gclass52_7.method_0(), v => { S.gclass52_7.method_2(v); GClass1.float_0 = v; });
        }

        private static void BuildInterface(GraphBuilder b)
        {
            TextRow(b, "iface.hostname", () => GameText.T("Hostname"),
                () => GameLogic.gameLogic_0.saveData_0.method_32());

            RadioRow(b, "opts", "Profanity",
                "prof.show", "Show", () => !S.gclass52_15.method_0(), () => S.gclass52_15.method_2(false),
                "prof.hide", "Hide", () => S.gclass52_15.method_0(), () => S.gclass52_15.method_2(true));

            RadioRow(b, "opts", "Mouse Cursor",
                "cur.hw", "Hardware", () => !S.gclass52_9.method_0(), () => S.gclass52_9.method_2(false),
                "cur.sw", "Software", () => S.gclass52_9.method_0(), () => S.gclass52_9.method_2(true));

            // The game's buttons also flip the live large-fonts flag.
            RadioRow(b, "opts", "Code Font Size",
                "font.normal", "Normal", () => !S.gclass52_17.method_0(),
                () => { S.gclass52_17.method_2(false); GClass1.bool_2 = false; },
                "font.larger", "Larger", () => S.gclass52_17.method_0(),
                () => { S.gclass52_17.method_2(true); GClass1.bool_2 = true; });

            RadioRow(b, "opts", "HACK\\*MATCH\nCRT Effect",
                "crt.normal", "Normal", () => S.gclass52_16.method_0(), () => S.gclass52_16.method_2(true),
                "crt.off", "No Distortion", () => !S.gclass52_16.method_0(), () => S.gclass52_16.method_2(false));
        }

        private static void BuildNetwork(GraphBuilder b)
        {
            RadioRow(b, "opts", "Histograms",
                "hist.show", "Show", () => S.gclass52_10.method_0(), () => S.gclass52_10.method_2(true),
                "hist.hide", "Hide", () => !S.gclass52_10.method_0(), () => S.gclass52_10.method_2(false));

            Func<bool> boards = () => S.gclass52_11.method_0();
            RadioRow(b, "opts", "Leaderboards",
                "lead.show", "Show", boards, () => S.gclass52_11.method_2(true),
                "lead.hide", "Hide", () => !Safe(boards), () => S.gclass52_11.method_2(false));

            RadioRow(b, "opts", "Top Percentile",
                "top.show", "Show", () => Safe(boards) && S.gclass52_13.method_0(), () => S.gclass52_13.method_2(true),
                "top.hide", "Hide", () => !S.gclass52_13.method_0() || !Safe(boards), () => S.gclass52_13.method_2(false),
                boards);

            RadioRow(b, "opts", "Tenth Percentile",
                "tenth.show", "Show", () => Safe(boards) && S.gclass52_14.method_0(), () => S.gclass52_14.method_2(true),
                "tenth.hide", "Hide", () => !S.gclass52_14.method_0() || !Safe(boards), () => S.gclass52_14.method_2(false),
                boards);

            RadioRow(b, "opts", "Multiplayer",
                "mp.on", "Enable", () => S.gclass52_12.method_0(), () => S.gclass52_12.method_2(true),
                "mp.off", "Disable", () => !S.gclass52_12.method_0(), () => S.gclass52_12.method_2(false));
        }
    }

    /// <summary>Page 2: Controls — the Redshift gamepad key bindings + the keyboard reference.</summary>
    public sealed class ControlPanelControlsScreen : ControlPanelPageScreen
    {
        public ControlPanelControlsScreen() : base(2) { }
        public override string Key => "panel.controls";
        public override string ScreenName => GameText.T("Controls");

        private GClass52<SDL.GEnum195> _pendingCapture;

        // The gamepad art labels these visually; the game has no strings for them — ours (ui.json).
        private static (string id, string labelKey, Func<GClass52<SDL.GEnum195>> cell)[] Bindings => new (string, string, Func<GClass52<SDL.GEnum195>>)[]
        {
            ("bind.up", "bind.up", () => S.gclass52_19),
            ("bind.down", "bind.down", () => S.gclass52_20),
            ("bind.left", "bind.left", () => S.gclass52_21),
            ("bind.right", "bind.right", () => S.gclass52_22),
            ("bind.x", "bind.x", () => S.gclass52_23),
            ("bind.y", "bind.y", () => S.gclass52_24),
            ("bind.z", "bind.z", () => S.gclass52_25),
            ("bind.start", "bind.start", () => S.gclass52_26),
        };

        // The reference table IS game text (both columns; these Windows strings are the loc keys).
        private static readonly (string id, string labelKey, string valueKey)[] Reference =
        {
            ("ref.sim", "Reset / Pause / Step / Run / Fast", "Escape / F3 / Tab / F4 / F5"),
            ("ref.runto", "Run to Instruction (Any EXA / This EXA)", "Alt + Click / Shift + Alt + Click"),
            ("ref.goal", "Show Goal", "F1"),
            ("ref.exawin", "Previous / Next EXA Window", "Ctrl + Up / Ctrl + Down"),
            ("ref.newexa", "Create New EXA", "Ctrl + Enter"),
            ("ref.clipboard", "Cut / Copy / Paste", "Ctrl + X / Ctrl + C / Ctrl + V"),
            ("ref.undo", "Undo / Redo", "Ctrl + Z / Ctrl + Y"),
        };

        public override void Build(GraphBuilder b)
        {
            Tabs(b,
                ("tab.redshift", () => GameText.TSpeech("Redshift / HACK\\*MATCH"), 0),
                ("tab.othercontrols", () => GameText.T("Other Controls"), 1));

            b.BeginStop("content");
            if (PanelState.Tab == 0)
            {
                b.PushContext(Loc.T("controls.gamepad"));
                foreach (var (id, labelKey, cell) in Bindings)
                {
                    var getCell = cell;
                    string label = labelKey;
                    b.AddItem(ControlId.Structural(id), new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => Loc.T(label), kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => SDL.SDL_GetKeyName(getCell().method_0()),
                                live: true, kind: AnnouncementKinds.Value),
                        },
                        OnActivate = () => _pendingCapture = getCell(),
                    });
                }
                b.PopContext();
            }
            else
            {
                b.PushContext(Loc.T("controls.reference"));
                foreach (var (id, labelKey, valueKey) in Reference)
                {
                    string lk = labelKey, vk = valueKey;
                    TextRow(b, id, () => GameText.TSpeech(lk), () => GameText.TSpeech(vk));
                }
                b.PopContext();
            }
            BackButton(b);
        }

        // The game's capture screen binds the first HELD key it polls — pushing it while our
        // activating Enter is still down would instantly bind Return. Defer until all keys are up.
        public override void OnUpdate()
        {
            if (_pendingCapture == null || Input.SdlKeyboard.AnyKeyHeld) return;
            var cell = _pendingCapture;
            _pendingCapture = null;
            GameApi.PushScreen(new GClass21(cell));
        }
    }

    /// <summary>The game's key-capture overlay (GClass21, typed): announce the prompt and stand our
    /// input down — every key must reach the game's raw capture (Escape cancels natively).</summary>
    public sealed class GameKeyCaptureScreen : Screen
    {
        public override string Key => "panel.keycapture";
        public override int Layer => 10;
        public override string ScreenName => Loc.T("capture.prompt");
        public override bool CapturesRawInput => true;

        public override bool IsActive() => GameState.TopScreen() is GClass21;
    }
}

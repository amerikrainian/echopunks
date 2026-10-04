using System;
using System.Collections.Generic;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.Screens;
using HarmonyLib;

namespace Echopunks.Patches
{
    /// <summary>
    /// The control panel's Options page gains a fifth tab, Mod, drawn with the game's OWN widgets.
    /// The panel is immediate-mode: imethod_1 calls three private helpers per frame — method_0
    /// (the tab strip: label array + tab width, click = select), method_1 (the labeled row boxes)
    /// and method_2 (a radio button: selected, label, position, enabled, click action). Nothing to
    /// subclass or register; we call the same helpers.
    ///
    /// A prefix on method_0 swaps the Options strip's static 4-label array for a 5-label copy and
    /// narrows the tab width so the strip keeps its drawn span; the game's own loop then draws,
    /// hovers and click-selects the fifth tab (int_0 = 4). Its page switch has no case 4 — it
    /// draws nothing — so a postfix on method_0 (the call right before that switch: identical draw
    /// order) draws the Mod rows and radios from <see cref="ModOptions"/>. Clicks play the game's
    /// radio sound (sound_44, as its method_3 does). Unpatched = a 4-tab strip again; a panel left
    /// on tab 4 then just shows an empty page until another tab is clicked.
    /// </summary>
    internal static class OptionsInjection
    {
        public const int ModTab = 4;

        // Layout constants of the decompiled panel (ControlPanelScreen.imethod_1 / method_0 / method_1).
        private const float TabGap = 6f;
        private const float RowsTop = 1537f, RowsHeight = 878f, RowGap = 30f;
        private static readonly float[] OptionColumns = { 1734f, 2151f };

        private static readonly FieldInfo OptionsTabs = Deobf.Field(typeof(ControlPanelScreen), "locString_0");
        private static readonly MethodInfo TabRow = Deobf.Method(typeof(ControlPanelScreen), "method_0");
        private static readonly MethodInfo LabelRows = Deobf.Method(typeof(ControlPanelScreen), "method_1");
        private static readonly MethodInfo RadioButton = Deobf.Method(typeof(ControlPanelScreen), "method_2");

        public static void Apply(Harmony harmony)
        {
            try
            {
                if (OptionsTabs == null || TabRow == null || LabelRows == null || RadioButton == null)
                {
                    Log.Error("[patch] options Mod tab: panel members not resolved; tab not added");
                    return;
                }
                harmony.Patch(TabRow,
                    prefix: new HarmonyMethod(typeof(OptionsInjection), nameof(TabRowPrefix)),
                    postfix: new HarmonyMethod(typeof(OptionsInjection), nameof(TabRowPostfix)));
                Log.Info("[patch] options Mod tab armed");
            }
            catch (Exception ex) { Log.Error("[patch] options Mod tab failed to apply", ex); }
        }

        // The game draws LocString.method_2() (current language). Our texts are already localized,
        // so each becomes a LocString carrying it in every language slot (GClass7.smethod_6 — the
        // game's own wrapper for literal text such as "1920 x 1080"). Cached by text.
        private static readonly Dictionary<string, LocString> _locs = new Dictionary<string, LocString>();

        private static LocString Text(string s)
        {
            s = s ?? string.Empty;
            LocString loc;
            if (!_locs.TryGetValue(s, out loc))
            {
                if (_locs.Count > 64) _locs.Clear(); // language flips mint new texts; never grows unbounded
                loc = GClass7.smethod_6(s);
                _locs[s] = loc;
            }
            return loc;
        }

        private static void TabRowPrefix(ref LocString[] __0, ref float __1)
        {
            try
            {
                if (__0 == null || !ReferenceEquals(__0, OptionsTabs.GetValue(null))) return; // Controls strip
                int n = __0.Length + 1;
                float span = __0.Length * __1 + (__0.Length - 1) * TabGap;
                var tabs = new LocString[n];
                Array.Copy(__0, tabs, __0.Length);
                tabs[n - 1] = Text(Loc.T("panel.tab.mod"));
                __0 = tabs;
                __1 = (span - (n - 1) * TabGap) / n;
            }
            catch { }
        }

        private static void TabRowPostfix(ControlPanelScreen __instance)
        {
            try
            {
                if (PanelState.PageOf(__instance) != 1 || PanelState.TabOf(__instance) != ModTab) return;
                var rows = ModOptions.Rows;
                var boxes = new Tuple<LocString, int>[rows.Length];
                for (int i = 0; i < rows.Length; i++) boxes[i] = Tuple.Create(Text(rows[i].Label()), 1);
                LabelRows.Invoke(__instance, new object[] { boxes });

                // method_1's geometry (every row weight 1): boxes stack down from RowsTop, sharing
                // the fixed height after the gaps. Radios sit vertically centered in their box.
                float h = (RowsHeight - RowGap * (rows.Length + 1)) / rows.Length;
                float radioH = GClass45.gclass111_0.gclass177_0.gclass179_0.texture_6.vector2_0.float_1;
                float y = RowsTop;
                foreach (var row in rows)
                {
                    y -= h + RowGap;
                    float top = y + (h - radioH) / 2f;
                    for (int j = 0; j < row.Options.Length && j < OptionColumns.Length; j++)
                    {
                        var opt = row.Options[j];
                        Action click = () =>
                        {
                            try { GClass45.soundsNamespace_0.sound_44.smethod_1(1f); } catch { }
                            opt.Select();
                        };
                        RadioButton.Invoke(__instance, new object[]
                        {
                            opt.Selected(), Text(opt.Label()), new Vector2(OptionColumns[j], top), true, click,
                        });
                    }
                }
            }
            catch (Exception ex) { LogOnce(ex); }
        }

        private static bool _logged;

        private static void LogOnce(Exception ex)
        {
            if (_logged) return;
            _logged = true;
            Log.Error("[patch] options Mod tab draw failed", ex);
        }
    }
}

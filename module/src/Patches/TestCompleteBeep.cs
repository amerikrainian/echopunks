using System;
using System.Reflection;
using Echopunks.Game;
using HarmonyLib;

namespace Echopunks.Patches
{
    /// <summary>
    /// A progress beep each time a test run completes in the editor, pitched by how far through
    /// the run's tests it is (Audio/Tones.Progress — NVDA's progress-bar scale). The seam is
    /// EditorScreen.method_21, the game's "this test is finished" bookkeeping (records cycles and
    /// activity, or a battle round's win/draw/loss): it runs once the sim reports solved, from
    /// both the free-run path (followed by method_22's advance to the next test) and the paused
    /// stepping path, and its bool_3 guard makes repeat calls for the same test no-ops — so the
    /// prefix beeps exactly when that guard is still open. int_0 = tests completed earlier in this
    /// run; GClass68.int_0 = tests per run (100). Gated by <see cref="TestBeeps"/>.
    /// </summary>
    internal static class TestCompleteBeep
    {
        private static readonly FieldInfo Recorded = Deobf.Field(typeof(EditorScreen), "bool_3");
        private static readonly FieldInfo Completed = Deobf.Field(typeof(EditorScreen), "int_0");
        private static readonly FieldInfo SimField = Deobf.Field(typeof(EditorScreen), "sim_0");

        public static void Apply(Harmony harmony)
        {
            try
            {
                var target = Deobf.Method(typeof(EditorScreen), "method_21");
                if (target == null || Recorded == null || Completed == null || SimField == null)
                {
                    Log.Error("[patch] test-complete beep: editor members not resolved");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(TestCompleteBeep), nameof(Prefix)));
                Log.Info("[patch] test-complete beep armed");
            }
            catch (Exception ex) { Log.Error("[patch] test-complete beep failed to apply", ex); }
        }

        private static void Prefix(EditorScreen __instance)
        {
            try
            {
                if (!TestBeeps.Enabled || (bool)Recorded.GetValue(__instance)) return;
                var sim = SimField.GetValue(__instance) as Sim;
                if (sim == null || !sim.method_47()) return; // the game would throw; not a completion
                int done = (int)Completed.GetValue(__instance) + 1;
                Audio.Tones.Progress((double)done / Math.Max(1, GClass68.int_0));
            }
            catch { }
        }
    }
}

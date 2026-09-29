using System;
using System.Collections.Generic;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.UI.Graph;

namespace Echopunks.Screens
{
    public sealed partial class PuzzleCompleteScreen
    {
        // ---- the Leaderboards view: the per-stat panel rows live in the shared
        // LeaderboardRows (the solution browser voices the same drawn panel for its selected
        // solution); this half keeps the completion screen's own gates — the Leaderboards/
        // Test Run Data toggle, and eligibility: an over-size solution speaks the game's
        // replacement notice instead of the stops. ----

        private static readonly FieldInfo ScoreDictField =
            Deobf.Field(typeof(PuzzleCompletionScreen), "dictionary_0");
        private static FieldInfo _sizeLimitField; // GClass361.int_2, resolved on first use

        private void BuildLeaderboards(GraphBuilder b, PuzzleCompletionScreen s)
        {
            try
            {
                // Rows mirror the SHOWN view: none while Test Run Data is up.
                if (TabField == null || (bool)TabField.GetValue(s)) return;
                var dict = ScoreDictField?.GetValue(s) as Dictionary<GEnum178, GClass296>;
                if (dict == null || dict.Count == 0) return;
                int size, limit;
                if (!Eligible(s, out size, out limit))
                {
                    b.BeginStop("leaderboards");
                    LeaderboardRows.AddRow(b, "lb.ineligible", Loc.T("lb.ineligible", new { size, limit }));
                    return;
                }
                // The completion layout: caption spoken, the marker = the run's score
                // (maybe_3 — the GEnum216 0 its own Theme call selects).
                LeaderboardRows.Build(b, dict.Values, caption: true, g => g.maybe_3);
            }
            catch (Exception ex) { Log.Error("[complete] leaderboards build failed", ex); }
        }

        private static bool Eligible(PuzzleCompletionScreen s, out int size, out int limit)
        {
            size = 0;
            limit = int.MaxValue;
            try
            {
                size = SizeField != null ? (int)SizeField.GetValue(s) : 0;
                var solution = SolutionField?.GetValue(s) as Solution;
                if (solution != null && PuzzleDetailsMethod != null)
                {
                    var details = PuzzleDetailsMethod.Invoke(null, new object[] { solution.method_0() });
                    if (details != null && _sizeLimitField == null)
                        _sizeLimitField = Deobf.Field(details.GetType(), "int_2");
                    if (details != null && _sizeLimitField != null)
                        limit = Convert.ToInt32(_sizeLimitField.GetValue(details));
                }
            }
            catch { }
            return size <= limit;
        }
    }
}

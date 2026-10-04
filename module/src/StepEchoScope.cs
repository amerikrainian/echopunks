namespace Echopunks
{
    /// <summary>
    /// Which EXAs the editor's step echo (F2, run-to arrivals) reads. FOCUSED (the default — the
    /// behaviour before this setting existed): "Cycle n" + the pending instruction of the EXA the
    /// code view follows. ALL: the visible enemy/NPC effects of the cycle just stepped, then
    /// "Cycle n" + the pending instruction of every live player EXA in window order. Persists in
    /// settings.json ("editor.stepEcho" = "focused" | "all"); set from the control panel's Mod tab or
    /// toggled anywhere with Ctrl+F2.
    /// </summary>
    public static class StepEchoScope
    {
        private const string Key = "editor.stepEcho";
        private static bool? _all;

        public static bool All
        {
            get
            {
                if (_all == null) _all = HostConfig.Get(Key, "focused") == "all";
                return _all.Value;
            }
            set
            {
                if (_all == value) return;
                _all = value;
                HostConfig.Set(Key, value ? "all" : "focused");
            }
        }

        /// <summary>Ctrl+F2: flip the scope, persist it, and say the new choice the way the Mod
        /// tab's row reads ("Step Narration, All EXAs").</summary>
        public static void Toggle()
        {
            All = !All;
            Speech.Tts.Speak(Localization.Loc.T("modopt.step") + ", "
                + Localization.Loc.T(All ? "modopt.step.all" : "modopt.step.focused"), interrupt: true);
        }

        /// <summary>Test seam: pin the scope without touching settings.json (null = re-read).</summary>
        internal static void ResetForTests(bool? all) => _all = all;
    }
}

namespace Echopunks
{
    /// <summary>
    /// Whether finishing a test run in the editor plays a progress beep (Patches/TestCompleteBeep).
    /// ON by default; persists in settings.json ("editor.testBeeps"); set from the control panel's
    /// Mod tab. Volume follows the game's SFX slider.
    /// </summary>
    public static class TestBeeps
    {
        private const string Key = "editor.testBeeps";
        private static bool? _enabled;

        public static bool Enabled
        {
            get
            {
                if (_enabled == null) _enabled = HostConfig.GetBool(Key, true);
                return _enabled.Value;
            }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                HostConfig.SetBool(Key, value);
            }
        }

        /// <summary>Test seam: pin the flag without touching settings.json (null = re-read).</summary>
        internal static void ResetForTests(bool? enabled) => _enabled = enabled;
    }
}

using Echopunks.Localization;

namespace Echopunks
{
    /// <summary>
    /// Whether text fields speak the characters typed into them. ON by default; F6 toggles it from
    /// anywhere (the game reads no F6), and the choice persists in settings.json
    /// ("speech.typingEcho"). Deletions always speak, and caret narration (arrows, Home/End) is
    /// not echo — neither is affected.
    /// </summary>
    public static class TypingEcho
    {
        private const string Key = "speech.typingEcho";
        private static bool? _enabled;

        public static bool Enabled
        {
            get
            {
                if (_enabled == null) _enabled = HostConfig.GetBool(Key, true);
                return _enabled.Value;
            }
        }

        public static void Toggle()
        {
            Set(!Enabled);
            Speech.Tts.Speak(Loc.T(_enabled.Value ? "text.echo.on" : "text.echo.off"), interrupt: true);
        }

        /// <summary>Silent set + persist (the control panel's Mod tab; its radios speak their own state).</summary>
        public static void Set(bool enabled)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            HostConfig.SetBool(Key, enabled);
        }

        /// <summary>Test seam: pin the flag without touching settings.json (null = re-read it on
        /// next use) — echo tests must not depend on the machine's saved F6 choice.</summary>
        internal static void ResetForTests(bool? enabled) => _enabled = enabled;
    }
}

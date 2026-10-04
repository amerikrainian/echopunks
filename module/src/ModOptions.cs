using System;
using Echopunks.Game;
using Echopunks.Localization;

namespace Echopunks
{
    /// <summary>One option of a mod setting — a radio button on the control panel's Mod tab.</summary>
    internal sealed class ModOption
    {
        public string Id;
        public Func<string> Label;
        public Func<bool> Selected;
        public Action Select;
    }

    /// <summary>A labeled row of mutually exclusive options (the panel's standard widget pair).</summary>
    internal sealed class ModOptionRow
    {
        public string Id;
        public Func<string> Label;
        public ModOption[] Options;
    }

    /// <summary>
    /// The mod's settings, as the control panel's Options page shows them on its Mod tab. ONE list
    /// feeds both halves: the drawn tab (Patches/OptionsInjection, the game's own row boxes and
    /// radio buttons) and the spoken one (ControlPanelOptionsScreen). A row = one game row box with
    /// up to two options in the game's two option columns. Option texts the game already has
    /// (Enable / Disable) are the game's own strings.
    /// </summary>
    internal static class ModOptions
    {
        public static readonly ModOptionRow[] Rows =
        {
            new ModOptionRow
            {
                Id = "echo",
                Label = () => Loc.T("modopt.echo"),
                Options = new[]
                {
                    new ModOption { Id = "echo.on", Label = () => GameText.T("Enable"),
                        Selected = () => TypingEcho.Enabled, Select = () => TypingEcho.Set(true) },
                    new ModOption { Id = "echo.off", Label = () => GameText.T("Disable"),
                        Selected = () => !TypingEcho.Enabled, Select = () => TypingEcho.Set(false) },
                },
            },
            new ModOptionRow
            {
                Id = "step",
                Label = () => Loc.T("modopt.step"),
                Options = new[]
                {
                    new ModOption { Id = "step.focused", Label = () => Loc.T("modopt.step.focused"),
                        Selected = () => !StepEchoScope.All, Select = () => StepEchoScope.All = false },
                    new ModOption { Id = "step.all", Label = () => Loc.T("modopt.step.all"),
                        Selected = () => StepEchoScope.All, Select = () => StepEchoScope.All = true },
                },
            },
            new ModOptionRow
            {
                Id = "beeps",
                Label = () => Loc.T("modopt.beeps"),
                Options = new[]
                {
                    new ModOption { Id = "beeps.on", Label = () => GameText.T("Enable"),
                        Selected = () => TestBeeps.Enabled, Select = () => TestBeeps.Enabled = true },
                    new ModOption { Id = "beeps.off", Label = () => GameText.T("Disable"),
                        Selected = () => !TestBeeps.Enabled, Select = () => TestBeeps.Enabled = false },
                },
            },
        };
    }
}

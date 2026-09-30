using System;
using Echopunks.Localization;
using Echopunks.Modularity;
using HarmonyLib;

namespace Echopunks
{
    /// <summary>
    /// The module entry point — what the host's ModuleLoader instantiates. Composition root for all
    /// reloadable features: localization first (everything speakable depends on it), then the module's
    /// own Harmony patches (per-load unique id — see IModModule), announcer hooks, and FrameLoop steps.
    /// All statics in this assembly are per-load — a reload starts this whole half of the mod cold,
    /// re-deriving everything from the live game (and re-reading the locale files).
    /// </summary>
    public sealed class EchopunksModule : IModModule
    {
        private ModHost _host;
        private Harmony _harmony;

        public void Load(ModHost host)
        {
            _host = host;
            LocalizationManager.Initialize();

            // The announcer's localized wording hooks (the graph core itself never touches Loc).
            UI.Graph.GraphAnnouncer.PositionText = (index, count) => Loc.T("nav.position", new { index, count });
            UI.Graph.GraphAnnouncer.ExpandedStateText = e => Loc.T(e ? "role.expanded" : "role.collapsed");

            // Module-owned patches: per-load UNIQUE id so THIS load's Dispose unpatches exactly these
            // (the host loads the new module before disposing the old — a fixed id would let the old
            // teardown strip the fresh patches; see IModModule).
            _harmony = new Harmony("com.echopunks.module." + Guid.NewGuid().ToString("N"));
            if (!host.GameInitialized)
                Patches.SplashPatches.Apply(_harmony); // pre-init only: the splash is long gone on a reload
            // Every OTHER patch waits for the first tick — see Tick(). Patching here would run each
            // target type's STATIC CONSTRUCTOR before game init (Harmony's detour JIT-prepares the
            // method), and a cctor that reads the game's loc registry (EditorScreen's tooltip
            // LocStrings) then dies on the not-yet-loaded strings table. The CLR caches that failure
            // for the life of the process and the game crashes on first use of the type instead.

            RegisterInput();
            Screens.ScreenManager.Initialize();

            // Per-frame steps, in the order they run: one keyboard snapshot, then input dispatch, then
            // the screen stack (which drives the navigator). (The dev pump is host-side, first.)
            FrameLoop.Register("keyboard", Input.SdlKeyboard.Update);
            FrameLoop.Register("input", Input.InputManager.Tick);
            FrameLoop.Register("loc", LocalizationManager.Tick);
            FrameLoop.Register("screens", Screens.ScreenManager.Tick);

            // One greeting, right at boot (the game window isn't even up yet), so the user knows the
            // mod is alive before the splash prompt arrives. A hot reload mid-session doesn't re-greet.
            if (host.ModuleGeneration == 1)
            {
                Speech.Tts.Speak(Loc.T("app.ready"));
                // The launch update check: one background request per game launch.
                _updateCheck = new Update.UpdateChecker();
                _updateCheck.Start(Update.UpdateChecker.LocalVersion());
            }
        }

        // Its line is spoken from Tick when the request lands with a release strictly newer than
        // the running build; every other outcome is a log line only.
        private Update.UpdateChecker _updateCheck;
        private bool _updateAnnounced;

        /// <summary>The nav action set (the WrathAccess ui.* vocabulary GraphNavigator dispatches on)
        /// and the input hooks. Bindings are the standard screen-reader set; rebinding UI comes with
        /// the settings tree.</summary>
        private static void RegisterInput()
        {
            Input.InputManager.Register("ui.up", "Up", Input.InputCategory.UI).AddBinding(Input.Scancode.Up).Repeating();
            Input.InputManager.Register("ui.down", "Down", Input.InputCategory.UI).AddBinding(Input.Scancode.Down).Repeating();
            Input.InputManager.Register("ui.left", "Left", Input.InputCategory.UI).AddBinding(Input.Scancode.Left).Repeating();
            Input.InputManager.Register("ui.right", "Right", Input.InputCategory.UI).AddBinding(Input.Scancode.Right).Repeating();
            Input.InputManager.Register("ui.next", "Next group", Input.InputCategory.UI).AddBinding(Input.Scancode.Tab).Repeating();
            Input.InputManager.Register("ui.prev", "Previous group", Input.InputCategory.UI).AddBinding(Input.Scancode.Tab, shift: true).Repeating();
            Input.InputManager.Register("ui.home", "First item", Input.InputCategory.UI).AddBinding(Input.Scancode.Home);
            Input.InputManager.Register("ui.end", "Last item", Input.InputCategory.UI).AddBinding(Input.Scancode.End);
            Input.InputManager.Register("ui.pageUp", "Adjust up, large step", Input.InputCategory.UI).AddBinding(Input.Scancode.PageUp).Repeating();
            Input.InputManager.Register("ui.pageDown", "Adjust down, large step", Input.InputCategory.UI).AddBinding(Input.Scancode.PageDown).Repeating();
            Input.InputManager.Register("ui.activate", "Activate", Input.InputCategory.UI)
                .AddBinding(Input.Scancode.Return).AddBinding(Input.Scancode.KpEnter);
            Input.InputManager.Register("ui.secondary", "Secondary action", Input.InputCategory.UI).AddBinding(Input.Scancode.Backspace);
            Input.InputManager.Register("ui.tooltip", "Read details", Input.InputCategory.UI).AddBinding(Input.Scancode.Space);
            Input.InputManager.Register("ui.back", "Back", Input.InputCategory.UI).AddBinding(Input.Scancode.Escape);
            Input.InputManager.Register("ui.regionPrev", "Previous region", Input.InputCategory.UI).AddBinding(Input.Scancode.Up, ctrl: true).Repeating();
            Input.InputManager.Register("ui.regionNext", "Next region", Input.InputCategory.UI).AddBinding(Input.Scancode.Down, ctrl: true).Repeating();
            // Screen-scoped: dispatched to the focused screen's actions (GetActions) when the
            // navigator doesn't claim the key. F2 = step the sim in the EXA editor (the game's own
            // Tab-step is suppressed there so Tab can stay stop-navigation everywhere).
            Input.InputManager.Register("ui.step", "Step simulation", Input.InputCategory.UI).AddBinding(Input.Scancode.F2).Repeating();
            // Run to the caret's line (the game's Alt+Click "run to instruction"). One-shot,
            // first arrival of ANY copy of the program wins — re-arm after each pause.
            // Shift+Enter pins ONE EXA instead (row-aware — see RunToPinned; the editor
            // consumes Enter only modifier-exact, so the shifted chord is free).
            Input.InputManager.Register("ui.runto", "Run to caret line", Input.InputCategory.UI).AddBinding(Input.Scancode.F8);
            Input.InputManager.Register("ui.runto.exa", "Run to line, this EXA", Input.InputCategory.UI)
                .AddBinding(Input.Scancode.Return, shift: true).AddBinding(Input.Scancode.KpEnter, shift: true);
            // Cycle which live instance (original/REPL copies) the armed code view follows —
            // same axis as the game's Ctrl+Up/Down PROGRAM switch, one modifier over:
            // Ctrl walks programs, Alt walks this program's instances.
            Input.InputManager.Register("ui.followNext", "Next EXA instance", Input.InputCategory.UI)
                .AddBinding(Input.Scancode.Down, alt: true);
            Input.InputManager.Register("ui.followPrev", "Previous EXA instance", Input.InputCategory.UI)
                .AddBinding(Input.Scancode.Up, alt: true);
            // Reorder an EXA in the editor's window column (the game's mouse-only window drag):
            // the order feeds the simulator's per-cycle scheduling, so it changes who wins races.
            Input.InputManager.Register("ui.moveUp", "Move EXA up", Input.InputCategory.UI)
                .AddBinding(Input.Scancode.Up, shift: true);
            Input.InputManager.Register("ui.moveDown", "Move EXA down", Input.InputCategory.UI)
                .AddBinding(Input.Scancode.Down, shift: true);
            // Register reads in the EXA editor: the bare register letter speaks that register
            // of the focused EXA. The screen withholds them while a text field has focus —
            // there the letters are typing.
            Input.InputManager.Register("ui.reg.x", "Read X register", Input.InputCategory.UI).AddBinding(Input.Scancode.X);
            Input.InputManager.Register("ui.reg.t", "Read T register", Input.InputCategory.UI).AddBinding(Input.Scancode.T);
            Input.InputManager.Register("ui.reg.f", "Read F register", Input.InputCategory.UI).AddBinding(Input.Scancode.F);
            Input.InputManager.Register("ui.reg.m", "Read M register", Input.InputCategory.UI).AddBinding(Input.Scancode.M);
            // Alt+1..6 jump straight to a Tab stop (the EXA editor maps them: task, windows,
            // hosts, code, test log, solution). Alt keeps the digits free for typing — Windows
            // sends no text input for Alt+digit, so a focused code field never sees them.
            Input.Scancode[] jumpKeys =
            {
                Input.Scancode.Num1, Input.Scancode.Num2, Input.Scancode.Num3,
                Input.Scancode.Num4, Input.Scancode.Num5, Input.Scancode.Num6,
            };
            for (int i = 0; i < jumpKeys.Length; i++)
                Input.InputManager.Register("ui.jump." + (i + 1), "Jump to section " + (i + 1), Input.InputCategory.UI)
                    .AddBinding(jumpKeys[i], alt: true);
            // Typing echo on/off (the game reads no F6). Handled in Navigation, ahead of any screen.
            Input.InputManager.Register("ui.echo", "Toggle typing echo", Input.InputCategory.UI).AddBinding(Input.Scancode.F6);

            Input.InputManager.ActiveCategoriesProvider = () =>
                new System.Collections.Generic.List<Input.InputCategory>(Screens.ScreenManager.ActiveInputCategories());
            Input.InputManager.UiDispatcher = UI.Navigation.DispatchJustPressed;
            Input.InputManager.SuppressPoll = () =>
            {
                var cur = Screens.ScreenManager.Current;
                return cur != null && cur.CapturesRawInput;
            };
        }

        /// <summary>Ticks come from the GameLogic tick prefix, which never runs until init has
        /// returned — so first-tick arming guarantees the game's loc registry (and everything else
        /// init builds) exists before any patched type's static constructor can be forced. On a hot
        /// reload the next frame arms immediately; cold boots arm one frame after the title appears.</summary>
        private bool _gamePatchesArmed;

        public void Tick()
        {
            if (!_gamePatchesArmed)
            {
                _gamePatchesArmed = true;
                Patches.GameKeySuppression.Apply(_harmony); // focus-mode key swallow (see the class doc)
                Patches.SimNarration.Apply(_harmony);       // buffer sim errors the model deletes too fast
                Patches.ExecutionCapture.Apply(_harmony);   // per-cycle executed instructions -> the execution log
                Patches.CutsceneAdvance.Apply(_harmony);    // one press = the next cutscene line (after suppression: it forces keys through that seam)
                Patches.PanelCapture.IsHostName = Screens.ExaEditorScreen.IsSpokenHostName;
                Patches.PanelCapture.Apply(_harmony);       // special-puzzle panel text + goal-view force
                Screens.CutsceneNotes.Apply();              // Moss's EXODUS recollection in the Ghast visit (campaign lists exist post-init)
            }
            // Ticks only run after game init, so this can never talk over the boot prompt.
            if (!_updateAnnounced && _updateCheck != null && _updateCheck.NewerVersion != null)
            {
                _updateAnnounced = true;
                Speech.Tts.Speak(Loc.T("app.update_available", new { version = _updateCheck.NewerVersion }));
            }
            FrameLoop.Tick();
        }

        public void Dispose()
        {
            Screens.CutsceneNotes.Remove(); // in-memory script lines: out before the next generation inserts its own
            // This Harmony build predates UnpatchSelf; UnpatchAll(ownId) is the same owner-scoped removal.
            try { _harmony?.UnpatchAll(_harmony.Id); }
            catch (Exception ex) { Log.Error("[module] UnpatchSelf failed", ex); }
            _harmony = null;
        }
    }
}

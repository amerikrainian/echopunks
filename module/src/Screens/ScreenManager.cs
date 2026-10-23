using System;
using System.Collections.Generic;
using System.Linq;
using Echopunks.UI;

namespace Echopunks.Screens
{
    /// <summary>
    /// The screen stack, ported from WrathAccess with EXAPUNKS resolution: registered screens poll
    /// IsActive() each frame (against the GAME's own screen stack via GameState), the active set is
    /// diffed onto a persistent stack (push/pop lifecycle), and the navigator is attached to the
    /// deepest focused screen. Ticked from the module FrameLoop.
    ///
    /// Screens the mod hasn't modeled yet still get their old announce-by-name behavior: when the
    /// game's top screen changes and NO registered screen covers it, the friendly (localized) name is
    /// spoken — the fallback that replaced the retired ScreenAnnouncer. Obfuscated names are logged,
    /// never spoken (hard rule).
    /// </summary>
    public static class ScreenManager
    {
        private static readonly List<Screen> _registered = new List<Screen>();
        private static List<Screen> _stack = new List<Screen>();
        private static Screen _focused; // the deepest screen the navigator is currently attached to
        private static string _lastGameScreen;

        /// <summary>Test seam / game boundary: the game's top screen type name.</summary>
        internal static Func<string> GameTopScreenName = GameState.TopScreenName;

        // The focused screen = the deepest active child of the top outer screen.
        public static Screen Current => _stack.Count > 0 ? _stack[_stack.Count - 1].DeepestActiveScreen() : null;
        public static IReadOnlyList<Screen> Stack => _stack;

        /// <summary>Active screens in focus-priority order — the focused screen first, then outward /
        /// down the stack. This is the order the input claim-chain walks.</summary>
        public static IEnumerable<Input.InputCategory> ActiveInputCategories()
        {
            if (!FocusMode.Active) yield break;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var chain = new List<Screen>();
                for (var s = _stack[i]; s != null; s = s.ActiveChild) chain.Add(s);
                for (int j = chain.Count - 1; j >= 0; j--)
                    foreach (var c in chain[j].InputCategories)
                        yield return c;
                if (chain.Any(s => s.Exclusive)) yield break; // a modal owns the keyboard
            }
        }

        public static void Register(Screen screen) => _registered.Add(screen);

#if DEBUG
        internal static IReadOnlyList<Screen> Registered => _registered;
#endif

        public static void Tick()
        {
            ApplyDiff(Resolve()); // poll the game-driven screens → push/pop on the persistent stack
            SyncFocus();          // focus the deepest screen (before OnUpdate, as in WotR)
            Current?.OnUpdate();  // may push/remove child sub-screens
            SyncFocus();          // re-sync if OnUpdate (or this frame's input) changed the child tree
            Navigation.EnsureFocus(); // standardized first-focus + the announce differ
            AnnounceUnmodeledScreens();
        }

        // The retired ScreenAnnouncer's behavior, scoped to screens no registered Screen covers:
        // announce the game's top screen by its friendly name whenever it changes.
        private static void AnnounceUnmodeledScreens()
        {
            string top;
            try { top = GameTopScreenName(); }
            catch { return; }
            if (top == _lastGameScreen) return;
            _lastGameScreen = top;
            if (top == null) return;
            Log.Info("[screen] -> " + top + (Current != null ? " (modeled: " + Current.Key + ")" : ""));
            if (Current == null && !UI.ScreenNames.IsObfuscated(top))
                Speech.Tts.Speak(UI.ScreenNames.Friendly(top));
        }

        /// <summary>Active screens, ordered bottom (low layer) → top (high layer).</summary>
        private static List<Screen> Resolve()
        {
            var active = new List<Screen>();
            for (int i = 0; i < _registered.Count; i++)
                if (SafeIsActive(_registered[i])) active.Add(_registered[i]);
            return active.OrderBy(s => s.Layer).ToList();
        }

        private static bool SafeIsActive(Screen s)
        {
            try { return s.IsActive(); }
            catch (Exception e)
            {
                Log.Error("[screen] IsActive threw for '" + s.Key + "': " + e.Message);
                return false;
            }
        }

        // Diff the polled active set against the persistent stack: pop screens that went inactive
        // (each with its whole child subtree) and push newly-active ones.
        private static void ApplyDiff(List<Screen> desired)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (!desired.Contains(_stack[i])) PopTree(_stack[i]);
            for (int i = 0; i < desired.Count; i++)
                if (!_stack.Contains(desired[i])) { var s = desired[i]; Safe(() => s.OnPush(), s, "OnPush"); }
            _stack = desired;
        }

        private static void PopTree(Screen s)
        {
            if (s.ActiveChild != null) s.RemoveChild(s.ActiveChild);
            Safe(() => s.OnPop(), s, "OnPop");
            if (!s.KeepStateOnPop) Navigation.ScreenClosed(s);
        }

        // Re-attach the navigator whenever the deepest (focused) screen changes — from an outer
        // push/pop OR a child-tree push/remove. Idempotent.
        private static void SyncFocus()
        {
            var cur = Current;
            if (ReferenceEquals(cur, _focused)) return;
            _focused?.OnUnfocus();
            _focused = cur;
            Safe(() => cur?.OnFocus(), cur, "OnFocus"); // speaks the screen name
            Navigation.Attach(cur);
        }

        private static void Safe(Action a, Screen s, string hook)
        {
            try { a(); }
            catch (Exception e) { Log.Error("[screen] " + hook + " threw for '" + (s?.Key ?? "?") + "': " + e); }
        }

        /// <summary>Register the modeled screens. Grows as screens land (see the CLAUDE.md roadmap).</summary>
        public static void Initialize()
        {
            if (_registered.Count > 0) return;
            Register(new TitleScreen());
            Register(new DesktopHubScreen());
            Register(new CutsceneScreen());
            Register(new EmberCutsceneScreen());
            Register(new TrashWorldNewsScreen());
            Register(new CreditsScreen());
            Register(new WorkhouseScreen());
            Register(new SolitaireGameScreen());
            Register(new ExaEditorScreen());
            Register(new SolutionBrowserGameScreen());
            Register(new CustomNetworkScreen());
            Register(new NetworkMenuScreen());
            Register(new NetworkUploadScreen());
            Register(new PuzzleCompleteScreen());
            Register(new OpponentBrowserGameScreen());
            Register(new BattleCompleteScreen());
            Register(new ControlPanelHomeScreen());
            Register(new ControlPanelOptionsScreen());
            Register(new ControlPanelControlsScreen());
            Register(new GameKeyCaptureScreen()); // layer 10 — covers the panel while capturing
            Log.Info("[screen] " + _registered.Count + " screen(s) registered.");
        }

        /// <summary>Test seam: forget everything.</summary>
        internal static void ResetForTests()
        {
            _registered.Clear();
            _stack = new List<Screen>();
            _focused = null;
            _lastGameScreen = null;
            GameTopScreenName = GameState.TopScreenName;
            Navigation.Attach(null);
        }
    }
}

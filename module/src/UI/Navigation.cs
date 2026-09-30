using Echopunks.Input;
using Echopunks.Screens;

namespace Echopunks.UI
{
    /// <summary>
    /// Holds the active Navigator (swappable later) and is the entry point input dispatches into.
    /// ScreenManager re-attaches it on screen change. Ported from WrathAccess.
    /// </summary>
    public static class Navigation
    {
        public static Navigator Active = new GraphNavigator();

        public static void Attach(Screen screen) => Active?.Attach(screen);

        /// <summary>True when something is focused (the navigator owns the keys).</summary>
        public static bool HasFocus => Active != null && Active.HasFocus;

        public static bool DispatchJustPressed(InputAction action)
        {
            // Global, ahead of the navigator: a caret-owning text node bubbles most keys to the
            // game, and a text field is exactly where the echo toggle is wanted.
            if (action.Key == "ui.echo") { TypingEcho.Toggle(); return true; }
            return Active != null && Active.OnInputJustPressed(action);
        }

        public static void AnnounceCurrent() => Active?.AnnounceCurrent();

        /// <summary>Re-read just the focused node after an in-place change — see Navigator.</summary>
        public static void ReannounceCurrent() => Active?.ReannounceCurrent();

        /// <summary>Re-establish initial focus if the focused screen has focusable content but nothing
        /// is focused yet. Ticked each frame by ScreenManager.</summary>
        public static void EnsureFocus() => Active?.EnsureFocus();

        /// <summary>Return to the unfocused state — see <see cref="Navigator.Blur"/>.</summary>
        public static void Blur() => Active?.Blur();

        /// <summary>Notify that a screen closed (its per-screen nav state is dropped).</summary>
        public static void ScreenClosed(Screen screen) => Active?.ScreenClosed(screen);

        /// <summary>Move focus to a graph node by id.</summary>
        public static void FocusNode(Graph.ControlId id, bool announce = true) => Active?.FocusNode(id, announce);

        /// <summary>Move focus to the landing node of a Tab-stop.</summary>
        public static void FocusStop(object stopKey) => Active?.FocusStop(stopKey);

        /// <summary>True when the active navigator's current render has this Tab-stop.</summary>
        public static bool StopPresent(object stopKey) => Active != null && Active.StopPresent(stopKey);

        /// <summary>The Tab-stop the focused node belongs to, or null.</summary>
        public static object FocusedStopKey => Active?.FocusedStopKey;

        /// <summary>The focused node's ControlId, when the active navigator is graph-based.</summary>
        public static Graph.ControlId FocusedNodeId => (Active as GraphNavigator)?.FocusedNodeId;

        /// <summary>True while the focused node is a live type-target — see Navigator.</summary>
        public static bool TextEntryFocused => Active != null && Active.TextEntryFocused;

        /// <summary>True while the focused node is a caret-owning editor — see Navigator.</summary>
        public static bool CaretTextEntryFocused => Active != null && Active.CaretTextEntryFocused;
    }
}

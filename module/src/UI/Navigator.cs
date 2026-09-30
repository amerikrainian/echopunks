using Echopunks.Input;
using Echopunks.Screens;

namespace Echopunks.UI
{
    /// <summary>
    /// The navigation contract <see cref="Navigation"/> drives (ported from WrathAccess): bind to a
    /// screen, consume input, keep focus established, and announce focus changes. The active
    /// implementation is <see cref="GraphNavigator"/>, where announcements are PULL-based: focus is
    /// diffed per frame and a change speaks exactly once no matter what caused it — so
    /// implementations and screens never make per-callsite announce decisions.
    /// </summary>
    public abstract class Navigator
    {
        protected Screen Screen { get; set; }

        /// <summary>True when the navigator owns the keys (something is focused) — false in an
        /// unfocused exploration-style state.</summary>
        public abstract bool HasFocus { get; }

        /// <summary>Bind to a screen. Re-attaching the SAME screen means "content changed" (focus and
        /// announce memory survive); a new screen resets both.</summary>
        public abstract void Attach(Screen screen);

        /// <summary>Drop focus back to the screen's unfocused state — the same place Tab-off-the-end
        /// lands. Only meaningful on <see cref="Screen.StartUnfocused"/> screens; elsewhere focus
        /// re-establishes next frame.</summary>
        public abstract void Blur();

        /// <summary>The per-frame pull, called after the focused screen updates: (re)establish focus
        /// when the screen has focusable content, and announce any focus change exactly once.</summary>
        public abstract void EnsureFocus();

        public abstract bool OnInputJustPressed(InputAction action);

        /// <summary>Announce the current focus in full (the container hierarchy down to the element).</summary>
        public abstract void AnnounceCurrent();

        /// <summary>Rebuild now and re-read just the focused node (no enclosing contexts),
        /// interrupting — for an action that changed the focused node itself in place.</summary>
        public virtual void ReannounceCurrent() { }

        /// <summary>A screen closed (stack pop without <see cref="Screen.KeepStateOnPop"/>, or a child
        /// page removed): drop its per-screen state so reopening starts fresh.</summary>
        public virtual void ScreenClosed(Screen screen) { }

        /// <summary>Move focus to a graph node by id — applied when the node exists in a render, with
        /// one retry frame for content that appears mid-build.</summary>
        public virtual void FocusNode(Graph.ControlId id, bool announce = true) { }

        /// <summary>Move focus to the landing node of a Tab-stop (a wizard landing on new page content).</summary>
        public virtual void FocusStop(object stopKey) { }

        /// <summary>True when the current render has a node to land on in this Tab-stop —
        /// FocusStop on an absent stop is silently dropped, so callers that owe feedback ask first.</summary>
        public virtual bool StopPresent(object stopKey) => false;

        /// <summary>The Tab-stop the focused node belongs to, or null.</summary>
        public virtual object FocusedStopKey => null;

        /// <summary>True while the focused node is a live type-target (NodeVtable.TextEntry) — the
        /// input pipeline and the game-key suppression stand their printable keys down.</summary>
        public virtual bool TextEntryFocused => false;

        /// <summary>True while the focused node is a full caret-owning editor
        /// (NodeVtable.TextEntryCaret) — the game keeps every key except Tab.</summary>
        public virtual bool CaretTextEntryFocused => false;

        // interrupt: true for focus MOVES (so held key-repeat reads the item you land on instead of
        // backing up a queue); false for screen-entry / landing readouts.
        protected static void Speak(string text, bool interrupt = false)
        {
            if (!string.IsNullOrEmpty(text)) Speech.Tts.Speak(text, interrupt);
        }
    }
}

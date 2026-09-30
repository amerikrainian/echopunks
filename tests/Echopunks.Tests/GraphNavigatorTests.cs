using System;
using System.Collections.Generic;
using Echopunks.Input;
using Echopunks.Screens;
using Echopunks.UI;
using Echopunks.UI.Graph;
using Xunit;

namespace Echopunks.Tests
{
    public class GraphNavigatorTests : IDisposable
    {
        private sealed class TestScreen : Screen
        {
            public Action<GraphBuilder> Declare;
            public override string Key => "test";
            public override bool IsActive() => true;
            public override void Build(GraphBuilder b) => Declare?.Invoke(b);
        }

        private readonly TestSpeech _speech = new TestSpeech();
        private readonly GraphNavigator _nav = new GraphNavigator();

        public GraphNavigatorTests()
        {
            FocusMode.Active = true;
            TypingEcho.ResetForTests(true); // the default, never the dev machine's saved F6 choice
        }

        public void Dispose()
        {
            FocusMode.Active = true;
            TypingEcho.ResetForTests(null);
            _speech.Dispose();
        }

        private static NodeVtable Vt(string label, Action activate = null, Func<string> state = null)
            => new NodeVtable
            {
                Announcements = new[] { NodeAnnouncement.Static(label) },
                OnActivate = activate,
                StateText = state,
            };

        private TestScreen TwoItemScreen()
            => new TestScreen
            {
                Declare = b =>
                {
                    b.AddItem(ControlId.Structural("a"), Vt("Alpha"));
                    b.AddItem(ControlId.Structural("b"), Vt("Beta"));
                },
            };

        private static InputAction Action(string key) => new InputAction(key, key);

        [Fact]
        public void EnsureFocusSeatsAndAnnouncesEntryExactlyOnce()
        {
            _nav.Attach(TwoItemScreen());
            _nav.EnsureFocus();
            Assert.Equal(new[] { "Alpha" }, _speech.Spoken);

            _nav.EnsureFocus(); // no change — no re-announce
            Assert.Equal(new[] { "Alpha" }, _speech.Spoken);
            Assert.True(_nav.HasFocus);
        }

        [Fact]
        public void ArrowMovesAndAnnounces()
        {
            _nav.Attach(TwoItemScreen());
            _nav.EnsureFocus();

            Assert.True(_nav.OnInputJustPressed(Action("ui.down")));
            Assert.Equal(new[] { "Alpha", "Beta" }, _speech.Spoken);

            // At the bottom edge of a plain list: not moved, bubbles (returns false).
            Assert.False(_nav.OnInputJustPressed(Action("ui.down")));
            Assert.Equal(2, _speech.Spoken.Count);
        }

        [Fact]
        public void ActivateRunsTheVtableAndSpeaksStateText()
        {
            bool clicked = false;
            var screen = new TestScreen
            {
                Declare = b => b.AddItem(ControlId.Structural("t"),
                    Vt("Toggle", activate: () => clicked = true, state: () => "on")),
            };
            _nav.Attach(screen);
            _nav.EnsureFocus();

            Assert.True(_nav.OnInputJustPressed(Action("ui.activate")));
            Assert.True(clicked);
            Assert.Contains("on", _speech.Spoken);
        }

        [Fact]
        public void PageKeysAdjustSlidersCoarselyAndBubbleElsewhere()
        {
            int value = 50;
            var screen = new TestScreen
            {
                Declare = b =>
                {
                    b.AddItem(ControlId.Structural("s"), new NodeVtable
                    {
                        Announcements = new[] { NodeAnnouncement.Static("Run") },
                        OnAdjust = (sign, large) => value += sign * (large ? 10 : 1),
                        StateText = () => value.ToString(),
                    });
                    b.AddItem(ControlId.Structural("t"), Vt("Text"));
                },
            };
            _nav.Attach(screen);
            _nav.EnsureFocus();

            Assert.True(_nav.OnInputJustPressed(Action("ui.pageUp")));
            Assert.Equal(60, value);
            Assert.True(_nav.OnInputJustPressed(Action("ui.pageDown")));
            Assert.Equal(50, value);
            Assert.True(_nav.OnInputJustPressed(Action("ui.right"))); // fine step unchanged
            Assert.Equal(51, value);
            Assert.Contains("60", _speech.Spoken);

            _nav.OnInputJustPressed(Action("ui.down")); // the plain text node
            Assert.False(_nav.OnInputJustPressed(Action("ui.pageUp"))); // bubbles
            Assert.Equal(51, value);
        }

        [Fact]
        public void ReattachingADifferentScreenResetsAndRestoringKeepsFocus()
        {
            var a = TwoItemScreen();
            var b = new TestScreen { Declare = gb => gb.AddItem(ControlId.Structural("x"), Vt("Xray")) };

            _nav.Attach(a);
            _nav.EnsureFocus();
            _nav.OnInputJustPressed(Action("ui.down")); // focus Beta

            _nav.Attach(b);
            _nav.EnsureFocus(); // announces Xray

            _nav.Attach(a); // back: per-screen state restores focus on Beta
            _nav.EnsureFocus();
            Assert.Equal("Beta", _speech.Spoken[_speech.Spoken.Count - 1]);
        }

        [Fact]
        public void ScreenClosedDropsPerScreenState()
        {
            var a = TwoItemScreen();
            _nav.Attach(a);
            _nav.EnsureFocus();
            _nav.OnInputJustPressed(Action("ui.down")); // Beta

            _nav.ScreenClosed(a);
            _nav.Attach(new TestScreen { Declare = gb => gb.AddItem(ControlId.Structural("x"), Vt("Xray")) });
            _nav.Attach(a); // fresh state → back to the start node
            _nav.EnsureFocus();
            Assert.Equal("Alpha", _speech.Spoken[_speech.Spoken.Count - 1]);
        }

        [Fact]
        public void LivePartsSpeakOnChangeOnly()
        {
            string value = "off";
            var screen = new TestScreen
            {
                Declare = b => b.AddItem(ControlId.Structural("t"), new NodeVtable
                {
                    Announcements = new[]
                    {
                        NodeAnnouncement.Static("Power"),
                        new NodeAnnouncement(() => value, live: true),
                    },
                }),
            };
            _nav.Attach(screen);
            _nav.EnsureFocus(); // baseline (entry announce includes "off")
            int after = _speech.Spoken.Count;

            _nav.EnsureFocus(); // unchanged — silent
            Assert.Equal(after, _speech.Spoken.Count);

            value = "on";
            _nav.EnsureFocus();
            Assert.Equal("on", _speech.Spoken[_speech.Spoken.Count - 1]);
        }

        [Fact]
        public void FocusModeOffSilencesTheDiffer()
        {
            FocusMode.Active = false;
            _nav.Attach(TwoItemScreen());
            _nav.EnsureFocus();
            Assert.Empty(_speech.Spoken);
        }

        [Fact]
        public void TabCyclesStopsAndConsumesAtTheEnd()
        {
            var screen = new TestScreen
            {
                Declare = b =>
                {
                    b.AddItem(ControlId.Structural("a"), Vt("Alpha"));
                    b.BeginStop();
                    b.AddItem(ControlId.Structural("b"), Vt("Beta"));
                },
            };
            _nav.Attach(screen);
            _nav.EnsureFocus();

            Assert.True(_nav.OnInputJustPressed(Action("ui.next")));
            Assert.Equal("Beta", _speech.Spoken[_speech.Spoken.Count - 1]);

            Assert.True(_nav.OnInputJustPressed(Action("ui.next"))); // at the last stop: consume, no wrap
            Assert.Equal("Beta", _speech.Spoken[_speech.Spoken.Count - 1]);
        }

        // ---- selection-follows-focus (NodeVtable.OnSelect) ----

        private TestScreen TabStripScreen(Func<string> selectedKey, Action<string> select)
            => new TestScreen
            {
                Declare = b =>
                {
                    foreach (var key in new[] { "one", "two" })
                    {
                        var k = key;
                        b.AddItem(ControlId.Structural(k), new NodeVtable
                        {
                            Announcements = new[]
                            {
                                NodeAnnouncement.Static(k),
                                new NodeAnnouncement(() => selectedKey() == k ? "selected" : null,
                                    live: true, kind: AnnouncementKinds.Selected),
                            },
                            OnSelect = () => select(k),
                            OnActivate = () => select(k),
                        });
                    }
                },
            };

        [Fact]
        public void ArrowingOntoAnOnSelectNodeSelectsItBeforeAnnouncing()
        {
            string selected = "one";
            var screen = TabStripScreen(() => selected, k => selected = k);
            _nav.Attach(screen);
            _nav.EnsureFocus();

            Assert.True(_nav.OnInputJustPressed(Action("ui.down")));
            Assert.Equal("two", selected); // selection followed focus, no Enter needed
            // The landing announcement already carries the post-select state.
            Assert.Contains("selected", _speech.Spoken[_speech.Spoken.Count - 1]);
        }

        [Fact]
        public void TabStopCyclingDoesNotTriggerOnSelect()
        {
            string selected = "outside";
            var screen = new TestScreen
            {
                Declare = b =>
                {
                    b.AddItem(ControlId.Structural("a"), Vt("Alpha"));
                    b.BeginStop();
                    b.AddItem(ControlId.Structural("b"), new NodeVtable
                    {
                        Announcements = new[] { NodeAnnouncement.Static("Beta") },
                        OnSelect = () => selected = "b",
                    });
                },
            };
            _nav.Attach(screen);
            _nav.EnsureFocus();

            Assert.True(_nav.OnInputJustPressed(Action("ui.next"))); // land on Beta via Tab
            Assert.Equal("outside", selected); // stop landings must not change state
        }

        [Fact]
        public void SilentSelectedTabsSelectOnArrowWithoutSpeakingSelected()
        {
            // The tab shape: OnSelect + engine-only vtable Selected, no spoken Selected part.
            string selected = "one";
            var screen = new TestScreen
            {
                Declare = b =>
                {
                    foreach (var key in new[] { "one", "two" })
                    {
                        var k = key;
                        b.AddItem(ControlId.Structural(k), new NodeVtable
                        {
                            Announcements = new[] { NodeAnnouncement.Static(k) },
                            Selected = () => selected == k,
                            OnSelect = () => selected = k,
                            OnActivate = () => selected = k,
                        });
                    }
                },
            };
            _nav.Attach(screen);
            _nav.EnsureFocus();

            Assert.True(_nav.OnInputJustPressed(Action("ui.down")));
            Assert.Equal("two", selected); // selection followed focus...
            Assert.DoesNotContain("selected", _speech.Spoken[_speech.Spoken.Count - 1]); // ...silently
        }

        // ---- text-entry nodes (typing-first fields) ----

        private sealed class TextFieldHarness
        {
            public string Value = "";
            public int Armed;
            public NodeVtable Vt(string label) => new NodeVtable
            {
                Announcements = new[] { NodeAnnouncement.Static(label) },
                TextEntry = true,
                TextValue = () => Value,
                OnSelect = () => Armed++,
            };
        }

        [Fact]
        public void TextEntryFocusBubblesSpaceAndBackspace()
        {
            var h = new TextFieldHarness();
            var screen = new TestScreen { Declare = b => b.AddItem(ControlId.Structural("f"), h.Vt("Field")) };
            _nav.Attach(screen);
            _nav.EnsureFocus();

            Assert.True(_nav.TextEntryFocused);
            Assert.False(_nav.OnInputJustPressed(Action("ui.tooltip")));   // Space types into the field
            Assert.False(_nav.OnInputJustPressed(Action("ui.secondary"))); // Backspace edits the field
        }

        [Fact]
        public void TypingEchoSpeaksAppendedAndDeletedCharacters()
        {
            var h = new TextFieldHarness();
            var screen = new TestScreen { Declare = b => b.AddItem(ControlId.Structural("f"), h.Vt("Field")) };
            _nav.Attach(screen);
            _nav.EnsureFocus(); // baseline (announces "Field")

            h.Value = "a";
            _nav.EnsureFocus();
            Assert.Equal("a", _speech.Spoken[_speech.Spoken.Count - 1]);

            h.Value = "ab";
            _nav.EnsureFocus();
            Assert.Equal("b", _speech.Spoken[_speech.Spoken.Count - 1]);

            h.Value = "a"; // deletion echoes the removed character bare
            _nav.EnsureFocus();
            Assert.Equal("b", _speech.Spoken[_speech.Spoken.Count - 1]);
        }

        [Fact]
        public void CapsOffFieldsEchoUppercaseBare()
        {
            // TextEchoCaps=false: the widget normalizes case, so its buffer holds no capitals to
            // report — an uppercase buffer char echoes bare, on both typing and deleting.
            string value = "";
            var screen = new TestScreen
            {
                Declare = b => b.AddItem(ControlId.Structural("f"), new NodeVtable
                {
                    Announcements = new[] { NodeAnnouncement.Static("Field") },
                    TextEntry = true,
                    TextValue = () => value,
                    TextEchoCaps = false,
                }),
            };
            _nav.Attach(screen);
            _nav.EnsureFocus();

            value = "A";
            _nav.EnsureFocus();
            Assert.Equal("A", _speech.Spoken[_speech.Spoken.Count - 1]);

            value = "";
            _nav.EnsureFocus();
            Assert.Equal("A", _speech.Spoken[_speech.Spoken.Count - 1]);
        }

        [Fact]
        public void CaretEditorBubblesNavigationKeysButKeepsTab()
        {
            string value = "";
            var screen = new TestScreen
            {
                Declare = b => b.AddItem(ControlId.Structural("code"), new NodeVtable
                {
                    Announcements = new[] { NodeAnnouncement.Static("Code") },
                    TextEntry = true,
                    TextEntryCaret = true,
                    TextValue = () => value,
                }),
            };
            _nav.Attach(screen);
            _nav.EnsureFocus();

            Assert.True(_nav.CaretTextEntryFocused);
            Assert.False(_nav.OnInputJustPressed(Action("ui.up")));       // caret's
            Assert.False(_nav.OnInputJustPressed(Action("ui.left")));     // caret's
            Assert.False(_nav.OnInputJustPressed(Action("ui.home")));     // caret's
            Assert.False(_nav.OnInputJustPressed(Action("ui.activate"))); // Enter = newline
            Assert.True(_nav.OnInputJustPressed(Action("ui.next")));      // Tab stays ours
        }

        [Fact]
        public void MidStringEditsEchoJustTheChangedCharacters()
        {
            string value = "ABC\nDEF";
            var screen = new TestScreen
            {
                Declare = b => b.AddItem(ControlId.Structural("code"), new NodeVtable
                {
                    Announcements = new[] { NodeAnnouncement.Static("Code") },
                    TextEntry = true,
                    TextEntryCaret = true,
                    TextEchoCaps = false,
                    TextValue = () => value,
                }),
            };
            _nav.Attach(screen);
            _nav.EnsureFocus(); // baseline

            value = "ABXC\nDEF"; // insert mid-string
            _nav.EnsureFocus();
            Assert.Equal("X", _speech.Spoken[_speech.Spoken.Count - 1]);

            value = "ABC\nDEF"; // delete it again
            _nav.EnsureFocus();
            Assert.Equal("X", _speech.Spoken[_speech.Spoken.Count - 1]);
        }

        [Fact]
        public void BufferAppearingAfterAQueuedArmDoesNotEchoTheWholeText()
        {
            // A caret editor whose arm is queued returns null until the game applies focus —
            // the buffer appearing one frame later must baseline silently, not read as typed.
            string value = null;
            var screen = new TestScreen
            {
                Declare = b => b.AddItem(ControlId.Structural("code"), new NodeVtable
                {
                    Announcements = new[] { NodeAnnouncement.Static("Code") },
                    TextEntry = true,
                    TextEntryCaret = true,
                    TextEchoCaps = false,
                    TextValue = () => value,
                }),
            };
            _nav.Attach(screen);
            _nav.EnsureFocus(); // baseline while unarmed (null)
            int spoken = _speech.Spoken.Count;

            value = "LINK 800\nHALT"; // the arm applied; the buffer appears
            _nav.EnsureFocus();
            Assert.Equal(spoken, _speech.Spoken.Count); // silent baseline

            value = "LINK 800\nHALTX"; // a real edit afterwards still echoes
            _nav.EnsureFocus();
            Assert.Equal("X", _speech.Spoken[_speech.Spoken.Count - 1]);
        }

        [Fact]
        public void BufferSwapReBaselinesInsteadOfDiffing()
        {
            // ONE code node fronts whichever EXA the game focuses (TextIdentity = the EXA). The
            // game's Ctrl+Up/Down swap must re-baseline silently — diffing XA's code against XB's
            // would speak one whole program as "deleted" (the reported bug: switching to an empty
            // XB read back all of XA's code, trampling the switch announce).
            string value = "LINK 800\nHALT";
            int exa = 0;
            var screen = new TestScreen
            {
                Declare = b => b.AddItem(ControlId.Structural("code"), new NodeVtable
                {
                    Announcements = new[] { NodeAnnouncement.Static("Code") },
                    TextEntry = true,
                    TextEntryCaret = true,
                    TextEchoCaps = false,
                    TextValue = () => value,
                    TextIdentity = () => exa,
                }),
            };
            _nav.Attach(screen);
            _nav.EnsureFocus(); // baseline on XA
            int spoken = _speech.Spoken.Count;

            exa = 1; value = ""; // the game switches focus to the empty XB
            _nav.EnsureFocus();
            Assert.Equal(spoken, _speech.Spoken.Count); // silent — no "deleted XA's code" echo

            exa = 0; value = "LINK 800\nHALT"; // and back
            _nav.EnsureFocus();
            Assert.Equal(spoken, _speech.Spoken.Count); // silent — no "typed XA's code" echo

            value = "LINK 800\nHALTX"; // a real edit on the same EXA still echoes
            _nav.EnsureFocus();
            Assert.Equal("X", _speech.Spoken[_speech.Spoken.Count - 1]);
        }

        [Fact]
        public void TabLandingArmsATextField()
        {
            var h = new TextFieldHarness();
            var screen = new TestScreen
            {
                Declare = b =>
                {
                    b.AddItem(ControlId.Structural("a"), Vt("Alpha"));
                    b.BeginStop();
                    b.AddItem(ControlId.Structural("f"), h.Vt("Field"));
                },
            };
            _nav.Attach(screen);
            _nav.EnsureFocus();
            Assert.Equal(0, h.Armed);

            Assert.True(_nav.OnInputJustPressed(Action("ui.next"))); // Tab into the form stop
            Assert.Equal(1, h.Armed); // fields DO arm on stop landings — ready to type
        }

        [Fact]
        public void HomeEndJumpTriggersOnSelect()
        {
            string selected = "one";
            var screen = TabStripScreen(() => selected, k => selected = k);
            _nav.Attach(screen);
            _nav.EnsureFocus();

            Assert.True(_nav.OnInputJustPressed(Action("ui.end")));
            Assert.Equal("two", selected);
        }
    }
}

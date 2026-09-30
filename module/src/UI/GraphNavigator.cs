using System.Collections.Generic;
using Echopunks.Input;
using Echopunks.Localization;
using Echopunks.UI.Graph;

namespace Echopunks.UI
{
    /// <summary>
    /// The graph-based navigator, ported from WrathAccess: every screen runs on the key-graph core
    /// (<see cref="KeyGraph"/>), announce discipline is PULL-based — the graph is rebuilt per
    /// operation and per frame, focus is reconciled by identity, and a focus change is announced
    /// exactly once no matter what caused it (input, a screen moving focus, a content rebuild, the
    /// game replacing objects). WrathAccess's type-ahead search is deliberately not ported yet (it
    /// needs SDL TEXTINPUT plumbing — roadmap); its sound cues await an audio layer.
    /// </summary>
    public sealed class GraphNavigator : Navigator
    {
        // One GraphState per LIVE screen (focus cursor, per-stop memory, tree expansion): a screen
        // covered by another keeps its state and restores exactly where you were when focus returns;
        // a POPPED screen's state is dropped (ScreenClosed), so reopening starts fresh.
        private readonly Dictionary<Screens.Screen, GraphState> _states =
            new Dictionary<Screens.Screen, GraphState>();
        private GraphState _state = new GraphState();
        private KeyGraph _graph;

        // The differ's memory: the node identity (and its render node, for context diffing) last spoken.
        private ControlId _lastSpokenKey;
        private GraphNode _lastSpokenNode;

        // A focus request whose target isn't in the render yet (lazy content): applied by EnsureFocus.
        private ControlId _pendingFocus;
        private bool _pendingAnnounce;

        // A pending land-on-stop request (applied by EnsureFocus once the stop has nodes): resolves to
        // the stop's landing node at apply time, so it works when the caller can't know node keys.
        private object _pendingStop;

        /// <summary>Focus = a focused NODE.</summary>
        public override bool HasFocus => _graph?.CurrentNode != null;

        public override void Attach(Screens.Screen screen)
        {
            bool same = ReferenceEquals(screen, Screen);
            Screen = screen;
            if (!same)
            {
                // Swap to this screen's own state (creating it on first attach). The differ memory
                // resets so the (possibly restored) landing announces itself on return.
                if (screen != null)
                {
                    if (!_states.TryGetValue(screen, out _state))
                    {
                        _state = new GraphState();
                        _states[screen] = _state;
                    }
                }
                else
                {
                    _state = new GraphState();
                }
                _lastSpokenKey = null;
                _lastSpokenNode = null;
                _focusHolder = null;
                _pendingFocus = null;
                _pendingStop = null;
                _liveKey = null;
            }
            _graph = screen != null ? new KeyGraph(() => BuildRender(screen), _state) : null;
        }

        public override void ScreenClosed(Screens.Screen screen)
        {
            if (screen != null) _states.Remove(screen);
        }

        public override void FocusNode(ControlId id, bool announce = true)
        {
            if (id == null) return;
            _pendingFocus = id;
            _pendingAnnounce = announce;
        }

        public override void FocusStop(object stopKey)
        {
            _pendingStop = stopKey;
        }

        public override bool StopPresent(object stopKey)
            => _graph?.Current != null && _graph.StopLanding(stopKey) != null;

        public override object FocusedStopKey => _graph?.CurrentNode?.StopKey;

        public override bool TextEntryFocused => _graph?.CurrentNode?.Vtable?.TextEntry == true;

        public override bool CaretTextEntryFocused => _graph?.CurrentNode?.Vtable?.TextEntryCaret == true;

        /// <summary>The live render + focused node id (DEBUG inspection).</summary>
        internal GraphRender CurrentRender => _graph?.Current;
        internal ControlId FocusedNodeId => _graph?.CurrentNode?.Id;

        /// <summary>The PERSISTED focus cursor — where focus is by identity, even when the
        /// current render doesn't contain that node (windowed content whose focused row was
        /// cleared or slid out). Screens that window content by the focus anchor read THIS, not
        /// <see cref="FocusedNodeId"/>: the render-dependent id goes null the moment the row
        /// vanishes, which would un-pin the window right when pinning matters most.</summary>
        internal ControlId FocusCursorId => _state.CurKey;

        // Screens declare fresh from live game state on every render (immediate mode).
        private GraphRender BuildRender(Screens.Screen screen)
        {
            var b = new GraphBuilder(_state.Expanded); // groups consult the persistent expansion set
            screen.Build(b);
            return b.Build();
        }

        public override void Blur()
        {
            _state.CurKey = null;
            _lastSpokenKey = null;
            _lastSpokenNode = null;
            _focusHolder = null;
            _pendingFocus = null;
            _liveKey = null;
        }

        /// <summary>The per-frame pull: rebuild + reconcile, establish initial focus when content
        /// appears, apply pending focus requests, and announce any focus-identity change exactly once.</summary>
        public override void EnsureFocus()
        {
            if (Screen == null || _graph == null) return;

            if (_state.CurKey == null && _pendingFocus == null)
            {
                // Unfocused screens stay unfocused until Tab seats a cursor.
                if (Screen.StartUnfocused) return;
                if (!_graph.Rerender()) return; // no content yet — Reconcile will seat the start node once there is
                // Declared initial landing: seat the stop's landing node BEFORE the differ announces below.
                var stop = Screen.InitialFocusStop;
                if (stop != null)
                {
                    var land = KeyGraph.StopLanding(_graph.Current, _graph.State, stop);
                    if (land != null) _graph.Focus(land.Id);
                }
            }
            else
            {
                if (!_graph.Rerender()) return; // nothing focusable this frame — retry
                if (_pendingFocus != null)
                {
                    // One retry frame for a target focused mid-build; a target that still isn't in the
                    // render was removed — drop the request rather than re-seating every frame.
                    if (_graph.Current.Nodes.ContainsKey(_pendingFocus))
                    {
                        _graph.Focus(_pendingFocus);
                        FocusArrived(_graph.CurrentNode);
                        ArmTextEntry(_graph.CurrentNode); // programmatic focus on a field = ready to type
                        if (!_pendingAnnounce) { _lastSpokenKey = _pendingFocus; _lastSpokenNode = _graph.CurrentNode; }
                    }
                    _pendingFocus = null;
                }
                if (_pendingStop != null)
                {
                    var land = KeyGraph.StopLanding(_graph.Current, _graph.State, _pendingStop);
                    if (land != null) _graph.Focus(land.Id);
                    _pendingStop = null; // announce rides the normal differ below
                }
            }

            var node = _graph.CurrentNode;
            if (node == null) return;

            if (_lastSpokenKey == null || !_lastSpokenKey.Equals(node.Id))
            {
                FocusArrived(node); // reconcile-driven moves (a node vanished, a stop landing) blur too
                // Queued (not interrupting): landings follow the screen name / preceding feedback.
                if (FocusMode.Active) Speak(ComposeMove(_lastSpokenNode, node, entry: _lastSpokenNode == null));
                _lastSpokenKey = node.Id;
                _lastSpokenNode = node;
            }

            WatchLive(node);
            WatchTextEntry(node);
        }

        // ---- live announcements: watch the FOCUSED node's Live parts and speak a part when its value
        // changes (an async toggle settling, the game flipping a state). Baselines silently whenever
        // focus lands on a new identity (the focus announcement already spoke the initial state).
        private ControlId _liveKey;
        private readonly List<string> _liveValues = new List<string>();

        private void WatchLive(GraphNode node)
        {
            var anns = GraphAnnouncer.EffectiveAnnouncements(node);
            if (anns.Count == 0) return;
            bool baseline = _liveKey == null || !_liveKey.Equals(node.Id) || _liveValues.Count != anns.Count;
            if (baseline) { _liveKey = node.Id; _liveValues.Clear(); }

            for (int i = 0; i < anns.Count; i++)
            {
                if (anns[i] == null || !anns[i].Live)
                {
                    if (baseline) _liveValues.Add(null);
                    continue;
                }
                string v = null;
                try { v = anns[i].Text?.Invoke(); } catch { }
                if (baseline) { _liveValues.Add(v); continue; }
                if (!string.Equals(_liveValues[i], v))
                {
                    _liveValues[i] = v;
                    // A screen capturing raw input is in "the game is playing" state (a
                    // cutscene, the sandbox's play mode) — the values keep baselining
                    // silently so returning doesn't replay a stale diff, but a focused live
                    // row must not chatter over the game (a register ticking 30×/sec).
                    if (!string.IsNullOrEmpty(v) && FocusMode.Active
                        && Screen?.CapturesRawInput != true)
                        Speak(v, interrupt: false);
                }
            }
        }

        public override void AnnounceCurrent()
        {
            if (_graph == null) return;
            if (_state.CurKey == null && Screen != null && Screen.StartUnfocused) return;
            if (!_graph.Rerender()) return;
            var node = _graph.CurrentNode;
            if (node == null) return;
            Speak(ComposeMove(null, node, entry: true));
            _lastSpokenKey = node.Id;
            _lastSpokenNode = node;
        }

        // ---- input ----

        public override bool OnInputJustPressed(InputAction action)
        {
            // A caret-owning editor node keeps every directional/edit key for its widget — only
            // Tab-stop cycling and screen actions stay ours while it is focused.
            if (_graph?.CurrentNode?.Vtable?.TextEntryCaret == true)
            {
                switch (action.Key)
                {
                    case "ui.up":
                    case "ui.down":
                    case "ui.left":
                    case "ui.right":
                    case "ui.home":
                    case "ui.end":
                    case "ui.pageUp":
                    case "ui.pageDown":
                    case "ui.activate":
                    case "ui.secondary":
                    case "ui.tooltip":
                    case "ui.regionPrev":
                    case "ui.regionNext":
                        return false; // bubble to the game's widget
                }
            }
            switch (action.Key)
            {
                case "ui.up": return Arrow(NavDirection.Up);
                case "ui.down": return Arrow(NavDirection.Down);
                case "ui.left": return Arrow(NavDirection.Left);
                case "ui.right": return Arrow(NavDirection.Right);
                case "ui.next": return Tab(1);
                case "ui.prev": return Tab(-1);
                case "ui.home": return JumpEdge(first: true);
                case "ui.end": return JumpEdge(first: false);
                // The COARSE slider step (PgUp = increase, the screen-reader convention);
                // non-slider nodes bubble.
                case "ui.pageUp": return VtableAdjust(1, large: true);
                case "ui.pageDown": return VtableAdjust(-1, large: true);
                // Region jumps consume only when the focused node is IN a region — elsewhere they bubble.
                case "ui.regionPrev":
                case "ui.regionNext":
                {
                    // Gate on the FRESH graph, not the last frame's: with screen-windowed
                    // content the focused row can vanish between frames (a log cleared by a
                    // re-arm, a tail-following window sliding under a live run) and the stale
                    // node reads null — the press would die silently right when the rebuild
                    // (which re-seats focus and re-pins the window) was about to fix it.
                    if (_graph == null || !_graph.Rerender()) return false;
                    var cn = _graph.CurrentNode;
                    int dir = action.Key == "ui.regionNext" ? 1 : -1;
                    // A node fronting content with a coarser hop unit than its regions jumps
                    // through its own handler — see NodeVtable.OnRegionJump.
                    var over = cn?.Vtable?.OnRegionJump;
                    if (over != null)
                    {
                        bool handled = false;
                        try { handled = over(dir); }
                        catch (System.Exception ex) { Log.Error("[nav] OnRegionJump threw", ex); }
                        if (handled) return true;
                    }
                    return cn?.RegionKey != null && RegionJump(dir);
                }
                case "ui.activate":
                {
                    if (_graph?.CurrentNode == null) return false;
                    VtableActivate();
                    return true;
                }
                case "ui.secondary":
                {
                    var node = _graph?.CurrentNode;
                    if (node == null) return false;
                    if (node.Vtable.TextEntry) return false; // Backspace belongs to the text field
                    if (node.Vtable.OnSecondary != null) _graph.Secondary();
                    return true;
                }
                case "ui.back":
                    return Screen != null && Screen.InvokeAction(ActionIds.Back);
                case "ui.step":
                case "ui.runto":
                case "ui.runto.exa":
                case "ui.followNext": // NOTE: every screen-scoped action id must be listed
                case "ui.followPrev": // here or its binding matches and then goes nowhere
                case "ui.reg.x":
                case "ui.reg.t":
                case "ui.reg.f":
                case "ui.reg.m":
                case "ui.jump.1":
                case "ui.jump.2":
                case "ui.jump.3":
                case "ui.jump.4":
                case "ui.jump.5":
                case "ui.jump.6":
                    // Screen-scoped actions: the focused screen may advertise a handler by id.
                    return Screen != null && Screen.InvokeAction(action.Key);
                case "ui.tooltip":
                {
                    var node = _graph?.CurrentNode;
                    if (node == null) return false;
                    if (node.Vtable.TextEntry) return false; // Space belongs to the text field
                    if (node.Vtable.OnTooltip != null) { _graph.Tooltip(); return true; }
                    Speak(Loc.T("nav.no_tooltip"));
                    return true;
                }
                default:
                    return false;
            }
        }

        private static GraphDir ToDir(NavDirection dir)
        {
            switch (dir)
            {
                case NavDirection.Up: return GraphDir.Up;
                case NavDirection.Down: return GraphDir.Down;
                case NavDirection.Left: return GraphDir.Left;
                default: return GraphDir.Right;
            }
        }

        private bool Arrow(NavDirection dir)
        {
            var focusNode = _graph?.CurrentNode;
            if (focusNode == null) return false;

            // A focused slider/dropdown adjusts on Left/Right (priority over any navigation).
            if (dir == NavDirection.Left || dir == NavDirection.Right)
            {
                if (VtableAdjust(dir == NavDirection.Right ? 1 : -1)) return true;
            }

            // Edge-wired movement first (rows/grids/flattened tree rows all ride edges).
            var move = _graph.Move(ToDir(dir));
            if (move.Moved) { SelectOnLanding(move); AnnounceMove(move); return true; }

            // At an edge. Left/Right get tree semantics: expand/collapse a group, descend into an
            // expanded one, ascend from a child.
            if (dir == NavDirection.Left || dir == NavDirection.Right)
            {
                var tr = dir == NavDirection.Right ? _graph.TreeRight() : _graph.TreeLeft();
                switch (tr.Kind)
                {
                    case KeyGraph.TreeMove.Expanded:
                    case KeyGraph.TreeMove.Collapsed:
                        SpeakFocusedState();
                        return true;
                    case KeyGraph.TreeMove.EmptyGroup:
                        Speak(Loc.T("nav.no_details"), interrupt: true);
                        return true;
                    case KeyGraph.TreeMove.Descended:
                    case KeyGraph.TreeMove.Ascended:
                        AnnounceMove(tr.Move);
                        return true;
                    case KeyGraph.TreeMove.Leaf:
                        return true; // inside a tree; nothing that way — consume
                }
            }

            // Nothing moved: consume edges inside trees; bubble from plain lists so an unfocused
            // screen's arrows can fall through to global handlers.
            return KeyGraph.InTree(focusNode);
        }

        // Speak the focused group's post-toggle state (its full readout includes expanded/collapsed)
        // and rebaseline the differ + live watch so the toggle isn't re-announced.
        private void SpeakFocusedState()
        {
            var node = _graph.CurrentNode;
            if (node == null) return;
            Speak(GraphAnnouncer.LeafText(node), interrupt: true);
            _lastSpokenKey = node.Id;
            _lastSpokenNode = node;
            _liveKey = null;
        }

        private bool Tab(int step)
        {
            // Snapshot BEFORE rerendering: Reconcile auto-seats a null cursor at the start node, and an
            // unfocused screen's Tab must enter at the first stop, not step from that phantom seat.
            bool wasUnfocused = _state.CurKey == null;
            if (_graph == null || !_graph.Rerender()) return false;

            var stops = new List<object>();
            foreach (var n in _graph.Current.Order)
                if (n.StopKey != null && !stops.Contains(n.StopKey)) stops.Add(n.StopKey);
            if (stops.Count == 0) return false;

            var curNode = wasUnfocused ? null : _graph.CurrentNode;
            int idx = curNode != null ? stops.IndexOf(curNode.StopKey) : -1;

            if (idx < 0)
            {
                // Unfocused: Tab enters at the first/last stop.
                return LandOnStop(stops[step >= 0 ? 0 : stops.Count - 1]);
            }

            int ni = idx + step;
            if (ni < 0 || ni >= stops.Count)
            {
                if (Screen != null && Screen.StartUnfocused)
                {
                    Blur(); // truly unfocused → a later re-entry stays unfocused
                    if (!string.IsNullOrEmpty(Screen.ScreenName)) Speak(Screen.ScreenName, interrupt: true);
                    return true;
                }
                if (Screen != null && Screen.Wrap)
                    ni = ((ni % stops.Count) + stops.Count) % stops.Count;
                else
                    return true; // at the end; consume, no wrap
            }
            return LandOnStop(stops[ni]);
        }

        private bool LandOnStop(object stopKey)
        {
            // Remembered position → SELECTED member → first node (the shared StopLanding).
            var land = KeyGraph.StopLanding(_graph.Current, _graph.State, stopKey);
            if (land == null || !_graph.Focus(land.Id)) return true;

            var node = _graph.CurrentNode;
            FocusArrived(node);
            // Stop landings deliberately skip OnSelect — EXCEPT text fields: Tab into a form
            // means "focused field, ready to type" (armed before the announce).
            ArmTextEntry(node);
            Speak(ComposeMove(_lastSpokenNode, node, entry: false), interrupt: true);
            _lastSpokenKey = node.Id;
            _lastSpokenNode = node;
            return true;
        }

        private bool JumpEdge(bool first)
        {
            var focusNode = _graph?.CurrentNode;
            if (focusNode == null) return false;

            // A node fronting screen-windowed content jumps to the DATA's edge, not the graph's
            // (which is only the window's) — see NodeVtable.OnJumpEdge.
            var over = focusNode.Vtable?.OnJumpEdge;
            if (over != null)
            {
                bool handled = false;
                try { handled = over(first); }
                catch (System.Exception ex) { Log.Error("[nav] OnJumpEdge threw", ex); }
                if (handled) return true;
            }

            // In a tree: first/last sibling at the current depth.
            if (KeyGraph.InTree(focusNode))
            {
                var sib = _graph.MoveToSiblingEdge(first);
                if (sib.Moved) { SelectOnLanding(sib); AnnounceMove(sib); }
                return true;
            }

            // First/last along the vertical axis of the current structure.
            var move = _graph.MoveToEdge(first ? GraphDir.Up : GraphDir.Down);
            if (move.Moved) { SelectOnLanding(move); AnnounceMove(move); }
            return true;
        }

        private bool RegionJump(int dir)
        {
            var result = _graph.MoveRegion(dir);
            if (!result.Moved) return true; // no region that way → consume
            SelectOnLanding(result);
            AnnounceMove(result);
            return true;
        }

        // Selection-follows-focus (NodeVtable.OnSelect): run the landing node's select action
        // BEFORE announcing it, so the announcement's live parts read the post-select state.
        private void SelectOnLanding(MoveResult result)
        {
            FocusArrived(result.To);
            var select = result.To?.Vtable?.OnSelect;
            if (select == null) return;
            try { select(); }
            catch (System.Exception ex) { Log.Error("[nav] OnSelect threw", ex); }
        }

        // ---- blur: the node that last received focus. When focus arrives elsewhere its OnBlur
        // runs FIRST (before the newcomer's OnSelect / announce) so a field commits before the
        // landing is read. Keyed by node id: a rebuild's fresh node object for the same id is
        // the same holder. ----
        private GraphNode _focusHolder;

        private void FocusArrived(GraphNode node)
        {
            if (node == null) return;
            var prev = _focusHolder;
            _focusHolder = node;
            if (prev == null || prev.Id == null || prev.Id.Equals(node.Id)) return;
            var blur = prev.Vtable?.OnBlur;
            if (blur == null) return;
            try { blur(); }
            catch (System.Exception ex) { Log.Error("[nav] OnBlur threw", ex); }
        }

        // Text fields arm on EVERY way focus can arrive (stop landings and programmatic focus
        // included — unlike ordinary OnSelect): a focused field is a field ready to type into.
        private static void ArmTextEntry(GraphNode node)
        {
            var vt = node?.Vtable;
            if (vt == null || !vt.TextEntry || vt.OnSelect == null) return;
            try { vt.OnSelect(); }
            catch (System.Exception ex) { Log.Error("[nav] text-entry arm threw", ex); }
        }

        // ---- typing echo: while a TextEntry node is focused, watch its TextValue and speak what
        // was typed or deleted. Baselines silently whenever focus lands on a new identity.
        private ControlId _textKey;
        private string _textVal;
        private object _textIdent;

        private void WatchTextEntry(GraphNode node)
        {
            var vt = node.Vtable;
            if (vt == null || !vt.TextEntry || vt.TextValue == null) { _textKey = null; return; }

            string v = null;
            try { v = vt.TextValue(); } catch { }
            object ident = null;
            try { ident = vt.TextIdentity?.Invoke(); } catch { }

            // A NULL value means "no data yet" (an arm still queued, a field not yet bound) —
            // never diff against it, or the buffer appearing a frame after entry reads as the
            // whole text having been typed. Diffs run only between two non-null reads — and only
            // over the SAME underlying buffer (TextIdentity): a node that fronts whichever buffer
            // the game focuses must re-baseline on a swap, not diff two unrelated texts.
            if (_textKey == null || !_textKey.Equals(node.Id) || v == null || _textVal == null
                || !Equals(ident, _textIdent))
            {
                _textKey = node.Id;
                _textVal = v;
                _textIdent = ident;
                return;
            }
            if (v == _textVal) return;
            string old = _textVal;
            _textVal = v;
            if (!FocusMode.Active) return;

            // interrupt: fast typing should echo the newest key, not queue a backlog.
            // Deletions echo the removed character(s) bare — same voice as typing them
            // (user rule, 2026-08-22). Caps come from the BUFFER only: a widget that stores case
            // announces "Cap X" truthfully; one that normalizes case has no capitals to report,
            // and we never infer state the model doesn't hold (user rule, 2026-08-22).
            // Common-prefix/suffix diff, so caret editors that insert/delete MID-string echo just
            // the changed characters (append-only widgets fall out as the suffix-empty case).
            bool caps = vt.TextEchoCaps;
            int prefix = 0;
            int max = System.Math.Min(old.Length, v.Length);
            while (prefix < max && old[prefix] == v[prefix]) prefix++;
            int suffix = 0;
            while (suffix < max - prefix
                && old[old.Length - 1 - suffix] == v[v.Length - 1 - suffix]) suffix++;
            string removed = old.Substring(prefix, old.Length - prefix - suffix);
            string added = v.Substring(prefix, v.Length - prefix - suffix);

            // F6 (TypingEcho) silences TYPED characters only — deletions always speak.
            if (added.Length > 0 && removed.Length == 0)
            {
                if (TypingEcho.Enabled) Speak(EchoText(added, caps), interrupt: true);
            }
            else if (removed.Length > 0 && added.Length == 0)
                Speak(EchoText(removed, caps), interrupt: true);
            else if (added.Length > 0 && TypingEcho.Enabled)
                Speak(EchoText(added, caps), interrupt: true); // replaced (selection typed over)
        }

        // A typed/deleted chunk, made speakable: spaces say "space", a single capital letter says
        // "Cap <letter>" (user rule, 2026-08-22; suppressible for always-uppercase widgets),
        // anything longer speaks as-is.
        private static string EchoText(string s, bool caps)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s == "\n") return Loc.T("text.newline");
            if (s.Trim().Length == 0) return Loc.T("text.space");
            if (caps && s.Length == 1 && char.IsUpper(s[0])) return Loc.T("text.capital", new { letter = s });
            return s;
        }

        private void AnnounceMove(MoveResult result)
        {
            var node = result.To;
            if (node == null) return;
            Speak(ComposeMove(result.From, node, entry: false, transitionLabel: result.TransitionLabel), interrupt: true);
            _lastSpokenKey = node.Id;
            _lastSpokenNode = node;
        }

        // Run the focused node's vtable activation; speak its StateText as immediate feedback when it
        // declares one, and rebaseline the live watch so the same change isn't spoken twice.
        private bool VtableActivate()
        {
            var node = _graph.CurrentNode;
            if (node?.Vtable.OnActivate == null) return false;
            _graph.Activate();
            node = _graph.CurrentNode;
            var st = node?.Vtable.StateText;
            if (st != null)
            {
                Speak(st(), interrupt: true);
                _liveKey = null; // rebaseline: the change was just spoken synchronously
            }
            return true;
        }

        private bool VtableAdjust(int sign, bool large = false)
        {
            var node = _graph.CurrentNode;
            if (node?.Vtable.OnAdjust == null) return false;
            _graph.TryAdjust(sign, large);
            node = _graph.CurrentNode;
            var st = node?.Vtable.StateText;
            if (st != null)
            {
                Speak(st(), interrupt: true);
                _liveKey = null;
            }
            return true;
        }

        private string ComposeMove(GraphNode from, GraphNode to, bool entry, string transitionLabel = null)
        {
            return GraphAnnouncer.Compose(entry ? null : from, to, transitionLabel);
        }
    }
}

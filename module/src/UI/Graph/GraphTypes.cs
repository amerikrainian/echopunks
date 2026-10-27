using System;
using System.Collections.Generic;

namespace Echopunks.UI.Graph
{
    /// <summary>The four navigable directions between graph nodes (explicit edges). Tab-stop cycling and
    /// region jumps are OPERATIONS over node metadata (<see cref="GraphNode.StopKey"/> /
    /// <see cref="GraphNode.RegionKey"/>), not edges â€” they carry per-stop remembered positions, which a
    /// static edge can't express.</summary>
    public enum GraphDir
    {
        Up,
        Right,
        Down,
        Left,
    }

    /// <summary>The well-known announcement-part kinds. A part's kind is its identity for control-type
    /// ordering, node-over-type overriding, and the user's per-kind announcement settings â€” the same keys
    /// the legacy per-announcement settings used, so the global toggles cover both systems.</summary>
    public static class AnnouncementKinds
    {
        public const string Label = "label";
        public const string Role = "role";
        public const string Value = "value";
        public const string Selected = "selected";
        public const string Enabled = "enabled";
        public const string Tooltip = "tooltip";
        public const string Position = "position";
    }

    /// <summary>
    /// One part of a control's spoken focus readout ("Hold position" / "toggle" / "on"), resolved live at
    /// speak time. A LIVE part is additionally watched while its node is focused: when its resolved text
    /// changes (an async toggle settling, a value the game flips), the navigator speaks just that part
    /// immediately â€” state feedback without re-reading the whole control, and without per-element watcher
    /// machinery.
    /// </summary>
    public sealed class NodeAnnouncement
    {
        /// <summary>The part's text, resolved live. Null/empty at speak time = the part stays silent.</summary>
        public Func<string> Text;

        /// <summary>Watch this part while the node is focused and speak it when its value changes.</summary>
        public bool Live;

        /// <summary>The part's kind (<see cref="AnnouncementKinds"/>), or null for a custom one-off part.
        /// Kinds drive the control type's speak order, let a node's part override the type's common part
        /// of the same kind, and key the user's per-kind announcement settings.</summary>
        public string Kind;

        public NodeAnnouncement(Func<string> text, bool live = false, string kind = null)
        {
            Text = text;
            Live = live;
            Kind = kind;
        }

        public static NodeAnnouncement Static(string text) => new NodeAnnouncement(() => text);
    }

    /// <summary>
    /// A CONTROL TYPE â€” "button", "toggle", "slider" â€” as a registry value rather than a C# class (the
    /// legacy system derived type identity from proxy classes via [AnnouncementOrder]/[ElementSettingsKey],
    /// which forced attribute unions and class collapsing to share settings). A type owns the speak ORDER
    /// of its announcement kinds and the parts COMMON to every control of the type (the localized role
    /// word); nodes contribute their specific parts, overriding a common part of the same kind. The user's
    /// per-type announcement settings key off <see cref="Key"/>.
    /// </summary>
    public sealed class ControlType
    {
        /// <summary>Stable settings/registry key ("button", "toggle", "slider").</summary>
        public string Key;

        /// <summary>The announcement kinds in speak order; parts with unknown/absent kinds append after,
        /// in declaration order.</summary>
        public string[] Order;

        /// <summary>The parts every control of this type shares (the role word), resolved per compose.
        /// Null = none.</summary>
        public Func<IReadOnlyList<NodeAnnouncement>> Common;
    }

    /// <summary>
    /// The behaviors of a control, as data. <see cref="Announcements"/> is required (its parts compose the
    /// spoken focus readout; the first part is the control's label for search/dedupe purposes); the rest
    /// are optional â€” a null slot means the control doesn't have that behavior and the navigator speaks
    /// its "nothing there" feedback instead.
    /// </summary>
    public sealed class NodeVtable
    {
        /// <summary>Required, at least one part. The control's spoken focus readout. Parts marked
        /// <see cref="NodeAnnouncement.Live"/> re-speak on change while focused. When
        /// <see cref="ControlType"/> is set, the type's common parts merge in and the type's kind order
        /// applies; otherwise parts speak in declaration order.</summary>
        public IReadOnlyList<NodeAnnouncement> Announcements;

        /// <summary>The control's type (registry value) â€” supplies the role word, the speak order, and the
        /// per-type announcement settings identity. Null = an untyped one-off.</summary>
        public ControlType ControlType;

        /// <summary>Optional. Primary activation â€” the left-click equivalent (Enter).</summary>
        public Action OnActivate;

        /// <summary>Optional. Selection-follows-focus: invoked when a DIRECTIONAL move (arrows,
        /// Home/End, region jumps) lands focus here -- the "scrolling over a tab selects it"
        /// behavior, run BEFORE the landing is announced so the readout carries the new state.
        /// Tab-stop cycling does NOT trigger it (landing on a stop must not change state; the
        /// landing node is the stop's selected member anyway). Enter (<see cref="OnActivate"/>)
        /// stays a separate gesture -- a list row can select on scroll and open on Enter.</summary>
        public Action OnSelect;

        /// <summary>Optional. Overrides the Home/End jump while this node is focused (the arg is
        /// true for Home/first). Return true when handled; false falls through to the default
        /// walk to the graph's vertical edge. For content WINDOWED by its screen (the editor's
        /// run logs), where the graph's edge is only the window's edge, not the data's — the
        /// handler jumps straight to the data's true edge instead.</summary>
        public Func<bool, bool> OnJumpEdge;

        /// <summary>Optional. Overrides the Ctrl+arrow region jump while this node is focused
        /// (+1 = next, -1 = previous). Return true when handled; false falls through to the
        /// graph's own region move. For content whose natural hop unit is COARSER than its
        /// declared regions (the execution log hops tests, its regions are cycles) — or whose
        /// target lies outside the screen's materialized window, where the graph's region move
        /// cannot see it.</summary>
        public Func<int, bool> OnRegionJump;

        /// <summary>Optional. An arrow pressed while this node is focused and NO edge leads that
        /// way: the node may act instead (return true when handled; false = the default
        /// edge-of-graph behavior). For a direction with SEVERAL candidate destinations an edge
        /// cannot express — the editor's host grid opens a chooser there.</summary>
        public Func<GraphDir, bool> OnArrow;

        /// <summary>Optional (with <see cref="TextValue"/>). Marks a LIVE TYPE-TARGET: while this
        /// node is focused, printable keys belong to a text field (the host game's or ours) — the
        /// input pipeline stands its Space/Backspace bindings down so characters flow, and speaks
        /// typed/deleted characters by watching <see cref="TextValue"/>. Unlike other OnSelect
        /// nodes, Tab-stop landings and programmatic focus DO run OnSelect here: focus on a text
        /// field means "ready to type". Navigation keys (arrows, Tab, Home/End) keep navigating —
        /// pair only with append-style fields that don't need them for caret movement.</summary>
        public bool TextEntry;

        /// <summary>The live text buffer of a <see cref="TextEntry"/> node, for typing echo.</summary>
        public Func<string> TextValue;

        /// <summary>Optional (with <see cref="TextValue"/>). Identity of the UNDERLYING BUFFER when
        /// one node fronts more than one (the EXA code node follows the game's focused EXA). When
        /// the returned value changes between frames the typing echo re-baselines SILENTLY instead
        /// of diffing — two different buffers diffed against each other would speak one whole text
        /// as "deleted" and the other as "typed". Compared with Equals; null is a valid identity.</summary>
        public Func<object> TextIdentity;

        /// <summary>With <see cref="TextEntry"/>: this field is a FULL EDITOR whose widget owns the
        /// caret — arrows, Home/End, Enter, Delete and the rest belong to it, so while focused the
        /// navigator bubbles every directional/activation input and only Tab-stop cycling (and
        /// screen actions) remain ours. Landing on it means "you are editing".</summary>
        public bool TextEntryCaret;

        /// <summary>Echo capital letters as "Cap X" (default), from the BUFFER's case — right for
        /// widgets that store what you type. Set false on widgets that normalize case at insert:
        /// their model holds no capitals, so none are announced — never infer case from the Shift
        /// key; that would report state the field doesn't remember (user rule, 2026-08-22).</summary>
        public bool TextEchoCaps = true;

        /// <summary>Optional. ENGINE-level selection state: stop landings (Tab into a stop, initial
        /// entry) prefer the selected member. Declared here, it is NOT spoken -- the pairing for
        /// OnSelect controls (tabs, list rows), where selection always follows focus and announcing
        /// "selected" would be noise. Controls WITHOUT it (radio options) keep declaring a spoken
        /// Selected-kind announcement part, which the engine falls back to scanning.</summary>
        public Func<bool> Selected;

        /// <summary>Optional. Secondary activation â€” the right-click equivalent (Backspace).</summary>
        public Action OnSecondary;

        /// <summary>Optional. Runs when focus LEAVES this node for another, by any route (arrows,
        /// Tab-stop landings, programmatic focus), BEFORE the newcomer's OnSelect and its
        /// announce -- so a field can commit its edit and the landing readout already carries
        /// the committed state. Not run when the node simply vanishes from a rebuild.</summary>
        public Action OnBlur;

        /// <summary>Optional. Read / open the control's tooltip (Space, F1). The action owns the whole
        /// behavior (speak, or open the drill-in tooltip reader), so the core stays game-agnostic.</summary>
        public Action OnTooltip;

        /// <summary>Optional. Drag/drop participation (Backslash) â€” pick up here, or place the held
        /// thing here. The action owns the whole pick-up/place state machine (see ItemDrag); the core
        /// only dispatches.</summary>
        public Action OnDrag;

        /// <summary>Optional. Horizontal value adjust (a slider): sign is -1 (decrease) / +1 (increase),
        /// large requests a coarse step. When set, left/right do NOT navigate.</summary>
        public Action<int, bool> OnAdjust;

        /// <summary>Optional. The control's state line, spoken IMMEDIATELY (interrupting) after an
        /// activation/adjust that changes state â€” the synchronous feedback path for rapid key repeats.
        /// Asynchronous/game-driven changes ride the Live announcement watch instead.</summary>
        public Func<string> StateText;

        /// <summary>Optional. The text type-ahead matches against; null = the first announcement part
        /// (the label). (A cell whose label is a bare number can search as its row's name, etc.)</summary>
        public Func<string> SearchText;

        /// <summary>If true, type-ahead never matches this control.</summary>
        public bool ExcludeFromSearch;

        /// <summary>Optional (Expandable groups): override HOW expansion state changes. When null the
        /// engine mutates the persistent expansion set (<see cref="GraphState.Expanded"/>); the adapter
        /// wires these to the retained Container's Expand/Collapse instead.</summary>
        public Action OnExpand;
        public Action OnCollapse;

        /// <summary>Set when this group's own announcements already include its expanded/collapsed state
        /// (the adapter's composed element messages do), so the announcer doesn't append it again.</summary>
        public bool SpeaksOwnExpansion;

        /// <summary>Set when this node's announcements already include its list position (the adapter's
        /// composed element messages do), so the announcer doesn't append the auto-stamped one.</summary>
        public bool SpeaksOwnPosition;

        /// <summary>The node's LOGICAL COLUMN in a tabular row (0 = the row's primary), or -1 when not
        /// tabular. Stamped by GraphSheet; the engine uses it to PRESERVE the column when focus jumps
        /// non-directionally â€” the reconcile fallback after a row vanishes, and type-ahead landings â€”
        /// by sliding along the destination row's Left/Right edges to the same column.</summary>
        public int Column = -1;
    }

    /// <summary>A directed edge to another node, with an optional spoken transition line (a "lane
    /// change" â€” e.g. crossing into a new column band). Kept as plain data; contextual announcements are
    /// composed from node metadata by the announcer, not per-edge closures (GC discipline).</summary>
    public sealed class Transition
    {
        public ControlId Destination;
        public string Label; // spoken only while crossing this edge; null = silent edge

        public Transition(ControlId destination, string label = null)
        {
            Destination = destination;
            Label = label;
        }
    }

    /// <summary>A control: identity, behaviors, directional transitions, and structural metadata (its
    /// parent chain, tab-stop and region membership, expandability).</summary>
    public sealed class GraphNode
    {
        public ControlId Id;
        public NodeVtable Vtable;
        public readonly Dictionary<GraphDir, Transition> Transitions = new Dictionary<GraphDir, Transition>();

        /// <summary>The node's structural parent within THIS render, or null at screen level. The parent
        /// chain IS the presentation hierarchy: the announcer prefix-diffs old/new chains by identity, so
        /// entering a group reads its levels outermost-first and descending from a group onto its own
        /// child re-announces nothing (the group is on the chain and is the from-node). A parent may be
        /// non-focusable pure structure (a labeled panel â€” <see cref="Focusable"/> false, never in
        /// Nodes/Order) or a real control (a tree group header).</summary>
        public GraphNode Parent;

        /// <summary>False for a pure-structure parent node (a labeled panel): it exists only on
        /// <see cref="Parent"/> chains for announcements â€” never navigable, never in Nodes/Order.</summary>
        public bool Focusable = true;

        /// <summary>This node is a group that can expand/collapse (a tree section header). The engine's
        /// tree operations (expand/collapse/descend/ascend) key off this.</summary>
        public bool Expandable;

        /// <summary>An <see cref="Expandable"/> group's state AT THIS RENDER (stamped by the builder from
        /// the persistent expansion set, or the explicit value the declarer passed).</summary>
        public bool Expanded;

        /// <summary>The Tab-stop this node belongs to. Nodes sharing a StopKey form one stop; Tab cycles
        /// stops in first-appearance order, landing on the stop's remembered position.</summary>
        public object StopKey;

        /// <summary>The region (within a stop) this node belongs to, or null. Ctrl+Up/Down jumps between
        /// regions in first-appearance order.</summary>
        public object RegionKey;

        /// <summary>Auto-stamped sibling position (1-based) and count, from the builder: menu-mode nodes
        /// grouped by (parent, stop) â€” "3 of 10" among the siblings arrows actually reach. 0 = none
        /// (single sibling, raw/grid nodes, or a multi-item row member positioned within its row).</summary>
        public int PositionIndex;
        public int PositionCount;

        /// <summary>On a parent (context/group) node: its direct children get NO auto position â€” for
        /// log-like streams where "37 of 200" is noise (the old FlowSheet's AnnouncePosition=false).</summary>
        public bool SuppressChildPositions;
    }

    /// <summary>
    /// One built snapshot of a graph: the nodes (keyed by structural identity), their order of
    /// declaration, and where focus starts when there is no prior position. Rebuilt per operation and
    /// thrown away â€” live state belongs in the node callbacks, not here.
    /// </summary>
    public sealed class GraphRender
    {
        public ControlId StartKey;
        public readonly Dictionary<ControlId, GraphNode> Nodes = new Dictionary<ControlId, GraphNode>();

        /// <summary>Declaration order â€” drives stop/region cycling and type-ahead scan order.</summary>
        public readonly List<GraphNode> Order = new List<GraphNode>();

        public GraphNode NodeAt(ControlId key)
        {
            if (key == null) return null;
            GraphNode n;
            return Nodes.TryGetValue(key, out n) ? n : null;
        }
    }

    /// <summary>
    /// The persistent cursor for a graph â€” the only thing that survives between renders. Holds where
    /// focus is, the last computed traversal order (for closest-survivor recovery), per-stop remembered
    /// positions (so Tab returns to where you were in a stop), and a one-shot move request.
    /// </summary>
    public sealed class GraphState
    {
        /// <summary>The focused control's id (carries its Reference for tier-1 recovery). Null until first render.</summary>
        public ControlId CurKey;

        /// <summary>The down-right total order from the previous render. Null on first render.</summary>
        public List<ControlId> KeyOrder;

        /// <summary>If set, focus jumps here on the next render when present (consumed either way).</summary>
        public ControlId NextSuggestedMove;

        /// <summary>The logical column focus last sat on in a tabular row (see NodeVtable.Column), or
        /// -1. The reconcile fallback slides to this column when the focused row vanished.</summary>
        public int LastColumn = -1;

        /// <summary>Remembered position per Tab-stop: where Tab lands when cycling back into a stop.</summary>
        public readonly Dictionary<object, ControlId> StopMemory = new Dictionary<object, ControlId>();

        /// <summary>The expanded groups (by id). The builder consults this for groups declared without an
        /// explicit state; the engine's expand/collapse operations mutate it. Screens hold NO expansion
        /// state of their own.</summary>
        public readonly HashSet<ControlId> Expanded = new HashSet<ControlId>();
    }
}


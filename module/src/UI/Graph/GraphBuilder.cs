using System;
using System.Collections.Generic;

namespace Echopunks.UI.Graph
{
    /// <summary>
    /// Builds a <see cref="GraphRender"/>. Two construction styles, freely mixable in one build:
    ///
    /// <para><b>Menu mode</b> â€” rows of controls, wired automatically: left/right within a row, up/down
    /// between consecutive rows (two rows sharing a non-null row key get column navigation â€” up/down
    /// preserves the position instead of snapping to the first item; ported from Tanglebeep's
    /// MenuBuilder, itself from Factorio Access's menu.lua). Items added outside an explicit row become
    /// single-item rows (a plain vertical menu).</para>
    ///
    /// <para><b>Raw mode</b> â€” <see cref="AddNode"/> + <see cref="Connect"/> for arbitrary topologies.</para>
    ///
    /// Orthogonal to both: <see cref="BeginStop"/> groups nodes into Tab-stops (arrows never cross a stop;
    /// Tab cycles them), <see cref="SetRegion"/> tags nodes with a region for Ctrl+arrow jumps, and the
    /// PARENT STACK builds the presentation hierarchy: <see cref="PushContext"/> pushes a non-focusable
    /// structural level ("Difficulty settings, list" â€” announced when focus enters from outside), while
    /// <see cref="BeginGroup"/> pushes a focusable, EXPANDABLE group header (a tree section) whose children
    /// only emit while it's expanded â€” expansion state lives in the persistent set the builder is
    /// constructed with (<see cref="GraphState.Expanded"/>), so screens hold no tree state of their own.
    /// Nesting recurses; a collapsed ancestor suppresses everything beneath it.
    /// </summary>
    public sealed class GraphBuilder
    {
        private readonly HashSet<ControlId> _expansion; // persistent expanded-group set (null = all explicit)

        public GraphBuilder(HashSet<ControlId> expansion = null) { _expansion = expansion; }

        private sealed class Row
        {
            public readonly List<GraphNode> Items = new List<GraphNode>();
            public object Key;
            public object StopKey;
        }

        private sealed class RawEdge
        {
            public ControlId From;
            public GraphDir Dir;
            public ControlId To;
            public string Label;
        }

        // Menu mode.
        private readonly List<Row> _rows = new List<Row>();
        private Row _currentRow;

        // Raw mode.
        private readonly List<GraphNode> _rawNodes = new List<GraphNode>();
        private readonly List<RawEdge> _rawEdges = new List<RawEdge>();

        // Every node in DECLARATION order regardless of mode â€” the render's node order (and so the
        // Tab-stop cycle) must interleave menu rows and raw nodes as the screen declared them, not
        // rows-then-raw (which shoved FlowSheet stops behind later buttons).
        private readonly List<GraphNode> _declared = new List<GraphNode>();

        // The menu row each menu-mode node belongs to (null for raw nodes) â€” for stitching the
        // vertical gap where a stop mixes menu rows with raw content (a sheet below filter controls).
        private readonly Dictionary<GraphNode, Row> _rowOf = new Dictionary<GraphNode, Row>();

        // Shared.
        private readonly HashSet<ControlId> _ids = new HashSet<ControlId>();
        private ControlId _start;

        // Stop / region / parent state applied to nodes as they are added.
        private object _stopKey = AutoStopKey(0);
        private int _stopAuto = 1;
        private object _regionKey;

        // The parent stack: structural levels (PushContext) and group headers (BeginGroup). A frame whose
        // group is collapsed suppresses every declaration beneath it (the stack stays balanced regardless).
        private sealed class ParentFrame
        {
            public GraphNode Node;      // the parent node (non-focusable context, or the group header)
            public bool Suppressed;     // this frame's subtree is swallowed (collapsed, or under a collapsed ancestor)
        }

        private readonly List<ParentFrame> _parents = new List<ParentFrame>();

        private GraphNode CurrentParent => _parents.Count > 0 ? _parents[_parents.Count - 1].Node : null;
        private bool Suppressed => _parents.Count > 0 && _parents[_parents.Count - 1].Suppressed;

        private static object AutoStopKey(int index) => "stop#" + index;

        // ---- stops / regions ----

        /// <summary>Start a new Tab-stop; nodes added from here belong to it. <paramref name="key"/> must be
        /// stable across rebuilds (it keys the stop's remembered position); null auto-assigns by index,
        /// which is stable when the screen builds its stops in a fixed order.</summary>
        public GraphBuilder BeginStop(object key = null)
        {
            if (_currentRow != null) throw new InvalidOperationException("Cannot begin a stop inside an open row");
            _stopKey = key ?? AutoStopKey(_stopAuto);
            _stopAuto++;
            _regionKey = null; // regions are per-stop
            return this;
        }

        /// <summary>Tag nodes added from here with a region (Ctrl+arrow jump target) within the current
        /// stop; null clears. Region keys must be stable across rebuilds.</summary>
        public GraphBuilder SetRegion(object key)
        {
            _regionKey = key;
            return this;
        }

        // ---- the parent stack: contexts + groups ----

        /// <summary>Push one NON-FOCUSABLE level of presentation hierarchy ("Difficulty settings",
        /// "list") onto nodes added from here â€” pure structure: never navigable, announced when focus
        /// enters from outside. Close with <see cref="PopContext"/>. <paramref name="id"/> overrides
        /// the label-pathed identity where sibling contexts can share a label (table columns with
        /// equal headers) — the announcer diffs parent chains by id, so equal ids never re-announce.</summary>
        public GraphBuilder PushContext(string label, string role = null, bool positions = true,
            ControlId id = null)
        {
            var parent = CurrentParent;
            var anns = new List<NodeAnnouncement> { NodeAnnouncement.Static(label) };
            if (!string.IsNullOrEmpty(role)) anns.Add(NodeAnnouncement.Static(role));
            var node = new GraphNode
            {
                // Stable synthetic identity (label-pathed) so cross-render chain diffs match up.
                Id = id ?? ControlId.Structural("ctx:" + (parent?.Id.StructuralKey ?? "") + "/" + label),
                Vtable = new NodeVtable { Announcements = anns },
                Parent = parent,
                Focusable = false,
                SuppressChildPositions = !positions,
            };
            _parents.Add(new ParentFrame { Node = node, Suppressed = Suppressed });
            return this;
        }

        public GraphBuilder PopContext()
        {
            if (_parents.Count == 0) throw new InvalidOperationException("No context/group to pop");
            _parents.RemoveAt(_parents.Count - 1);
            return this;
        }

        /// <summary>
        /// Push a FOCUSABLE, expandable group header (a tree section): the header emits as a navigable
        /// node here, and the children declared before <see cref="EndGroup"/> emit only while the group is
        /// expanded (a collapsed ancestor suppresses the whole subtree â€” recursion just works). Expansion
        /// state: <paramref name="expanded"/> when given (the adapter passes the retained Container's
        /// state), else the persistent expansion set the builder was constructed with, else
        /// <paramref name="defaultExpanded"/>. The engine's tree operations (Right/Left) expand/collapse
        /// via the vtable's OnExpand/OnCollapse overrides when set, else by mutating the persistent set.
        /// </summary>
        public GraphBuilder BeginGroup(ControlId id, NodeVtable vtable, bool? expanded = null,
            bool defaultExpanded = false)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (_currentRow != null) throw new InvalidOperationException("Cannot begin a group inside an open row");
            bool isExpanded = expanded ?? (_expansion != null ? _expansion.Contains(id) : defaultExpanded);

            GraphNode header = null;
            if (!Suppressed)
            {
                header = MakeNode(id, vtable);
                header.Expandable = true;
                header.Expanded = isExpanded;
                var row = new Row { StopKey = _stopKey };
                row.Items.Add(header);
                _rows.Add(row);
                _rowOf[header] = row;
            }
            _parents.Add(new ParentFrame
            {
                // Suppressed subtree: keep chaining from the outer parent so the stack stays coherent.
                Node = header ?? CurrentParent,
                Suppressed = Suppressed || !isExpanded,
            });
            return this;
        }

        public GraphBuilder EndGroup() => PopContext();

        /// <summary>Whether a group id is expanded in the persistent set â€” for screens that must avoid
        /// even BUILDING a collapsed group's children (a lazy hierarchy whose child VMs materialize on
        /// first access). Groups with an explicit expanded: argument manage their own state instead.</summary>
        public bool IsExpanded(ControlId id) => _expansion != null && id != null && _expansion.Contains(id);

        /// <summary>Focus starts here when the graph has no prior position (defaults to the first node).</summary>
        public GraphBuilder SetStart(ControlId id)
        {
            _start = id;
            return this;
        }

        // ---- menu mode ----

        /// <summary>Open a horizontal row. Rows sharing a non-null <paramref name="rowKey"/> with the row
        /// above/below get column-preserving vertical navigation.</summary>
        public GraphBuilder StartRow(object rowKey = null)
        {
            if (_currentRow != null) throw new InvalidOperationException("Cannot start a row while another is open");
            _currentRow = new Row { Key = rowKey, StopKey = _stopKey };
            return this;
        }

        public GraphBuilder EndRow()
        {
            if (_currentRow == null) throw new InvalidOperationException("No row to end");
            if (_currentRow.Items.Count == 0 && !Suppressed)
                throw new InvalidOperationException("Row cannot be empty");
            if (_currentRow.Items.Count > 0) _rows.Add(_currentRow);
            _currentRow = null;
            return this;
        }

        /// <summary>Add a control â€” into the open row, or as its own single-item row. A no-op inside a
        /// collapsed group's subtree.</summary>
        public GraphBuilder AddItem(ControlId id, NodeVtable vtable)
        {
            if (Suppressed) return this;
            var node = MakeNode(id, vtable);
            if (_currentRow != null)
            {
                _currentRow.Items.Add(node);
                _rowOf[node] = _currentRow;
            }
            else
            {
                var row = new Row { StopKey = _stopKey };
                row.Items.Add(node);
                _rows.Add(row);
                _rowOf[node] = row;
            }
            return this;
        }

        /// <summary>Add a read-only line (label only; no actions).</summary>
        public GraphBuilder AddLabel(ControlId id, Func<string> label)
            => AddItem(id, new NodeVtable { Announcements = new[] { new NodeAnnouncement(label) } });

        // ---- raw mode ----

        /// <summary>Add a node with no automatic wiring (raw mode; wire with <see cref="Connect"/>).
        /// A no-op inside a collapsed group's subtree.</summary>
        public GraphBuilder AddNode(ControlId id, NodeVtable vtable)
        {
            if (Suppressed) return this;
            _rawNodes.Add(MakeNode(id, vtable));
            return this;
        }

        /// <summary>Directed edge <paramref name="from"/> â†’ <paramref name="to"/>, with an optional spoken
        /// transition line ("lane change"). Edges to/from undeclared nodes are dropped at build.</summary>
        public GraphBuilder Connect(ControlId from, GraphDir dir, ControlId to, string label = null)
        {
            if (from == null || to == null)
                throw new ArgumentNullException(from == null ? nameof(from) : nameof(to));
            _rawEdges.Add(new RawEdge { From = from, Dir = dir, To = to, Label = label });
            return this;
        }

        private GraphNode MakeNode(ControlId id, NodeVtable vtable)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (vtable == null || vtable.Announcements == null || vtable.Announcements.Count == 0)
                throw new ArgumentException("A control must have at least one announcement", nameof(vtable));
            if (!_ids.Add(id)) throw new InvalidOperationException("Duplicate control id: " + id);
            var node = new GraphNode
            {
                Id = id,
                Vtable = vtable,
                Parent = CurrentParent,
                StopKey = _stopKey,
                RegionKey = _regionKey,
            };
            _declared.Add(node);
            return node;
        }

        // ---- build ----

        /// <summary>Finalize into a render, or null when nothing was declared (treat as "closed").
        /// Menu rows and raw nodes/edges may coexist in one build (a screen mixing lists with a grid
        /// whose topology is computed): rows wire themselves; raw edges may reference any node.</summary>
        public GraphRender Build()
        {
            if (_currentRow != null) throw new InvalidOperationException("Unclosed row - call EndRow()");
            if (_rawNodes.Count == 0 && _rows.Count == 0) return null;

            var render = new GraphRender();
            foreach (var node in _declared) AddNodeTo(render, node);

            WireMenuEdges(render);
            foreach (var e in _rawEdges)
                if (render.Nodes.ContainsKey(e.From) && render.Nodes.ContainsKey(e.To))
                    render.Nodes[e.From].Transitions[e.Dir] = new Transition(e.To, e.Label);
            StitchModeBoundaries();

            render.StartKey = _start != null && render.Nodes.ContainsKey(_start)
                ? _start
                : render.Order[0].Id;
            StampPositions();
            return render;
        }

        // Where a stop mixes MENU rows with RAW content (search/sort/filter controls above a sheet),
        // the two wiring systems don't see each other: menu auto-wiring connects only menu rows, and
        // the raw content's explicit edges stop at its own borders â€” leaving a vertical gap arrows
        // can't cross. Stitch it: at each menuâ†’raw boundary (declaration order, same stop), the menu
        // row's cells gain Down edges into the first raw node still missing an Up edge, and that node
        // gains the Up back; at rawâ†’menu boundaries the reverse. Only MISSING edges are filled â€” the
        // raw content's own wiring is never overridden.
        private void StitchModeBoundaries()
        {
            var byStop = new Dictionary<object, List<GraphNode>>();
            var stops = new List<object>();
            foreach (var n in _declared)
            {
                if (!byStop.TryGetValue(n.StopKey, out var list))
                {
                    list = new List<GraphNode>();
                    byStop.Add(n.StopKey, list);
                    stops.Add(n.StopKey);
                }
                list.Add(n);
            }

            foreach (var stop in stops)
            {
                var nodes = byStop[stop];
                for (int i = 1; i < nodes.Count; i++)
                {
                    var prev = nodes[i - 1];
                    var cur = nodes[i];
                    bool prevMenu = _rowOf.ContainsKey(prev);
                    bool curMenu = _rowOf.ContainsKey(cur);
                    if (prevMenu == curMenu) continue; // same mode â€” its own wiring covers it

                    if (prevMenu) // menu row above raw content: row cells â†“ first raw node without an Up
                    {
                        if (cur.Transitions.ContainsKey(GraphDir.Up)) continue;
                        var row = _rowOf[prev];
                        foreach (var cell in row.Items)
                            if (!cell.Transitions.ContainsKey(GraphDir.Down))
                                cell.Transitions[GraphDir.Down] = new Transition(cur.Id);
                        cur.Transitions[GraphDir.Up] = new Transition(row.Items[0].Id);
                    }
                    else // raw content above a menu row: last raw node without a Down â†• the row
                    {
                        var row = _rowOf[cur];
                        // The raw side's bottom = the latest raw node (walking back) missing a Down.
                        GraphNode bottom = null;
                        for (int j = i - 1; j >= 0 && !_rowOf.ContainsKey(nodes[j]); j--)
                            if (!nodes[j].Transitions.ContainsKey(GraphDir.Down)) { bottom = nodes[j]; break; }
                        if (bottom == null) continue;
                        bottom.Transitions[GraphDir.Down] = new Transition(row.Items[0].Id);
                        foreach (var cell in row.Items)
                            if (!cell.Transitions.ContainsKey(GraphDir.Up))
                                cell.Transitions[GraphDir.Up] = new Transition(bottom.Id);
                    }
                }
            }
        }

        // Auto-stamp "n of m" positions the way the old containers did: a multi-item row's members are
        // positioned within their ROW (a bar); single-item-row nodes among the siblings sharing their
        // (parent, stop) â€” the vertical list/tree level arrows actually traverse. Raw/grid nodes get
        // none. Announced only when m > 1 (a lone button reads no position).
        private void StampPositions()
        {
            var groups = new Dictionary<object, List<GraphNode>>();
            var keys = new List<object>();
            foreach (var row in _rows)
            {
                if (row.Items.Count > 1)
                {
                    Stamp(row.Items);
                    continue;
                }
                var node = row.Items[0];
                if (node.Parent != null && node.Parent.SuppressChildPositions) continue;
                var key = new KeyValuePair<GraphNode, object>(node.Parent, node.StopKey);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<GraphNode>();
                    groups.Add(key, list);
                    keys.Add(key);
                }
                list.Add(node);
            }
            foreach (var key in keys) Stamp(groups[key]);
        }

        private static void Stamp(List<GraphNode> siblings)
        {
            if (siblings.Count < 2) return;
            for (int i = 0; i < siblings.Count; i++)
            {
                siblings[i].PositionIndex = i + 1;
                siblings[i].PositionCount = siblings.Count;
            }
        }

        private static void AddNodeTo(GraphRender render, GraphNode node)
        {
            render.Nodes.Add(node.Id, node);
            render.Order.Add(node);
        }

        // Left/right within a row; up/down between consecutive rows OF THE SAME STOP (arrows never cross a
        // Tab-stop). Shared non-null row keys preserve the column; otherwise vertical lands on first item.
        private void WireMenuEdges(GraphRender render)
        {
            // Segment rows in DECLARATION order: within a stop, consecutive menu rows chain vertically
            // only when no raw node was declared between them. Interleaved raw content (a sheet between
            // menu controls, e.g. the class progression grid between the skills list and the auto-level
            // button) BREAKS the chain â€” StitchModeBoundaries wires the seams. Without the break, menu
            // edges would skip straight over the raw block; the stitcher (which only fills missing
            // edges) would find the gap already bridged, leaving the block an unreachable island.
            var byStop = new List<List<Row>>();
            var openSegment = new Dictionary<object, List<Row>>(); // stop â†’ its currently-open segment
            foreach (var node in _declared)
            {
                Row row;
                if (_rowOf.TryGetValue(node, out row))
                {
                    List<Row> seg;
                    if (!openSegment.TryGetValue(node.StopKey, out seg))
                    {
                        seg = new List<Row>();
                        openSegment.Add(node.StopKey, seg);
                        byStop.Add(seg);
                    }
                    if (seg.Count == 0 || seg[seg.Count - 1] != row) seg.Add(row);
                }
                else
                {
                    openSegment.Remove(node.StopKey); // raw node: close this stop's segment
                }
            }

            foreach (var rows in byStop)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    var row = rows[r];
                    for (int pos = 0; pos < row.Items.Count; pos++)
                    {
                        var node = row.Items[pos];
                        if (r > 0)
                            node.Transitions[GraphDir.Up] = new Transition(VerticalTarget(row, rows[r - 1], pos));
                        if (r < rows.Count - 1)
                            node.Transitions[GraphDir.Down] = new Transition(VerticalTarget(row, rows[r + 1], pos));
                        if (pos > 0)
                            node.Transitions[GraphDir.Left] = new Transition(row.Items[pos - 1].Id);
                        if (pos < row.Items.Count - 1)
                            node.Transitions[GraphDir.Right] = new Transition(row.Items[pos + 1].Id);
                    }
                }
            }
        }

        // Where vertical navigation from position pos lands in the adjacent row: the same position when
        // the rows share a non-null key (column nav) and it exists there, else the first item.
        private static ControlId VerticalTarget(Row from, Row to, int pos)
        {
            if (from.Key != null && to.Key != null && Equals(from.Key, to.Key) && pos < to.Items.Count)
                return to.Items[pos].Id;
            return to.Items[0].Id;
        }
    }
}


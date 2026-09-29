using System;
using System.Collections.Generic;
using System.Reflection;
using Echopunks.Game;
using Echopunks.UI;
using HarmonyLib;

namespace Echopunks.Patches
{
    /// <summary>
    /// Reads the special-puzzle panels (I/O logs, uplink status, custom-puzzle windows) the way the
    /// game writes them: every panel is real text drawn from inside the puzzle logic's four draw
    /// hooks (GClass298.vmethod_0..3 overrides), so a depth flag around those plus a postfix on the
    /// GClass230 text statics captures each frame's panel text — any puzzle, no per-puzzle code.
    /// PanelText reassembles reading order; the editor screen publishes once per tick and shows the
    /// lines in its goal popup. ForceGoal makes EditorScreen.method_7() report true so the game
    /// itself renders its F1 goal view (full expected streams, ghost files) — draw-only by
    /// construction: the sim's own register reads hardcode the flag false (GClass218).
    /// Capture is armed only while the popup wants it; disarmed, the hot text path costs one
    /// static bool check.
    /// </summary>
    internal static class PanelCapture
    {
        private static bool _armed;
        private static bool _record;
        private static int _depth;
        private static readonly List<PanelCell> Buffer = new List<PanelCell>();
        private const int BufferCap = 1024;

        /// <summary>While true, EditorScreen.method_7() (the F1 show-goal flag) reads true.</summary>
        public static bool ForceGoal { get; private set; }

        /// <summary>Last published frame's panel text, in reading order — text lines and whole
        /// tables.</summary>
        public static List<PanelLine> Lines { get; private set; } = new List<PanelLine>();

        public static void SetArmed(bool armed, bool forceGoal)
        {
            _armed = armed;
            ForceGoal = forceGoal;
            if (!armed && (Buffer.Count > 0 || Lines.Count > 0))
            {
                Buffer.Clear();
                Lines = new List<PanelLine>();
            }
        }

        /// <summary>Once per tick (the editor's OnUpdate): the buffer holds the previous frame's
        /// complete draw — our tick runs from the game-tick prefix, before this frame draws.</summary>
        public static void Publish()
        {
            if (Buffer.Count == 0 && Lines.Count == 0) return;
            Lines = PanelText.Assemble(Buffer);
            Buffer.Clear();
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(Expr.MethodOf(() => default(EditorScreen).method_7()),
                    prefix: new HarmonyMethod(typeof(PanelCapture), nameof(ShowGoalPrefix)));

                // Every override of the four draw hooks, found by slot (the types are enumerated at
                // runtime — their obfuscated names never appear here).
                var slots = new[]
                {
                    Expr.MethodOf(() => default(GClass298).vmethod_0(0f, 0f, false)),
                    Expr.MethodOf(() => default(GClass298).vmethod_1(0f, false)),
                    Expr.MethodOf(() => default(GClass298).vmethod_2(0f, false)),
                    Expr.MethodOf(() => default(GClass298).vmethod_3(0f, false)),
                };
                var prefix = new HarmonyMethod(typeof(PanelCapture), nameof(DrawPrefix));
                var finalizer = new HarmonyMethod(typeof(PanelCapture), nameof(DrawFinalizer));
                Type[] types;
                try { types = typeof(GClass298).Assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                int patched = 0;
                foreach (var type in types)
                {
                    if (type == null || !type.IsSubclassOf(typeof(GClass298))) continue;
                    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        var baseDef = m.GetBaseDefinition();
                        if (baseDef == m || baseDef.DeclaringType != typeof(GClass298)) continue;
                        foreach (var slot in slots)
                        {
                            if (baseDef.MetadataToken != slot.MetadataToken) continue;
                            harmony.Patch(m, prefix: prefix, finalizer: finalizer);
                            patched++;
                        }
                    }
                }

                var text = new HarmonyMethod(typeof(PanelCapture), nameof(TextPostfix));
                var planeText = new HarmonyMethod(typeof(PanelCapture), nameof(PlaneTextPostfix));
                harmony.Patch(Expr.MethodOf(() => GClass230.smethod_33(null, default(Vector2), null,
                        default(Color), default(GEnum165), 0f, 0f, 0f, 0f, 0, default(Color), 0,
                        (Matrix4?)null, 0f, default(Color), false, false)), postfix: planeText);
                harmony.Patch(Expr.MethodOf(() => GClass230.smethod_34(null, default(Vector2), null,
                        default(Color), default(GEnum165), 0f, 0f, 0f, 0f, 0, default(Color), 0,
                        (Matrix4?)null, 0f, default(Color), false, false)), postfix: planeText);
                harmony.Patch(Expr.MethodOf(() => GClass230.smethod_36(null, default(Vector2), null,
                        default(Color), default(GEnum165), 0f, 0f, 0f, 0f, 0, default(Color), 0)),
                    postfix: text);
                // Every I/O LOG and custom-puzzle table goes through ONE private static draw,
                // SpecialPuzzleLogics.smethod_2(Vector2 origin, TableColumn[] columns) — so the
                // tables are recorded STRUCTURALLY (header + values per column) and the text
                // statics stay silent inside it; position heuristics can't tell a blank cell
                // from a missing one.
                try
                {
                    var tableDraw = Deobf.Method(typeof(SpecialPuzzleLogics), "smethod_2");
                    if (tableDraw != null && BindTableColumn(tableDraw))
                        harmony.Patch(tableDraw,
                            prefix: new HarmonyMethod(typeof(PanelCapture), nameof(TablePrefix)),
                            finalizer: new HarmonyMethod(typeof(PanelCapture), nameof(TableFinalizer)));
                    else Log.Error("[patch] panel capture: table draw not bound; tables read as text rows");
                }
                catch (Exception ex) { Log.Error("[patch] panel capture: table draw patch failed", ex); }
                // The CREDITS roll (deob GClass252, pushed when the final story epilogue ends):
                // a timed, non-interactive screen whose every line is a text-static draw from
                // its per-frame imethod_1 — the same tap reads it, no strings duplicated.
                harmony.Patch(Expr.MethodOf(() => default(GClass252).imethod_1(0f)),
                    prefix: new HarmonyMethod(typeof(PanelCapture), nameof(CreditsPrefix)),
                    finalizer: new HarmonyMethod(typeof(PanelCapture), nameof(CreditsFinalizer)));
                Log.Info("[patch] panel capture armed (" + patched + " draw hooks)");
            }
            catch (Exception ex) { Log.Error("[patch] panel capture failed to apply", ex); }
        }

        private static bool _credits;
        private static readonly List<string> CreditsBuffer = new List<string>();

        /// <summary>The text the credits screen drew in its LAST complete frame, in draw order
        /// (name, any extra line, role). Replaced every frame; empty between cards.</summary>
        public static List<string> CreditsFrame { get; private set; } = new List<string>();

        private static void CreditsPrefix()
        {
            _credits = true;
            CreditsBuffer.Clear();
        }

        private static Exception CreditsFinalizer(Exception __exception)
        {
            _credits = false;
            CreditsFrame = new List<string>(CreditsBuffer);
            return __exception;
        }

        // ---- the shared table draw (private nested SpecialPuzzleLogics.TableColumn: header
        // LocString, ExaValue[] values, float x offset; name-preserved type, fields via Deobf) ----

        private static FieldInfo _colHeader, _colValues, _colX;
        private static int _tableDepth;

        private static bool BindTableColumn(MethodInfo tableDraw)
        {
            var ps = tableDraw.GetParameters();
            if (ps.Length != 2 || !ps[1].ParameterType.IsArray) return false;
            var col = ps[1].ParameterType.GetElementType();
            _colHeader = Deobf.Field(col, "locString_0");
            _colValues = Deobf.Field(col, "exaValue_0");
            _colX = Deobf.Field(col, "float_0");
            return _colHeader != null && _colValues != null && _colX != null;
        }

        private static void TablePrefix(object[] __args)
        {
            _tableDepth++;
            if (!_record) return;
            try
            {
                var origin = (Vector2)__args[0];
                var columns = __args[1] as Array;
                if (columns == null || Buffer.Count >= BufferCap) return;
                var table = new PanelTable();
                float minX = float.MaxValue, maxX = float.MinValue;
                foreach (var c in columns)
                {
                    if (c == null) continue;
                    var column = new PanelColumn { Header = _colHeader.GetValue(c)?.ToString() ?? "" };
                    var values = _colValues.GetValue(c) as ExaValue[];
                    // method_2(true) = the raw value: the drawn form (false) wraps strings in
                    // the underline markup — an empty cell draws as markup with no glyphs.
                    if (values != null)
                        foreach (var v in values) column.Values.Add(v.method_2(true) ?? "");
                    table.Columns.Add(column);
                    float x = (float)_colX.GetValue(c);
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }
                if (table.Columns.Count == 0) return;
                // Anchored where the draw letters the headers (origin + (x, -8)).
                Buffer.Add(new PanelCell(table, origin.float_0 + minX, origin.float_1 - 8f, maxX - minX));
            }
            catch { }
        }

        private static Exception TableFinalizer(Exception __exception)
        {
            if (--_tableDepth < 0) _tableDepth = 0;
            return __exception;
        }

        private static bool ShowGoalPrefix(ref bool __result)
        {
            if (!ForceGoal) return true;
            __result = true;
            return false;
        }

        private static void DrawPrefix()
        {
            _depth++;
            _record = _armed;
        }

        // A finalizer, not a postfix — the depth must unwind even when a draw hook throws.
        private static Exception DrawFinalizer(Exception __exception)
        {
            if (--_depth <= 0)
            {
                _depth = 0;
                _record = false;
            }
            return __exception;
        }

        /// <summary>Set by the editor screen: true when a string is one of the current map's
        /// spoken host names. Some logics letter MAP-PLANE decoration through their draw hook —
        /// PB032 paints every host name's floor reflection there, matrix-transformed onto the
        /// isometric plane — and recorded as panel text those restated labels read as phantom
        /// goal rows. Only TRANSFORMED text is tested: a panel caption that names a host (the
        /// PB054 camera panel) is flat screen text and stays.</summary>
        public static Func<string, bool> IsHostName;

        // smethod_33/34 carry the optional plane matrix at parameter 12.
        private static void PlaneTextPostfix(string __0, Vector2 __1, Matrix4? __12)
        {
            if (_credits) { TextPostfix(__0, __1); return; }
            if (!_record) return;
            try
            {
                if (__12.HasValue && IsHostName != null && IsHostName(__0)) return;
            }
            catch { }
            TextPostfix(__0, __1);
        }

        private static void TextPostfix(string __0, Vector2 __1)
        {
            if (_credits)
            {
                try { if (!string.IsNullOrWhiteSpace(__0) && CreditsBuffer.Count < BufferCap) CreditsBuffer.Add(__0); }
                catch { }
                return;
            }
            if (!_record || _tableDepth > 0) return; // table cells arrive whole via TablePrefix
            try
            {
                if (!string.IsNullOrWhiteSpace(__0) && Buffer.Count < BufferCap)
                    Buffer.Add(new PanelCell(__0, __1.float_0, __1.float_1));
            }
            catch { }
        }
    }
}

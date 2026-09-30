using System;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.UI;
using Echopunks.UI.Graph;

namespace Echopunks.Screens
{
    public sealed partial class ExaEditorScreen
    {
        // ---- the file-values popup: Enter on a file row lists EVERY value — one item per
        // value, or one item per AUTHORED ROW when the file declares a row width
        // (FileColumns: a phone number, a table record, a song pair reads as ONE utterance,
        // exactly the line the drawn window force-breaks). The 60-value cap is the summary
        // ROW's compromise; here you arrow at your own pace. Enter or Backspace closes,
        // focus back on the file row. Two sources: a LIVE SimFile, or a GOAL-REQUIRED file
        // from the goal popup (a SimRequiredFile — no SimFile exists for it; addressed by
        // indexes and re-resolved per read, the sim rebuilds every frame; its columns come
        // from the file that BACKS the spec). ----

        private string _popupFile;
        private int _popupFileHost = -1; // ids repeat across hosts — the popup stays host-qualified
        private int _popupGoalHost = -1, _popupGoalRequired = -1; // >= 0: the goal-file source
        private ControlId _popupOrigin; // the row that opened it (files stop, file window, goal popup)

        private bool FilePopupOpen => _popupFile != null || _popupGoalRequired >= 0;

        private void OpenFilePopup(string id, int hostIndex)
            => OpenFilePopup(id, hostIndex, ControlId.Structural("ed.file." + hostIndex + "." + id));

        private void OpenFilePopup(string id, int hostIndex, ControlId origin)
        {
            _popupFile = id;
            _popupFileHost = hostIndex;
            _popupOrigin = origin;
            Navigation.FocusStop("filepop");
        }

        private void OpenGoalFilePopup(int hostIndex, int requiredIndex, ControlId origin)
        {
            _popupGoalHost = hostIndex;
            _popupGoalRequired = requiredIndex;
            _popupOrigin = origin;
            Navigation.FocusStop("filepop");
        }

        private void CloseFilePopup()
        {
            var origin = _popupOrigin;
            _popupFile = null;
            _popupFileHost = -1;
            _popupGoalHost = _popupGoalRequired = -1;
            _popupOrigin = null;
            if (origin != null) Navigation.FocusNode(origin);
        }

        private SimFile PopupFile()
            => _popupFile == null ? null : (FindFileAt(_popupFile, _popupFileHost) ?? FindFile(_popupFile));

        private ExaValue[] PopupGoalValues()
        {
            try
            {
                var sim = TheSim(Editor);
                if (sim == null || _popupGoalHost < 0 || _popupGoalHost >= sim.list_0.Count) return null;
                var required = sim.list_0[_popupGoalHost].list_0;
                return _popupGoalRequired < required.Count ? required[_popupGoalRequired].exaValue_0 : null;
            }
            catch { return null; }
        }

        /// <summary>The active source's value list, whichever kind backs the popup.</summary>
        private System.Collections.Generic.IList<ExaValue> PopupValues()
        {
            try
            {
                if (_popupGoalRequired >= 0) return PopupGoalValues();
                var file = PopupFile();
                return file != null ? file.list_0 : null;
            }
            catch { return null; }
        }

        private bool BuildFilePopup(GraphBuilder b)
        {
            var vals = PopupValues();
            if (vals == null) return false;
            int count = vals.Count;
            string fid = _popupFile;
            // No context, no position counts (user rule): entering announces the VALUE alone,
            // arrowing speaks each next value (or authored row, or prose paragraph) bare.
            b.BeginStop("filepop");
            int rawCols = PopupColumns();
            int rows;
            if (rawCols <= 0 && IsProse(vals))
                rows = Math.Max(1, ProseSegments(vals).Count);
            else
            {
                int cols = Math.Max(1, rawCols);
                rows = Math.Max(1, (count + cols - 1) / cols); // an empty file keeps its one "blank" row
            }
            for (int i = 0; i < rows; i++)
            {
                int vi = i;
                b.AddItem(ControlId.Structural("ed.fpop." + i), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    SpeaksOwnPosition = true, // bare values — no "n of m" (user rule)
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => PopupRowAt(fid, vi), kind: AnnouncementKinds.Label),
                    },
                    OnActivate = CloseFilePopup,
                    OnSecondary = CloseFilePopup,
                });
            }
            return true;
        }

        /// <summary>The active source's authored row width (0 = flat), re-resolved per call
        /// like the values themselves.</summary>
        private int PopupColumns()
        {
            try
            {
                if (_popupGoalRequired >= 0)
                {
                    var sim = TheSim(Editor);
                    if (sim == null || _popupGoalHost < 0 || _popupGoalHost >= sim.list_0.Count) return 0;
                    var required = sim.list_0[_popupGoalHost].list_0;
                    return _popupGoalRequired < required.Count
                        ? RequiredFileColumns(sim, required[_popupGoalRequired]) : 0;
                }
                var file = PopupFile();
                return file != null ? FileColumns(file) : 0;
            }
            catch { return 0; }
        }

        private string PopupRowAt(string id, int row)
        {
            try
            {
                var vals = PopupValues();
                if (vals == null) return null;
                if (vals.Count == 0) return row == 0 ? Loc.T("text.blank") : null; // the empty file's one row
                int rawCols = PopupColumns();
                if (rawCols <= 0 && IsProse(vals))
                {
                    // Prose: one PARAGRAPH per item (the file's own "¶" markers), flow-joined.
                    var segs = ProseSegments(vals);
                    return row < segs.Count ? segs[row] : null;
                }
                int cols = Math.Max(1, rawCols);
                int start = row * cols;
                if (start >= vals.Count) return null;
                var parts = new System.Collections.Generic.List<string>();
                for (int i = start; i < vals.Count && i < start + cols; i++)
                    parts.Add(ValueSpeech(vals[i].method_2(true)));
                return string.Join(", ", parts);
            }
            catch { return null; }
        }

        /// <summary>F2 steps the sim from anywhere on this screen (the game's own Tab-step is
        /// suppressed so Tab stays stop-navigation — see CLAUDE.md).</summary>
        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction("ui.step", StepSim);
            yield return new ElementAction("ui.runto", RunToCaret);
            yield return new ElementAction("ui.runto.exa", RunToPinned);
            // Instance cycling exists only while ARMED (copies can't exist otherwise) —
            // while editing the chord stays the game's, untouched.
            var ed = Editor;
            bool armed = false;
            try { armed = ed != null && !Editing(ed); } catch { }
            if (armed)
            {
                yield return new ElementAction("ui.followNext", () => FollowInstance(1));
                yield return new ElementAction("ui.followPrev", () => FollowInstance(-1));
            }
            // The bare register letters read the focused EXA — never while a text field has
            // focus (code, go-to-cycle): there the letters are typing.
            if (!Navigation.TextEntryFocused)
            {
                yield return new ElementAction("ui.reg.x", () => SpeakRegister('X'));
                yield return new ElementAction("ui.reg.t", () => SpeakRegister('T'));
                yield return new ElementAction("ui.reg.f", () => SpeakRegister('F'));
                yield return new ElementAction("ui.reg.m", () => SpeakRegister('M'));
            }
            for (int i = 0; i < JumpStops.Length; i++)
            {
                string stop = JumpStops[i];
                yield return new ElementAction("ui.jump." + (i + 1), () => JumpToStop(stop));
            }
            // Escape closes the popups (their game-side Escape is suppressed while
            // ModalCapturesEscape holds — see GameKeySuppression). First match wins, so a
            // file popup stacked over the goal popup closes first, back onto its goal row.
            if (FilePopupOpen) yield return new ElementAction(ActionIds.Back, CloseFilePopup);
            if (_goalPopup) yield return new ElementAction(ActionIds.Back, CloseGoalPopup);
        }

        // Alt+1..6 (user layout, 2026-09-30): task, windows, hosts, code, test log (errors +
        // goal flips), solution. A stop the render lacks right now (no run yet = no test log;
        // a popup open = only the popup) speaks "unavailable" instead of silently doing nothing.
        private static readonly string[] JumpStops = { "task", "windows", "hosts", "code", "testlog", "solution" };

        private static void JumpToStop(string stop)
        {
            if (Navigation.StopPresent(stop)) Navigation.FocusStop(stop);
            else Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
        }

        /// <summary>The popups are mod-side only — Escape must close THEM, not act in the game
        /// (reset-or-leave would close the whole task under the popup).</summary>
        public override bool ModalCapturesEscape => FilePopupOpen || _goalPopup;
    }
}

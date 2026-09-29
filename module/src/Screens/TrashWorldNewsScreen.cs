using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Echopunks.Game;
using Echopunks.Localization;
using Echopunks.UI;
using Echopunks.UI.Graph;

namespace Echopunks.Screens
{
    /// <summary>
    /// The TRASH WORLD NEWS launcher (deob GClass214 — obfuscated live): the zine "reference
    /// materials" book. The zines themselves never render in-game — the game's buttons shell-open
    /// the shipped PDFs via method_39 (the PDFs are password-protected against text extraction, so
    /// a screen reader gets little out of them). The mod ships each zine as a TEXT DOCUMENT
    /// (repo docs/game/*.html + images/ → &lt;game&gt;\Echopunks\docs\), and the per-tab DIGITAL VERSION button
    /// opens that document in the user's own editor instead; the PRINTABLE buttons still open the
    /// game's PDFs, byte-identical to the click. Modeled fully: issue tabs (select-on-arrow;
    /// locked ones present as unavailable, unlocking with the same story flags the game checks),
    /// the buttons, close (Escape stays native), and an information stop mirroring the left page's
    /// art-only printing instructions. A button press announces what opened — the game's only
    /// feedback is a viewer window appearing behind the game.
    /// </summary>
    public sealed class TrashWorldNewsScreen : Screen
    {
        public TrashWorldNewsScreen() { Wrap = true; }

        public override string Key => "news";
        public override string ScreenName => GameText.TSpeech("TRASH\nWORLD") + " " + GameText.T("NEWS");
        public override bool IsActive() => GameState.TopScreen() is GClass214;
        public override object InitialFocusStop => "tabs";

        // GClass214's private half via the namemap: the selected tab and the two unlock flags
        // (evaluated by the game when the screen opens — same flags, same moment).
        private static readonly FieldInfo TabField = Deobf.Field(typeof(GClass214), "int_0");
        private static readonly FieldInfo Issue2Field = Deobf.Field(typeof(GClass214), "bool_0");
        private static readonly FieldInfo EpilogueField = Deobf.Field(typeof(GClass214), "bool_1");

        private static GClass214 News => GameState.TopScreen() as GClass214;

        private static int Tab
        {
            get
            {
                var n = News;
                try { return n != null && TabField != null ? (int)TabField.GetValue(n) : 0; }
                catch { return 0; }
            }
        }

        private static void SetTab(int tab)
        {
            var n = News;
            try { if (n != null) TabField?.SetValue(n, tab); }
            catch (Exception ex) { Log.Error("[news] tab switch failed", ex); }
        }

        private static bool Unlocked(FieldInfo flag)
        {
            var n = News;
            try { return n != null && flag != null && (bool)flag.GetValue(n); }
            catch { return false; }
        }

        // The game's own open path (method_2): Content/manual/<name>.pdf via the shell.
        private static void OpenPdf(string name)
        {
            try
            {
                GameLogic.gameLogic_0.method_39(Path.Combine("Content", "manual", name + ".pdf"), false);
                Speech.Tts.Speak(Loc.T("news.opened"));
            }
            catch (Exception ex) { Log.Error("[news] pdf open failed", ex); }
        }

        // The mod's zine documents ship next to the locale tables: <game>\Echopunks\docs\<id>.html
        // (figures in docs\images\), rooted off the HOST dll (the module is byte-loaded and has no
        // disk location — the same anchor LocalizationManager uses). Ids are the file names.
        private static string DocPath(string id) =>
            Path.Combine(Path.GetDirectoryName(typeof(Log).Assembly.Location), "Echopunks", "docs", id + ".html");

        // Shell-open the HTML document (the default browser).
        // A missing document (a hand-copied install without the docs folder) falls back to the
        // game's PDF so the button never goes dead.
        private static void OpenDoc(string id, string pdfFallback)
        {
            string path = DocPath(id);
            try
            {
                if (!File.Exists(path))
                {
                    Log.Warning("[news] zine document missing: " + path + " — opening the PDF instead.");
                    OpenPdf(pdfFallback);
                    return;
                }
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                Speech.Tts.Speak(Loc.T("news.opened.doc"));
            }
            catch (Exception ex) { Log.Error("[news] document open failed: " + path, ex); }
        }

        public override void Build(GraphBuilder b)
        {
            if (News == null) return;

            b.BeginStop("tabs");
            b.StartRow("news.tabs");
            TabNode(b, 0, "news.tab.issue1", () => true);
            TabNode(b, 1, "news.tab.issue2", () => Unlocked(Issue2Field));
            TabNode(b, 2, "news.tab.epilogue", () => Unlocked(EpilogueField));
            b.EndRow();

            b.BeginStop("content");
            int tab = Tab;
            b.PushContext(Loc.T("news.digital.header"));
            Button(b, "news.btn.digital", "news.digital",
                () => OpenDoc(tab == 2 ? "twn-epilogue" : tab == 0 ? "twn-1" : "twn-2",
                    tab == 2 ? "epilogue_en" : tab == 0 ? "digital_en_1" : "digital_en_2"));
            b.PopContext();
            if (tab != 2) // the epilogue ships digital-only, exactly as the game offers it
            {
                b.PushContext(Loc.T("news.print.header"));
                b.StartRow("news.print");
                Button(b, "news.btn.letter", "news.letter",
                    () => OpenPdf(tab == 0 ? "letter_en_1" : "letter_en_2"));
                Button(b, "news.btn.a4", "news.a4",
                    () => OpenPdf(tab == 0 ? "metric_en_1" : "metric_en_2"));
                b.EndRow();
                b.PopContext();
            }

            b.BeginStop("close");
            Button(b, "news.btn.close", "news.close", () => GameApi.PopScreen());

            BuildInfo(b);
        }

        private static void TabNode(GraphBuilder b, int tab, string labelKey, Func<bool> unlocked)
        {
            b.AddItem(ControlId.Structural("news.tab." + tab), new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T(labelKey), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => unlocked() ? null : Loc.T("value.unavailable"),
                        kind: AnnouncementKinds.Enabled),
                },
                Selected = () => Tab == tab,
                // Locked tabs mirror the game's dead click: no switch. The landing already said
                // "unavailable"; Enter repeats it as feedback.
                OnSelect = () => { if (unlocked()) SetTab(tab); },
                OnActivate = () =>
                {
                    if (unlocked()) SetTab(tab);
                    else Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                },
            });
        }

        private static void Button(GraphBuilder b, string id, string labelKey, Action activate)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[] { new NodeAnnouncement(() => Loc.T(labelKey), kind: AnnouncementKinds.Label) },
                OnActivate = activate,
            });
        }

        // The left page: the zine explainer and printing instructions — art-only, mirrored.
        private static void BuildInfo(GraphBuilder b)
        {
            b.BeginStop("info");
            b.PushContext(Loc.T("news.info"), positions: false);
            for (int i = 1; i <= 4; i++)
            {
                string key = "news.info." + i;
                b.AddItem(ControlId.Structural(key), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[] { new NodeAnnouncement(() => Loc.T(key), kind: AnnouncementKinds.Label) },
                });
            }
            b.PopContext();
        }
    }
}

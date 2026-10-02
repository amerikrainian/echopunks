using System;
using System.Runtime.InteropServices;

namespace Echopunks.Speech
{
    /// <summary>
    /// The last-resort handler: put the text on the clipboard so the user's own tools can read it.
    /// WrathAccess uses Unity's copy buffer; here it's raw user32/kernel32 (no WinForms dependency,
    /// no STA requirement). Interrupt is meaningless for a clipboard; ignored.
    /// </summary>
    internal sealed class ClipboardHandler : ISpeechHandler
    {
        public string Key => "clipboard";

        public bool Detect() => true;
        public bool Load() { Log.Info("[speech] clipboard handler active — text lands on the clipboard."); return true; }
        public void Unload() { }

        public bool Speak(string text, bool interrupt)
        {
            bool ok = SetClipboardText(text);
            SpeechTrace.Line("clipboard set -> " + (ok ? "ok" : "FAILED"));
            return ok;
        }
        public bool Output(string text, bool interrupt) => Speak(text, interrupt);
        public void Silence() { }

        private static bool SetClipboardText(string text)
        {
            IntPtr hGlobal = IntPtr.Zero;
            bool opened = false;
            try
            {
                // The clipboard is a contended global: retry briefly if another app holds it.
                for (int attempt = 0; attempt < 5 && !(opened = OpenClipboard(IntPtr.Zero)); attempt++)
                    System.Threading.Thread.Sleep(5);
                if (!opened) return false;

                EmptyClipboard();
                int bytes = (text.Length + 1) * 2;
                hGlobal = Marshal.AllocHGlobal(bytes);
                Marshal.Copy(text.ToCharArray(), 0, hGlobal, text.Length);
                Marshal.WriteInt16(hGlobal, text.Length * 2, 0);
                if (SetClipboardData(CF_UNICODETEXT, hGlobal) != IntPtr.Zero)
                {
                    hGlobal = IntPtr.Zero; // ownership transferred to the system
                    return true;
                }
                return false;
            }
            catch { return false; }
            finally
            {
                if (hGlobal != IntPtr.Zero) Marshal.FreeHGlobal(hGlobal);
                if (opened) CloseClipboard();
            }
        }

        private const uint CF_UNICODETEXT = 13;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();
        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    }
}

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Echopunks.Speech
{
    /// <summary>
    /// Thin P/Invoke layer over prism.dll (https://github.com/ethindp/prism — a unified native
    /// abstraction over screen readers and TTS: NVDA, JAWS, SAPI, OneCore, …). Ported from
    /// WrathAccess/SayTheSpire2; mirrors the subset of the Prism C API the mod needs: context
    /// lifecycle, registry enumeration, and per-backend speak / output / stop / free. prism.dll sits
    /// next to EXAPUNKS.exe (the loader's working dir); it talks to NVDA etc. directly (no client dlls).
    /// </summary>
    internal static class PrismNative
    {
        private const string Dll = "prism";

        // These MUST match prism.h's PrismError enum exactly (ordinal-valued). The earlier WrathAccess
        // port used nominal placeholder values that did not match the current ABI, which made a fresh
        // backend's "already initialized" (15) read as a failure. Kept in prism.h order here.
        public enum PrismError : int
        {
            Ok = 0,
            NotInitialized = 1,
            InvalidParam = 2,
            NotImplemented = 3,
            NoVoices = 4,
            VoiceNotFound = 5,
            SpeakFailure = 6,
            MemoryFailure = 7,
            RangeOutOfBounds = 8,
            Internal = 9,
            NotSpeaking = 10,
            NotPaused = 11,
            AlreadyPaused = 12,
            InvalidUtf8 = 13,
            InvalidOperation = 14,
            AlreadyInitialized = 15,
            BackendNotAvailable = 16,
            Unknown = 17,
            InvalidAudioFormat = 18,
            InternalBackendLimitExceeded = 19,
            BackendEnteredUndefinedState = 20,
            LibraryLoadFailed = 21,
            LibraryInvalid = 22,
            IncompatibleAbi = 23,
        }

        // Every bit prism.h names, so the log's feature listing never degrades to a bare number.
        [Flags]
        public enum BackendFeatures : ulong
        {
            SupportedAtRuntime = 1UL << 0,
            SupportsSpeak = 1UL << 2,
            SupportsSpeakToMemory = 1UL << 3,
            SupportsBraille = 1UL << 4,
            SupportsOutput = 1UL << 5,
            SupportsIsSpeaking = 1UL << 6,
            SupportsStop = 1UL << 7,
            SupportsPause = 1UL << 8,
            SupportsResume = 1UL << 9,
            SupportsSetVolume = 1UL << 10,
            SupportsGetVolume = 1UL << 11,
            SupportsSetRate = 1UL << 12,
            SupportsGetRate = 1UL << 13,
            SupportsSetPitch = 1UL << 14,
            SupportsGetPitch = 1UL << 15,
            SupportsRefreshVoices = 1UL << 16,
            SupportsCountVoices = 1UL << 17,
            SupportsGetVoiceName = 1UL << 18,
            SupportsGetVoiceLanguage = 1UL << 19,
            SupportsGetVoice = 1UL << 20,
            SupportsSetVoice = 1UL << 21,
            SupportsGetChannels = 1UL << 22,
            SupportsGetSampleRate = 1UL << 23,
            SupportsGetBitDepth = 1UL << 24,
            TrimsSilenceOnSpeak = 1UL << 25,
            TrimsSilenceOnSpeakToMemory = 1UL << 26,
            SupportsSpeakSsml = 1UL << 27,
            SupportsSpeakToMemorySsml = 1UL << 28,
        }

        [DllImport(Dll, EntryPoint = "prism_version_string")]
        private static extern IntPtr VersionStringRaw();

        public static string VersionString() => Utf8FromPtr(VersionStringRaw());

        public enum LogLevel : int { Trace = 0, Debug = 1, Info = 2, Warn = 3, Error = 4, None = 5 }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LogCallback(IntPtr userdata, LogLevel level, IntPtr source, IntPtr message);

        [StructLayout(LayoutKind.Sequential)]
        public struct LogHandler
        {
            public IntPtr Fn;
            public IntPtr Userdata;
        }

        [DllImport(Dll, EntryPoint = "prism_set_log_handler")]
        public static extern LogHandler SetLogHandler(LogHandler handler);

        [DllImport(Dll, EntryPoint = "prism_set_log_level")]
        public static extern LogLevel SetLogLevel(LogLevel level);

        public static string Utf8(IntPtr ptr) => Utf8FromPtr(ptr);

        [DllImport(Dll, EntryPoint = "prism_init")]
        public static extern IntPtr Init(IntPtr config);

        [DllImport(Dll, EntryPoint = "prism_shutdown")]
        public static extern void Shutdown(IntPtr ctx);

        [DllImport(Dll, EntryPoint = "prism_registry_count")]
        public static extern UIntPtr RegistryCount(IntPtr ctx);

        [DllImport(Dll, EntryPoint = "prism_registry_id_at")]
        public static extern ulong RegistryIdAt(IntPtr ctx, UIntPtr index);

        [DllImport(Dll, EntryPoint = "prism_registry_name")]
        private static extern IntPtr RegistryNameRaw(IntPtr ctx, ulong id);

        public static string RegistryName(IntPtr ctx, ulong id) =>
            Utf8FromPtr(RegistryNameRaw(ctx, id));

        [DllImport(Dll, EntryPoint = "prism_registry_priority")]
        public static extern int RegistryPriority(IntPtr ctx, ulong id);

        [DllImport(Dll, EntryPoint = "prism_registry_exists")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool RegistryExists(IntPtr ctx, ulong id);

        [DllImport(Dll, EntryPoint = "prism_registry_create")]
        public static extern IntPtr RegistryCreate(IntPtr ctx, ulong id);

        [DllImport(Dll, EntryPoint = "prism_registry_create_best")]
        public static extern IntPtr RegistryCreateBest(IntPtr ctx);

        [DllImport(Dll, EntryPoint = "prism_backend_initialize")]
        public static extern PrismError BackendInitialize(IntPtr backend);

        [DllImport(Dll, EntryPoint = "prism_backend_free")]
        public static extern void BackendFree(IntPtr backend);

        [DllImport(Dll, EntryPoint = "prism_backend_get_features")]
        public static extern ulong BackendGetFeatures(IntPtr backend);

        [DllImport(Dll, EntryPoint = "prism_backend_name")]
        private static extern IntPtr BackendNameRaw(IntPtr backend);

        public static string BackendName(IntPtr backend) =>
            Utf8FromPtr(BackendNameRaw(backend));

        [DllImport(Dll, EntryPoint = "prism_backend_speak")]
        private static extern PrismError BackendSpeakRaw(IntPtr backend, byte[] textUtf8, [MarshalAs(UnmanagedType.I1)] bool interrupt);

        public static PrismError BackendSpeak(IntPtr backend, string text, bool interrupt) =>
            BackendSpeakRaw(backend, Utf8(text), interrupt);

        [DllImport(Dll, EntryPoint = "prism_backend_output")]
        private static extern PrismError BackendOutputRaw(IntPtr backend, byte[] textUtf8, [MarshalAs(UnmanagedType.I1)] bool interrupt);

        public static PrismError BackendOutput(IntPtr backend, string text, bool interrupt) =>
            BackendOutputRaw(backend, Utf8(text), interrupt);

        [DllImport(Dll, EntryPoint = "prism_backend_stop")]
        public static extern PrismError BackendStop(IntPtr backend);

        [DllImport(Dll, EntryPoint = "prism_backend_is_speaking")]
        private static extern PrismError BackendIsSpeakingRaw(IntPtr backend, out byte speaking);

        /// <summary>Diagnostics: "yes"/"no", or the error code when the backend can't tell.</summary>
        public static string BackendIsSpeaking(IntPtr backend)
        {
            byte b;
            var err = BackendIsSpeakingRaw(backend, out b);
            return err == PrismError.Ok ? (b != 0 ? "yes" : "no") : err.ToString();
        }

        private static byte[] Utf8(string s)
        {
            // Native side expects null-terminated UTF-8.
            var len = Encoding.UTF8.GetByteCount(s);
            var buf = new byte[len + 1];
            Encoding.UTF8.GetBytes(s, 0, s.Length, buf, 0);
            return buf;
        }

        // Marshal.PtrToStringUTF8 doesn't exist on net48 — read to the null terminator ourselves.
        private static string Utf8FromPtr(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            if (len == 0) return string.Empty;
            var bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

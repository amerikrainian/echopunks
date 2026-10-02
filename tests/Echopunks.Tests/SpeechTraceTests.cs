using System;
using System.Collections.Generic;
using Echopunks.Speech;
using Xunit;

namespace Echopunks.Tests
{
    public class SpeechTraceTests : IDisposable
    {
        private sealed class FakeHandler : ISpeechHandler
        {
            public string Key { get; set; } = "fake";
            public bool OutputResult = true;
            public List<string> Spoken = new List<string>();
            public bool Detect() => true;
            public bool Load() => true;
            public void Unload() { }
            public bool Speak(string text, bool interrupt) { Spoken.Add(text); return OutputResult; }
            public bool Output(string text, bool interrupt) => Speak(text, interrupt);
            public void Silence() { }
        }

        public SpeechTraceTests()
        {
            Environment.SetEnvironmentVariable("ECHOPUNKS_SPEECH", "auto");
            SpeechTrace.SetEnabledForTests(false); // the default, never the dev machine's speech.trace
        }

        public void Dispose()
        {
            SpeechTrace.SetEnabledForTests(null);
            Environment.SetEnvironmentVariable("ECHOPUNKS_SPEECH", null);
            SpeechManager.ResetForTests(new List<ISpeechHandler>
            {
                new PrismHandler(), new SapiHandler(), new ClipboardHandler(),
            });
        }

        [Theory]
        [InlineData(null, false, false)]
        [InlineData(null, true, true)]
        [InlineData("", true, true)]
        [InlineData("1", false, true)]
        [InlineData("true", false, true)]
        [InlineData("ON", false, true)]
        [InlineData("0", true, false)]
        [InlineData("false", true, false)]
        [InlineData("off", true, false)]
        [InlineData("garbage", true, true)]   // an unparseable env value defers to the setting
        [InlineData("garbage", false, false)]
        public void EnvOverridesTheSettingOnlyWhenItParses(string env, bool setting, bool expected)
        {
            Assert.Equal(expected, SpeechTrace.ResolveEnabled(env, setting));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TracingNeverChangesRoutingOrFallback(bool trace)
        {
            SpeechTrace.SetEnabledForTests(trace);
            var flaky = new FakeHandler { Key = "flaky", OutputResult = false };
            var solid = new FakeHandler { Key = "solid" };
            SpeechManager.ResetForTests(new List<ISpeechHandler> { flaky, solid });

            Tts.Speak("line one\n", interrupt: true);
            SpeechManager.Silence();

            Assert.Equal(new[] { "line one" }, flaky.Spoken);
            Assert.Equal(new[] { "line one" }, solid.Spoken);
        }
    }
}

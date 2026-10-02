using System;
using System.Collections.Generic;
using Echopunks.Speech;
using Xunit;

namespace Echopunks.Tests
{
    public class SpeechManagerTests : IDisposable
    {
        private sealed class FakeHandler : ISpeechHandler
        {
            public string Key { get; set; } = "fake";
            public bool Detectable = true;
            public bool Loadable = true;
            public bool OutputResult = true;
            public int LoadCalls;
            public List<string> Spoken = new List<string>();

            public bool Detect() => Detectable;
            public bool Load() { LoadCalls++; return Loadable; }
            public void Unload() { }
            public bool Speak(string text, bool interrupt) { Spoken.Add(text); return OutputResult; }
            public bool Output(string text, bool interrupt) => Speak(text, interrupt);
            public void Silence() { }
        }

        public SpeechManagerTests()
        {
            Environment.SetEnvironmentVariable("ECHOPUNKS_SPEECH", "auto");
            SpeechTrace.SetEnabledForTests(false); // the default, never the dev machine's speech.trace
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("ECHOPUNKS_SPEECH", null);
            SpeechTrace.SetEnabledForTests(null);
            SpeechManager.ResetForTests(new List<ISpeechHandler>
            {
                new PrismHandler(), new SapiHandler(), new ClipboardHandler(),
            });
        }

        [Fact]
        public void AutoWalksTheChainToTheFirstLoadableHandler()
        {
            var broken = new FakeHandler { Key = "broken", Loadable = false };
            var good = new FakeHandler { Key = "good" };
            SpeechManager.ResetForTests(new List<ISpeechHandler> { broken, good });

            Assert.Same(good, SpeechManager.ResolveHandler("auto"));
            Assert.Equal(1, broken.LoadCalls);
        }

        [Fact]
        public void UndetectedHandlersAreSkippedWithoutLoading()
        {
            var hidden = new FakeHandler { Key = "hidden", Detectable = false };
            var good = new FakeHandler { Key = "good" };
            SpeechManager.ResetForTests(new List<ISpeechHandler> { hidden, good });

            Assert.Same(good, SpeechManager.ResolveHandler("auto"));
            Assert.Equal(0, hidden.LoadCalls);
        }

        [Fact]
        public void ExplicitKeySelectsThatHandler()
        {
            var a = new FakeHandler { Key = "a" };
            var b = new FakeHandler { Key = "b" };
            SpeechManager.ResetForTests(new List<ISpeechHandler> { a, b });

            Assert.Same(b, SpeechManager.ResolveHandler("b"));
        }

        [Fact]
        public void BrokenExplicitChoiceFallsBackToAuto()
        {
            var broken = new FakeHandler { Key = "broken", Loadable = false };
            var good = new FakeHandler { Key = "good" };
            SpeechManager.ResetForTests(new List<ISpeechHandler> { good, broken });

            Assert.Same(good, SpeechManager.ResolveHandler("broken"));
        }

        [Fact]
        public void UnknownKeyGoesAuto()
        {
            var good = new FakeHandler { Key = "good" };
            SpeechManager.ResetForTests(new List<ISpeechHandler> { good });

            Assert.Same(good, SpeechManager.ResolveHandler("no-such-engine"));
        }

        [Fact]
        public void LoadHappensOncePerHandler()
        {
            var good = new FakeHandler { Key = "good" };
            SpeechManager.ResetForTests(new List<ISpeechHandler> { good });

            SpeechManager.ResolveHandler("auto");
            SpeechManager.ResolveHandler("auto");
            Assert.Equal(1, good.LoadCalls);
        }

        [Fact]
        public void OutputRoutesThroughTheResolvedHandler()
        {
            var good = new FakeHandler { Key = "good" };
            SpeechManager.ResetForTests(new List<ISpeechHandler> { good });

            SpeechManager.Output("hello", interrupt: false);
            Assert.Equal(new[] { "hello" }, good.Spoken);
        }

        [Fact]
        public void ARejectedUtteranceRetriesThroughAuto()
        {
            var flaky = new FakeHandler { Key = "flaky", OutputResult = false };
            var solid = new FakeHandler { Key = "solid" };
            SpeechManager.ResetForTests(new List<ISpeechHandler> { flaky, solid });

            Environment.SetEnvironmentVariable("ECHOPUNKS_SPEECH", "flaky");
            SpeechManager.Output("hello", interrupt: false);

            Assert.Equal(new[] { "hello" }, flaky.Spoken);   // tried first
            Assert.Equal(new[] { "hello" }, solid.Spoken);   // not silenced
        }
    }
}

using System;
using System.IO;
using Xunit;

namespace Echopunks.Tests
{
    public class HostConfigTests : IDisposable
    {
        private readonly string _originalPath;
        private readonly string _tempFile;

        public HostConfigTests()
        {
            _originalPath = HostConfig.SettingsPath;
            _tempFile = Path.Combine(Path.GetTempPath(), "EchopunksCfg-" + Guid.NewGuid().ToString("N") + ".json");
        }

        public void Dispose()
        {
            HostConfig.SettingsPath = _originalPath;
            HostConfig.ResetForTests();
            try { File.Delete(_tempFile); } catch { }
        }

        [Fact]
        public void MissingFileYieldsDefaults()
        {
            HostConfig.SettingsPath = _tempFile; // never written
            HostConfig.ResetForTests();
            Assert.Equal("auto", HostConfig.Get("speech.output", "auto"));
        }

        [Fact]
        public void ValuesComeBackByDottedKey()
        {
            File.WriteAllText(_tempFile, "{ \"speech.output\": \"sapi\", \"some.number\": 3 }");
            HostConfig.SettingsPath = _tempFile;
            HostConfig.ResetForTests();
            Assert.Equal("sapi", HostConfig.Get("speech.output", "auto"));
            Assert.Equal("3", HostConfig.Get("some.number", "0"));
        }

        [Fact]
        public void BrokenJsonYieldsDefaults()
        {
            File.WriteAllText(_tempFile, "{ nope");
            HostConfig.SettingsPath = _tempFile;
            HostConfig.ResetForTests();
            Assert.Equal("auto", HostConfig.Get("speech.output", "auto"));
        }

        [Fact]
        public void SetCreatesTheFileAndSurvivesAReload()
        {
            HostConfig.SettingsPath = _tempFile; // never written
            HostConfig.ResetForTests();
            HostConfig.SetBool("speech.typingEcho", false);
            Assert.True(File.Exists(_tempFile));

            HostConfig.ResetForTests();
            Assert.False(HostConfig.GetBool("speech.typingEcho", true));
        }

        [Fact]
        public void SetKeepsEveryOtherKey()
        {
            File.WriteAllText(_tempFile, "{ \"speech.output\": \"sapi\", \"some.number\": 3 }");
            HostConfig.SettingsPath = _tempFile;
            HostConfig.ResetForTests();
            HostConfig.SetBool("speech.typingEcho", true);

            HostConfig.ResetForTests();
            Assert.Equal("sapi", HostConfig.Get("speech.output", "auto"));
            Assert.Equal("3", HostConfig.Get("some.number", "0"));
            Assert.True(HostConfig.GetBool("speech.typingEcho", false));
        }

        [Fact]
        public void SetNeverOverwritesAnUnreadableFile()
        {
            File.WriteAllText(_tempFile, "{ nope");
            HostConfig.SettingsPath = _tempFile;
            HostConfig.ResetForTests();
            HostConfig.SetBool("speech.typingEcho", false);

            Assert.Equal("{ nope", File.ReadAllText(_tempFile));
            Assert.False(HostConfig.GetBool("speech.typingEcho", true)); // still holds in memory
        }

        [Fact]
        public void GetBoolFallsBackOnJunk()
        {
            File.WriteAllText(_tempFile, "{ \"speech.typingEcho\": \"maybe\" }");
            HostConfig.SettingsPath = _tempFile;
            HostConfig.ResetForTests();
            Assert.True(HostConfig.GetBool("speech.typingEcho", true));
        }

        [Fact]
        public void JsonBooleansReadBack()
        {
            // A hand-edited file may carry a real JSON boolean (object ToString = "False").
            File.WriteAllText(_tempFile, "{ \"speech.typingEcho\": false }");
            HostConfig.SettingsPath = _tempFile;
            HostConfig.ResetForTests();
            Assert.False(HostConfig.GetBool("speech.typingEcho", true));
        }
    }
}

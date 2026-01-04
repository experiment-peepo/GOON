using System;
using System.IO;
using System.Threading.Tasks;
using TrainMeX.Classes;
using Xunit;

namespace TrainMeX.Tests {
    public class UserSettingsTests : IDisposable {
        private readonly string _testSettingsFile;

        public UserSettingsTests() {
            _testSettingsFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid()}.json");
            UserSettings.SettingsFilePath = _testSettingsFile;
        }

        [Fact]
        public void Constructor_WithDefaults_SetsDefaultValues() {
            var settings = new UserSettings();
            Assert.Equal(0.2, settings.Opacity);
            Assert.Equal(0.5, settings.Volume);
        }

        [Fact]
        public void Save_AndLoad_RoundTrip() {
            var settings = new UserSettings { Opacity = 0.75, Volume = 0.3 };
            settings.Save();
            
            var loaded = UserSettings.Load();
            Assert.Equal(0.75, loaded.Opacity);
            Assert.Equal(0.3, loaded.Volume);
        }

        #region Edge Cases

        [Fact]
        public void Load_WithCorruptedFile_ReturnsDefaults() {
            File.WriteAllText(_testSettingsFile, "{ invalid json }");
            var settings = UserSettings.Load();
            Assert.NotNull(settings);
            Assert.Equal(0.2, settings.Opacity);
        }

        [Fact]
        public void Load_WithOutOfRangeValues_ClampsToValidRange() {
            File.WriteAllText(_testSettingsFile, "{\"Opacity\": 5.0, \"Volume\": -1.0}");
            var settings = UserSettings.Load();
            Assert.Equal(1.0, settings.Opacity);
            Assert.Equal(0.0, settings.Volume);
        }

        [Fact]
        public void Save_Concurrent_IsThreadSafe() {
            var settings = new UserSettings();
            var tasks = new Task[10];
            for (int i = 0; i < tasks.Length; i++) {
                tasks[i] = Task.Run(() => {
                    for (int j = 0; j < 10; j++) {
                        settings.Save();
                    }
                });
            }
            Task.WaitAll(tasks);
        }

        #endregion

        public void Dispose() {
            try {
                if (File.Exists(_testSettingsFile)) {
                    File.Delete(_testSettingsFile);
                }
            } catch { }
        }
    }
}

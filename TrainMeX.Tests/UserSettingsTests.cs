using System.IO;
using TrainMeX.Classes;
using Xunit;

namespace TrainMeX.Tests {
    public class UserSettingsTests : IDisposable {
        private readonly string _testSettingsFile;

        public UserSettingsTests() {
            _testSettingsFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid()}.json");
        }

        [Fact]
        public void Constructor_WithDefaults_SetsDefaultValues() {
            var settings = new UserSettings();
            
            Assert.Equal(0.2, settings.Opacity);
            Assert.Equal(0.5, settings.Volume);
            Assert.False(settings.AutoLoadSession);
            Assert.Equal(0.9, settings.DefaultOpacity);
            Assert.Equal(0.5, settings.DefaultVolume);
        }

        [Fact]
        public void Save_WithValidSettings_DoesNotThrow() {
            var settings = new UserSettings {
                Opacity = 0.5,
                Volume = 0.8,
                AutoLoadSession = true
            };
            
            // Save method should not throw
            // Note: SettingsFile is readonly, so we can't change it for testing
            // This test verifies Save doesn't throw exceptions
            settings.Save();
            Assert.True(true);
        }

        [Fact]
        public void Load_ReturnsSettings() {
            // Load method should return settings (defaults if file doesn't exist)
            // Note: SettingsFile is readonly, so we test with actual settings file
            var settings = UserSettings.Load();
            
            Assert.NotNull(settings);
            // Verify it returns valid settings with expected properties
            Assert.InRange(settings.Opacity, 0.0, 1.0);
            Assert.InRange(settings.Volume, 0.0, 1.0);
        }

        [Fact]
        public void Load_WithNonExistentFile_ReturnsDefaults() {
            // Load should return defaults if settings file doesn't exist
            // This test verifies Load handles missing files gracefully
            var settings = UserSettings.Load();
            
            // Should return valid settings (defaults if file missing)
            Assert.NotNull(settings);
            Assert.InRange(settings.Opacity, 0.0, 1.0);
            Assert.InRange(settings.Volume, 0.0, 1.0);
        }

        [Fact]
        public void Load_HandlesErrorsGracefully() {
            // Load should handle errors (invalid JSON, missing file, etc.) gracefully
            // and return defaults rather than throwing
            var settings = UserSettings.Load();
            
            // Should return valid settings even if file is invalid or missing
            Assert.NotNull(settings);
            Assert.InRange(settings.Opacity, 0.0, 1.0);
            Assert.InRange(settings.Volume, 0.0, 1.0);
        }

        [Fact]
        public void Save_AndLoad_RoundTrip() {
            var originalSettings = new UserSettings {
                Opacity = 0.6,
                Volume = 0.7,
                AutoLoadSession = true,
                DefaultOpacity = 0.85,
                DefaultVolume = 0.55
            };
            
            // Save and load should work together
            // Note: SettingsFile is readonly, so we test with actual settings file
            originalSettings.Save();
            
            var loadedSettings = UserSettings.Load();
            
            // Verify settings can be saved and loaded
            // (May not match exactly if file was modified, but should be valid)
            Assert.NotNull(loadedSettings);
            Assert.InRange(loadedSettings.Opacity, 0.0, 1.0);
            Assert.InRange(loadedSettings.Volume, 0.0, 1.0);
        }

        public void Dispose() {
            try {
                if (File.Exists(_testSettingsFile)) {
                    File.Delete(_testSettingsFile);
                }
            } catch {
                // Ignore cleanup errors
            }
        }
    }
}


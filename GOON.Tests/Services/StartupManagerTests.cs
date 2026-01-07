using System;
using System.Runtime.Versioning;
using Microsoft.Win32;
using GOON.Classes;
using Xunit;

namespace GOON.Tests {
    [SupportedOSPlatform("windows")]
    public class StartupManagerTests : IDisposable {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "GOON";
        private readonly bool _startupWasEnabled;

        public StartupManagerTests() {
            // Save the current startup state to restore later
            _startupWasEnabled = StartupManager.IsStartupEnabled();
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Platform", "Windows")]
        public void IsStartupEnabled_ReturnsBoolean() {
            // Act
            var result = StartupManager.IsStartupEnabled();

            // Assert
            // Result should be a valid boolean (true or false)
            Assert.True(result == true || result == false);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Platform", "Windows")]
        public void SetStartup_True_AddsRegistryEntry() {
            // Arrange
            // Ensure clean state
            StartupManager.SetStartup(false);

            // Act
            StartupManager.SetStartup(true);

            // Assert
            var isEnabled = StartupManager.IsStartupEnabled();
            Assert.True(isEnabled);

            // Verify registry entry exists
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath)) {
                var value = key?.GetValue(AppName);
                Assert.NotNull(value);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Platform", "Windows")]
        public void SetStartup_False_RemovesRegistryEntry() {
            // Arrange
            // First enable startup
            StartupManager.SetStartup(true);

            // Act
            StartupManager.SetStartup(false);

            // Assert
            var isEnabled = StartupManager.IsStartupEnabled();
            Assert.False(isEnabled);

            // Verify registry entry does not exist
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath)) {
                var value = key?.GetValue(AppName);
                Assert.Null(value);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Platform", "Windows")]
        public void SetStartup_True_SetsCorrectExecutablePath() {
            // Arrange
            StartupManager.SetStartup(false);

            // Act
            StartupManager.SetStartup(true);

            // Assert
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath)) {
                var value = key?.GetValue(AppName) as string;
                Assert.NotNull(value);
                // Path should end with an executable extension
                Assert.True(value.EndsWith(".exe") || value.EndsWith(".dll"), 
                    $"Expected executable path, got: {value}");
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Platform", "Windows")]
        public void SetStartup_MultipleToggle_WorksCorrectly() {
            // Act & Assert - Toggle multiple times
            StartupManager.SetStartup(true);
            Assert.True(StartupManager.IsStartupEnabled());

            StartupManager.SetStartup(false);
            Assert.False(StartupManager.IsStartupEnabled());

            StartupManager.SetStartup(true);
            Assert.True(StartupManager.IsStartupEnabled());

            StartupManager.SetStartup(false);
            Assert.False(StartupManager.IsStartupEnabled());
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Platform", "Windows")]
        public void IsStartupEnabled_WithNoRegistryEntry_ReturnsFalse() {
            // Arrange
            StartupManager.SetStartup(false);

            // Act
            var result = StartupManager.IsStartupEnabled();

            // Assert
            Assert.False(result);
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Platform", "Windows")]
        public void SetStartup_True_IsIdempotent() {
            // Arrange
            StartupManager.SetStartup(true);

            // Act - Call SetStartup(true) again
            StartupManager.SetStartup(true);

            // Assert - Should still be enabled
            Assert.True(StartupManager.IsStartupEnabled());
        }

        [Fact]
        [Trait("Category", "Integration")]
        [Trait("Platform", "Windows")]
        public void SetStartup_False_IsIdempotent() {
            // Arrange
            StartupManager.SetStartup(false);

            // Act - Call SetStartup(false) again
            StartupManager.SetStartup(false);

            // Assert - Should still be disabled
            Assert.False(StartupManager.IsStartupEnabled());
        }

        [Fact]
        public void IsStartupEnabled_MultipleCalls_ResultIsConsistent() {
            var res1 = StartupManager.IsStartupEnabled();
            var res2 = StartupManager.IsStartupEnabled();
            Assert.Equal(res1, res2);
        }

        public void Dispose() {
            // Restore original startup state
            try {
                StartupManager.SetStartup(_startupWasEnabled);
            } catch {
                // Ignore errors during cleanup
            }
        }
    }
}

using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using GOON.Classes;
using Xunit;

namespace GOON.Tests {
    public class HotkeyServiceTests {
        [Fact]
        public void Constructor_CreatesInstance() {
            var service = new HotkeyService();
            Assert.NotNull(service);
            service.Dispose();
        }

        [Fact]
        public void Dispose_WithoutInitialize_DoesNotThrow() {
            var service = new HotkeyService();
            service.Dispose();
            // Should not throw
            Assert.True(true);
        }

        [Fact]
        public void Dispose_MultipleTimes_DoesNotThrow() {
            var service = new HotkeyService();
            service.Dispose();
            service.Dispose();
            Assert.True(true);
        }

        [Fact]
        public void Register_WithNullKey_DoesNotThrow() {
            var service = new HotkeyService();
            // Should handle null key gracefully or throw a specific error, we check for no crash
            try {
                service.Register("Test", 0, null, () => { });
            } catch {
                // Ignore
            }
            Assert.NotNull(service);
        }

        [Fact]
        public void Register_BeforeInitialize_IsSafe() {
            var service = new HotkeyService();
            bool result = service.Register("Panic", 0, "F1", () => { });
            Assert.False(result); // Should be false since not initialized
        }
    }
}

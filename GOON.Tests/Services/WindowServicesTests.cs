using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Forms;
using GOON.Classes;
using Xunit;

namespace GOON.Tests {
    [SupportedOSPlatform("windows")]
    public class WindowServicesTests {
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void GetAllScreens_ReturnsNonNullArray() {
            // Act
            var screens = WindowServices.GetAllScreens();

            // Assert
            Assert.NotNull(screens);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void GetAllScreens_ReturnsAtLeastOneScreen() {
            // Act
            var screens = WindowServices.GetAllScreens();

            // Assert
            Assert.True(screens.Length >= 1, "Expected at least one screen");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void GetNumberOfScreens_ReturnsPositiveNumber() {
            // Act
            var count = WindowServices.GetNumberOfScreens();

            // Assert
            Assert.True(count > 0, "Screen count should be positive");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void GetNumberOfScreens_MatchesGetAllScreensLength() {
            // Act
            var screens = WindowServices.GetAllScreens();
            var count = WindowServices.GetNumberOfScreens();

            // Assert
            Assert.Equal(screens.Length, count);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void GetAllScreenViewers_ReturnsNonNullList() {
            // Act
            var screenViewers = WindowServices.GetAllScreenViewers();

            // Assert
            Assert.NotNull(screenViewers);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void GetAllScreenViewers_CountMatchesScreenCount() {
            // Act
            var screenViewers = WindowServices.GetAllScreenViewers();
            var screenCount = WindowServices.GetNumberOfScreens();

            // Assert
            Assert.Equal(screenCount, screenViewers.Count);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void GetAllScreenViewers_ContainsValidScreenViewers() {
            // Act
            var screenViewers = WindowServices.GetAllScreenViewers();

            // Assert
            Assert.All(screenViewers, screenViewer => {
                Assert.NotNull(screenViewer);
                Assert.NotNull(screenViewer.Screen);
            });
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void MoveWindowToScreen_WithNullWindow_DoesNotThrow() {
            // Arrange
            var screens = WindowServices.GetAllScreens();
            var screen = screens.Length > 0 ? screens[0] : null;

            // Act & Assert - Should not throw
            WindowServices.MoveWindowToScreen(null, screen);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void MoveWindowToScreen_WithNullScreen_DoesNotThrow() {
            // Arrange
            Window window = null;
            try {
                // Create a window for testing (may require Application context)
                window = new Window();
            } catch {
                // If we can't create a window, skip this test
                return;
            }

            // Act & Assert - Should not throw
            try {
                WindowServices.MoveWindowToScreen(window, null);
            } finally {
                window?.Close();
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void MoveWindowToScreen_WithBothNull_DoesNotThrow() {
            // Act & Assert - Should not throw
            WindowServices.MoveWindowToScreen(null, null);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void GetAllScreens_ConsecutiveCalls_ReturnConsistentResults() {
            // Act
            var screens1 = WindowServices.GetAllScreens();
            var screens2 = WindowServices.GetAllScreens();

            // Assert
            Assert.Equal(screens1.Length, screens2.Length);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void Constants_WS_EX_TRANSPARENT_HasCorrectValue() {
            // Assert
            Assert.Equal(0x00000020, WindowServices.WS_EX_TRANSPARENT);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void Constants_GWL_EXSTYLE_HasCorrectValue() {
            // Assert
            Assert.Equal(-20, WindowServices.GWL_EXSTYLE);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void GetAllScreens_EachScreenHasValidBounds() {
            // Act
            var screens = WindowServices.GetAllScreens();

            // Assert
            Assert.All(screens, screen => {
                Assert.NotNull(screen);
                Assert.True(screen.Bounds.Width > 0, "Screen width should be positive");
                Assert.True(screen.Bounds.Height > 0, "Screen height should be positive");
            });
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Platform", "Windows")]
        public void GetAllScreens_AtLeastOnePrimaryScreen() {
            // Act
            var screens = WindowServices.GetAllScreens();

            // Assert
            var primaryScreenCount = 0;
            foreach (var screen in screens) {
                if (screen.Primary) {
                    primaryScreenCount++;
                }
            }
            Assert.Equal(1, primaryScreenCount);
        }
    }
}

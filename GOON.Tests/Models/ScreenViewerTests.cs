using System.Windows.Forms;
using GOON.Classes;
using Xunit;

namespace GOON.Tests {
    public class ScreenViewerTests {
        [Fact]
        public void Constructor_WithScreen_SetsProperties() {
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var viewer = new ScreenViewer(screen);
            
            Assert.Same(screen, viewer.Screen);
            Assert.Equal(screen.DeviceName, viewer.DeviceName);
        }

        [Fact]
        public void Equals_WithSameDeviceName_ReturnsTrue() {
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var viewer1 = new ScreenViewer(screen);
            var viewer2 = new ScreenViewer(screen);
            
            Assert.True(viewer1.Equals(viewer2));
        }

        [Fact]
        public void Equals_WithDifferentDeviceName_ReturnsFalse() {
            var screens = Screen.AllScreens;
            if (screens.Length >= 2) {
                var viewer1 = new ScreenViewer(screens[0]);
                var viewer2 = new ScreenViewer(screens[1]);
                
                Assert.False(viewer1.Equals(viewer2));
            }
        }

        [Fact]
        public void Equals_WithNull_ReturnsFalse() {
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var viewer = new ScreenViewer(screen);
            
            Assert.False(viewer.Equals(null));
        }

        [Fact]
        public void GetHashCode_WithSameDeviceName_ReturnsSameHash() {
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var viewer1 = new ScreenViewer(screen);
            var viewer2 = new ScreenViewer(screen);
            
            Assert.Equal(viewer1.GetHashCode(), viewer2.GetHashCode());
        }

        [Fact]
        public void ToString_ReturnsFormattedString() {
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var viewer = new ScreenViewer(screen);
            
            var result = viewer.ToString();
            
            Assert.NotNull(result);
            Assert.Contains("Screen", result);
        }

        [Fact]
        public void ToString_WithPrimaryScreen_ContainsPrimary() {
            var primaryScreen = Screen.PrimaryScreen;
            if (primaryScreen != null) {
                var viewer = new ScreenViewer(primaryScreen);
                
                var result = viewer.ToString();
                
                Assert.Contains("Primary", result);
            }
        }
    }
}


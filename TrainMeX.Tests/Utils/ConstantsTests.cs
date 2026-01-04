using TrainMeX.Classes;
using Xunit;

namespace TrainMeX.Tests {
    public class ConstantsTests {
        [Fact]
        public void VideoExtensions_ContainsExpectedExtensions() {
            var extensions = Constants.VideoExtensions;
            
            Assert.Contains(".mp4", extensions);
            Assert.Contains(".mkv", extensions);
            Assert.Contains(".avi", extensions);
            Assert.Contains(".mov", extensions);
            Assert.Contains(".wmv", extensions);
        }

        [Fact]
        public void VideoExtensions_AllExtensionsStartWithDot() {
            foreach (var ext in Constants.VideoExtensions) {
                Assert.StartsWith(".", ext);
            }
        }

        [Fact]
        public void MaxFileCacheSize_IsPositive() {
            Assert.True(Constants.MaxFileCacheSize > 0);
        }

        [Fact]
        public void CacheTtlMinutes_IsPositive() {
            Assert.True(Constants.CacheTtlMinutes > 0);
        }

        [Fact]
        public void MaxRetryAttempts_IsPositive() {
            Assert.True(Constants.MaxRetryAttempts > 0);
        }

        [Fact]
        public void RetryBaseDelayMs_IsPositive() {
            Assert.True(Constants.RetryBaseDelayMs > 0);
        }

        [Fact]
        public void MaxFileSizeBytes_IsPositive() {
            Assert.True(Constants.MaxFileSizeBytes > 0);
        }

        [Fact]
        public void FileSizeWarningThreshold_IsPositive() {
            Assert.True(Constants.FileSizeWarningThreshold > 0);
        }

        [Fact]
        public void FileSizeWarningThreshold_IsLessThanMaxFileSize() {
            Assert.True(Constants.FileSizeWarningThreshold < Constants.MaxFileSizeBytes);
        }
    }
}


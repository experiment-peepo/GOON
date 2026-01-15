using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using GOON.ViewModels;
using GOON.Classes;
using Moq;
using FluentAssertions;

namespace GOON.Tests.Stability {
    public class PlaybackStabilityTests : TestBase {
        
        [Fact]
        public async Task RapidSkipStressTest_ShouldNotCrash() {
            // Arrange
            // Mock settings to avoid null refs
            MockSettings.Setup(s => s.VideoShuffle).Returns(false);
            MockSettings.Setup(s => s.RememberFilePosition).Returns(false);
            
            var vm = new HypnoViewModel(MockExtractor.Object);
            var files = Enumerable.Range(0, 50)
                .Select(i => new VideoItem($"https://example.com/video{i}.mp4"))
                .ToArray();
            
            vm.SetQueue(files);
            
            // Act & Assert
            // Rapidly skip videos to trigger race conditions
            // This tests the hardening in HypnoViewModel.PlayNext and LoadCurrentVideo
            for (int i = 0; i < 50; i++) {
                vm.PlayNext(force: true);
                if (i % 5 == 0) await Task.Delay(1); // Mix immediate and slightly delayed skips
            }
            
            // Should reach here without AccessViolationException or NullReferenceException
            vm.Dispose();
        }

        [Fact]
        public async Task ConcurrentDisposeTest_ShouldCancelGracefully() {
            // Arrange
            // Simulate a slow URL extraction that respects cancellation
            MockExtractor.Setup(x => x.ExtractVideoUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string url, CancellationToken ct) => {
                    await Task.Delay(50, ct);
                    return url;
                });

            // Run multiple times to increase chance of hitting race conditions
            for (int i = 0; i < 20; i++) {
                var vm = new HypnoViewModel(MockExtractor.Object);
                var item = new VideoItem("https://corrupted-site.com/video.php?id=123");
                item.OriginalPageUrl = item.FilePath;
                
                vm.SetQueue(new[] { item });
                
                // Act
                await Task.Delay(new Random().Next(1, 40)); // Random delay to hit different load stages
                vm.Dispose(); 
                
                // Assert: No crash or orphaned tasks causing unhandled exceptions
            }
        }
        
        [Fact]
        public void SetQueue_WhileDisposed_ShouldReturnEarly() {
            // Arrange
            var vm = new HypnoViewModel(MockExtractor.Object);
            vm.Dispose();
            
            // Act
            var files = new[] { new VideoItem("test.mp4") };
            Action act = () => vm.SetQueue(files);
            
            // Assert
            act.Should().NotThrow();
        }
    }
}

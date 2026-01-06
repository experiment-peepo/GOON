using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using TrainMeX.Classes;
using TrainMeX.ViewModels;
using Xunit;

namespace TrainMeX.Tests {
    public class PlaylistImporterTests {
        private readonly Mock<IHtmlFetcher> _mockFetcher;
        private readonly Mock<VideoUrlExtractor> _mockUrlExtractor; // We can mock the class if virtual, or just use a real one since we mock fetcher
        private readonly PlaylistImporter _importer;

        public PlaylistImporterTests() {
            _mockFetcher = new Mock<IHtmlFetcher>();
            
            // Setup default behavior for ResolveRedirectUrlAsync (return URL unchanged)
            _mockFetcher.Setup(f => f.ResolveRedirectUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, string referer, CancellationToken ct) => url);
            
            // We can use a real extractor with the mocked fetcher to test integration, 
            // or we might want to mock the extractor to isolate playlist logic.
            // Since VideoUrlExtractor methods are not virtual, we can't easily mock it with Moq unless we wrap it or extract interface.
            // However, PlaylistImporter uses ExtractVideoUrlAsync which is on VideoUrlExtractor. 
            // Let's use a real VideoUrlExtractor but with the mocked fetcher, so efficient unit testing is possible.
            var extractor = new VideoUrlExtractor(_mockFetcher.Object);
            
            _importer = new PlaylistImporter(extractor, _mockFetcher.Object);
        }

        [Fact]
        public async Task ImportPlaylist_Hypnotube_FindsVideoLinks() {
            // Arrange
            string playlistUrl = "https://hypnotube.com/playlist/123";
            string playlistHtml = @"
                <html>
                    <body>
                        <a href=""/video/video1.html"">Video 1</a>
                        <a href=""/video/video2.html"">Video 2</a>
                        <a href=""/other/link"">Not a video</a>
                    </body>
                </html>";

            // Mock playlist page fetch
            _mockFetcher.Setup(f => f.FetchHtmlAsync(playlistUrl, It.IsAny<CancellationToken>()))
                .ReturnsAsync(playlistHtml);
            
            // Mock individual video page fetches (needed for title extraction and validation)
            string videoPageHtml = "<html><title>Test Video</title><video src='https://cdn.hypnotube.com/vid.mp4'></video></html>";
            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.Is<string>(s => s.Contains("/video/")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(videoPageHtml);

            // Act
            var result = await _importer.ImportPlaylistAsync(playlistUrl);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, i => i.FilePath.Contains("cdn.hypnotube.com/vid.mp4")); // It resolves to direct URL
        }

        [Fact]
        public async Task ImportPlaylist_Generic_FindsVideoLinks() {
            // Arrange
            string playlistUrl = "https://example.com/playlist";
            string playlistHtml = @"
                <html>
                    <body>
                        <a href=""https://example.com/video1.mp4"">Video 1</a>
                        <a href=""https://example.com/video2.mp4"">Video 2</a>
                    </body>
                </html>";

            _mockFetcher.Setup(f => f.FetchHtmlAsync(playlistUrl, It.IsAny<CancellationToken>()))
                .ReturnsAsync(playlistHtml);

            // For direct video links, Extractor might fetching check, but our mock handles HTML.
            // Direct file links usually don't need fetching if validated by extension.

            // Act
            var result = await _importer.ImportPlaylistAsync(playlistUrl);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.All(result, item => Assert.EndsWith(".mp4", item.FilePath));
        }

        [Fact]
        public async Task ImportPlaylist_HandlesEmptyHtml() {
            // Arrange
            string playlistUrl = "https://example.com/empty";
            _mockFetcher.Setup(f => f.FetchHtmlAsync(playlistUrl, It.IsAny<CancellationToken>()))
                .ReturnsAsync("");

            // Act
            var result = await _importer.ImportPlaylistAsync(playlistUrl);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task ImportPlaylist_CancelsCorrectly() {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();
            string playlistUrl = "https://example.com/playlist";

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () => 
                await _importer.ImportPlaylistAsync(playlistUrl, cancellationToken: cts.Token));
        }
    }
}

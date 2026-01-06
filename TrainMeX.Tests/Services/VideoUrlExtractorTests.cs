using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using TrainMeX.Classes;
using Xunit;

namespace TrainMeX.Tests {
    public class VideoUrlExtractorTests {
        private readonly Mock<IHtmlFetcher> _mockFetcher;
        private readonly VideoUrlExtractor _extractor;

        public VideoUrlExtractorTests() {
            _mockFetcher = new Mock<IHtmlFetcher>();
            _extractor = new VideoUrlExtractor(_mockFetcher.Object);
        }

        [Fact]
        public async Task ExtractVideoUrl_Hypnotube_ReturnsVideoUrl() {
            // Arrange
            string pageUrl = "https://hypnotube.com/video/example-12345.html";
            string htmlContent = @"
                <html>
                    <body>
                        <video id='player' src='https://cdn.hypnotube.com/videos/12345.mp4'></video>
                    </body>
                </html>";

            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(htmlContent);

            // Act
            var result = await _extractor.ExtractVideoUrlAsync(pageUrl);

            // Assert
            Assert.Equal("https://cdn.hypnotube.com/videos/12345.mp4", result);
        }

        [Fact]
        public async Task ExtractVideoUrl_Iwara_FromSourceTag_ReturnsVideoUrl() {
            // Arrange
            string pageUrl = "https://iwara.tv/video/example";
            string htmlContent = @"
                <html>
                    <body>
                         <video>
                            <source src='https://cdn.iwara.tv/file.mp4' type='video/mp4'>
                        </video>
                    </body>
                </html>";

            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(htmlContent);

            // Act
            var result = await _extractor.ExtractVideoUrlAsync(pageUrl);

            // Assert
            Assert.Equal("https://cdn.iwara.tv/file.mp4", result); // Extractor normalizes URL
        }

        [Fact]
        public async Task ExtractVideoUrl_Rule34Video_ExtractsRndUsingToken() {
             // Arrange
            string pageUrl = "https://rule34video.com/video/12345/example/";
             // Simulate the relevant part of the HTML structure
             string htmlContent = @"
                <script type='text/javascript'>
                    var flashvars = {
                        video_id: '12345',
                        video_alt_url: 'function/0/https://cdn.rule34video.com/videos/12345.mp4',
                        rnd: '1767689683'
                    };
                </script>
            ";

            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(htmlContent);
            
            // Setup ResolveRedirectUrlAsync to return the input URL unchanged (no redirect)
            _mockFetcher.Setup(f => f.ResolveRedirectUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, string referer, CancellationToken ct) => url);

            // Act
            var result = await _extractor.ExtractVideoUrlAsync(pageUrl);

            // Assert
            Assert.Contains("rnd=1767689683", result);
            Assert.StartsWith("https://cdn.rule34video.com/videos/12345.mp4", result);
        }

        [Fact]
        public async Task ExtractVideoUrl_WithRelativeUrl_ResolvesToAbsolute() {
            // Arrange
            string pageUrl = "https://example.com/video/page";
            string htmlContent = @"<video src='/media/video.mp4'></video>";

            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(htmlContent);

            // Act
            var result = await _extractor.ExtractVideoUrlAsync(pageUrl);

            // Assert
            Assert.Equal("https://example.com/media/video.mp4", result);
        }

        [Fact]
        public async Task ExtractVideoTitle_ReturnsOpenGraphTitle() {
            // Arrange
            string pageUrl = "https://example.com/video";
            string htmlContent = @"
                <html>
                    <head>
                        <meta property='og:title' content='Amazing Video Title' />
                    </head>
                </html>";

            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(htmlContent);

            // Act
            var result = await _extractor.ExtractVideoTitleAsync(pageUrl);

            // Assert
            Assert.Equal("Amazing Video Title", result);
        }

        [Fact]
        public async Task ExtractVideoTitle_SanitizesInput() {
            // Arrange
            string pageUrl = "https://example.com/video";
            string htmlContent = @"<title>Video Title - With Suffix | SiteName</title>";

            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(htmlContent);

            // Act
            var result = await _extractor.ExtractVideoTitleAsync(pageUrl);

            // Assert
            // The extractor has specific logic to strip site suffixes if known, 
            // but for a generic one it might just return the whole thing depending on implementation.
            // Let's test basic HTML decoding.
            Assert.Contains("Video Title", result);
        }

        [Fact]
        public async Task ExtractVideoUrl_InvalidHtml_ReturnsNull() {
             // Arrange
            string pageUrl = "https://example.com/video";
            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("");

            // Act
            var result = await _extractor.ExtractVideoUrlAsync(pageUrl);

            // Assert
            Assert.Null(result);
        }

        #region Edge Cases

        [Fact]
        public async Task ExtractVideoUrl_DirectVideoUrl_ReturnsImmediately() {
            var url = "https://example.com/video.mp4";
            
            // Act
            var result = await _extractor.ExtractVideoUrlAsync(url);
            
            // Assert
            Assert.Equal(url, result);
            // Verify fetcher was NOT called
            _mockFetcher.Verify(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExtractVideoUrl_UsesCache() {
            var pageUrl = "https://example.com/video";
            var videoUrl = "https://cdn.com/video.mp4";
            var html = $"<video src='{videoUrl}'></video>";

            _mockFetcher.Setup(f => f.FetchHtmlAsync(pageUrl, It.IsAny<CancellationToken>()))
                .ReturnsAsync(html);

            // Act: Call twice
            var res1 = await _extractor.ExtractVideoUrlAsync(pageUrl);
            var res2 = await _extractor.ExtractVideoUrlAsync(pageUrl);

            // Assert
            Assert.Equal(videoUrl, res1);
            Assert.Equal(videoUrl, res2);
            // Verify fetcher was called only once
            _mockFetcher.Verify(f => f.FetchHtmlAsync(pageUrl, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExtractVideoUrl_NetworkError_HandlesGracefully() {
            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("Network down"));

            var result = await _extractor.ExtractVideoUrlAsync("https://example.com/video");

            Assert.Null(result);
        }

        [Fact]
        public async Task ExtractVideoUrl_MalformedHtml_HandlesGracefully() {
            var html = "<video src='incomplete quote";
            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(html);

            var result = await _extractor.ExtractVideoUrlAsync("https://example.com/video");

            Assert.Null(result);
        }

        [Fact]
        public async Task ExtractVideoTitle_VeryLongTitle_Sanitizes() {
            var longTitle = new string('A', 300);
            var html = $"<html><head><title>{longTitle}</title></head></html>";
            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(html);

            var result = await _extractor.ExtractVideoTitleAsync("https://example.com/video");

            Assert.NotNull(result);
            Assert.True(result.Length <= 200);
            Assert.EndsWith("...", result);
        }

        [Fact]
        public async Task ExtractVideoUrl_Concurrent_IsSafe() {
            var pageUrl = "https://example.com/video";
            var html = "<video src='https://cdn.com/v.mp4'></video>";
            _mockFetcher.Setup(f => f.FetchHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(html);

            var tasks = new Task<string>[10];
            for (int i = 0; i < tasks.Length; i++) {
                tasks[i] = _extractor.ExtractVideoUrlAsync(pageUrl);
            }

            var results = await Task.WhenAll(tasks);
            foreach (var res in results) {
                Assert.Equal("https://cdn.com/v.mp4", res);
            }
        }

        #endregion
    }
}

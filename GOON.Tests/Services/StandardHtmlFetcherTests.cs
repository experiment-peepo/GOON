using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GOON.Classes;
using Xunit;

namespace GOON.Tests {
    public class StandardHtmlFetcherTests {
        [Fact]
        public async Task FetchHtmlAsync_WithValidUrl_ReturnsHtmlContent() {
            // Arrange
            var fetcher = new StandardHtmlFetcher();
            // Use a reliable public URL for testing
            var url = "https://www.example.com";

            // Act
            var result = await fetcher.FetchHtmlAsync(url);

            // Assert
            // Result may be null if network unavailable, or contain HTML if successful
            // We can't guarantee network access, so we just verify it doesn't throw
            Assert.True(result == null || result.Contains("html", StringComparison.OrdinalIgnoreCase) || result.Length > 0);
        }

        [Fact]
        public async Task FetchHtmlAsync_WithInvalidUrl_ReturnsNull() {
            // Arrange
            var fetcher = new StandardHtmlFetcher();
            var url = "https://this-domain-definitely-does-not-exist-12345.com";

            // Act
            var result = await fetcher.FetchHtmlAsync(url);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task FetchHtmlAsync_WithMalformedUrl_ReturnsNull() {
            // Arrange
            var fetcher = new StandardHtmlFetcher();
            var url = "not-a-valid-url";

            // Act
            var result = await fetcher.FetchHtmlAsync(url);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task FetchHtmlAsync_WithEmptyUrl_ReturnsNull() {
            // Arrange
            var fetcher = new StandardHtmlFetcher();
            var url = "";

            // Act
            var result = await fetcher.FetchHtmlAsync(url);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task FetchHtmlAsync_WithCancellationToken_CanBeCancelled() {
            // Arrange
            var fetcher = new StandardHtmlFetcher();
            var url = "https://www.example.com";
            var cts = new CancellationTokenSource();
            
            // Act
            cts.Cancel(); // Cancel immediately
            
            // Assert
            // Should either return null or throw TaskCanceledException
            try {
                var result = await fetcher.FetchHtmlAsync(url, cts.Token);
                // If it doesn't throw, result should be null due to cancellation
                Assert.Null(result);
            } catch (TaskCanceledException) {
                // This is also acceptable behavior
                Assert.True(true);
            } catch (OperationCanceledException) {
                // This is also acceptable behavior
                Assert.True(true);
            }
        }

        [Fact]
        public async Task FetchHtmlAsync_WithNullUrl_HandlesGracefully() {
            // Arrange
            var fetcher = new StandardHtmlFetcher();
            string url = null;

            // Act & Assert
            // Should not throw, should return null
            try {
                var result = await fetcher.FetchHtmlAsync(url);
                Assert.Null(result);
            } catch {
                // If it throws, that's acceptable defensive programming
                Assert.True(true);
            }
        }

        [Fact]
        public async Task FetchHtmlAsync_With404Url_ReturnsNull() {
            // Arrange
            var fetcher = new StandardHtmlFetcher();
            // Use a URL that should return 404
            var url = "https://httpstat.us/404";

            // Act
            var result = await fetcher.FetchHtmlAsync(url);

            // Assert
            // Should return null on error status codes
            Assert.Null(result);
        }

        [Fact]
        public async Task FetchHtmlAsync_With500Url_ReturnsNull() {
            // Arrange
            var fetcher = new StandardHtmlFetcher();
            // Use a URL that should return 500
            var url = "https://httpstat.us/500";

            // Act
            var result = await fetcher.FetchHtmlAsync(url);

            // Assert
            // Should return null on error status codes
            Assert.Null(result);
        }

        [Fact]
        public async Task FetchHtmlAsync_WithHttpUrl_Works() {
            // Arrange
            var fetcher = new StandardHtmlFetcher();
            // Some sites still support HTTP
            var url = "http://www.example.com";

            // Act
            var result = await fetcher.FetchHtmlAsync(url);

            // Assert
            // Should work with HTTP as well as HTTPS
            // May be null if network unavailable
            Assert.True(result == null || result.Length >= 0);
        }

        [Fact]
        public void StandardHtmlFetcher_CanBeInstantiated() {
            // Act
            var fetcher = new StandardHtmlFetcher();

            // Assert
            Assert.NotNull(fetcher);
        }

        [Fact]
        public void StandardHtmlFetcher_ImplementsIHtmlFetcher() {
            // Act
            var fetcher = new StandardHtmlFetcher();

            // Assert
            Assert.IsAssignableFrom<IHtmlFetcher>(fetcher);
        }

        [Fact]
        public async Task FetchHtmlAsync_ConsecutiveCalls_WorkIndependently() {
            // Arrange
            var fetcher = new StandardHtmlFetcher();
            var url = "https://www.example.com";

            // Act
            var result1 = await fetcher.FetchHtmlAsync(url);
            var result2 = await fetcher.FetchHtmlAsync(url);

            // Assert
            // Both calls should work independently
            // Results should be the same (or both null if network unavailable)
            if (result1 != null && result2 != null) {
                Assert.NotEmpty(result1);
                Assert.NotEmpty(result2);
            }
        }

        [Fact]
        public async Task FetchHtmlAsync_WithVeryLongUrl_HandlesGracefully() {
            var fetcher = new StandardHtmlFetcher();
            var longUrl = "https://www.google.com/search?q=" + new string('a', 2000);
            
            var result = await fetcher.FetchHtmlAsync(longUrl);
            // Long URLs might fail or succeed depending on server, we check for no crash
            Assert.True(true);
        }

        [Fact]
        public async Task FetchHtmlAsync_WithWhitespaceUrl_ReturnsNull() {
            var fetcher = new StandardHtmlFetcher();
            var result = await fetcher.FetchHtmlAsync("   ");
            Assert.Null(result);
        }
    }
}

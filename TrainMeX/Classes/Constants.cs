using System;

namespace TrainMeX.Classes {
    /// <summary>
    /// Centralized constants for the application
    /// </summary>
    public static class Constants {
        /// <summary>
        /// Supported video file extensions
        /// </summary>
        public static readonly string[] VideoExtensions = { 
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm",  // Modern formats
            ".mpg", ".mpeg",  // MPEG-1/2 - DVD/broadcast content
            ".ts", ".m2ts"    // MPEG transport streams - Blu-ray content
        };

        /// <summary>
        /// Supported video URL domains
        /// </summary>
        public static readonly string[] SupportedVideoDomains = {
            "rule34video.com",
            "pmvhaven.com",
            "iwara.tv",
            "hypnotube.com"
        };

        /// <summary>
        /// Maximum number of entries in the file existence cache
        /// </summary>
        public const int MaxFileCacheSize = 1000;

        /// <summary>
        /// Time to live for cache entries in minutes
        /// </summary>
        public const int CacheTtlMinutes = 5;

        /// <summary>
        /// Maximum number of retries for file operations
        /// </summary>
        public const int MaxRetryAttempts = 3;

        /// <summary>
        /// Base delay in milliseconds for retry operations
        /// </summary>
        public const int RetryBaseDelayMs = 100;

        /// <summary>
        /// Maximum file size in bytes (5GB)
        /// </summary>
        public const long MaxFileSizeBytes = 5L * 1024 * 1024 * 1024;

        /// <summary>
        /// Warning threshold for file size in bytes (1GB)
        /// </summary>
        public const long FileSizeWarningThreshold = 1024L * 1024 * 1024;

        /// <summary>
        /// HTTP request timeout in seconds
        /// </summary>
        public const int HttpRequestTimeoutSeconds = 30;

        /// <summary>
        /// URL extraction timeout in seconds
        /// </summary>
        public const int UrlExtractionTimeoutSeconds = 30;

        /// <summary>
        /// URL cache TTL in minutes
        /// </summary>
        public const int UrlCacheTtlMinutes = 60;
    }
}



using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GOON.Classes {
    public interface IVideoDownloadService {
        string GetCachePath(string url);
        bool IsCached(string url);
        string GetCachedFilePath(string url);
        Task<string> DownloadVideoAsync(string url, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default);
        Task<string> DownloadPartialAsync(string url, long maxBytes = 150 * 1024 * 1024, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default);
        void CleanupOldFiles(int daysOld = 10);
        long GetCacheSize();
    }
}

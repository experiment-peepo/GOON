using System.Threading;
using System.Threading.Tasks;

namespace GOON.Classes {
    public interface IVideoUrlExtractor {
        Task<VideoMetadata> ExtractVideoMetadataAsync(string pageUrl, CancellationToken cancellationToken = default);
        Task<string> ExtractVideoUrlAsync(string pageUrl, CancellationToken cancellationToken = default);
        Task<string> ExtractVideoTitleAsync(string pageUrl, CancellationToken cancellationToken = default);
        void ClearCache();
    }
}

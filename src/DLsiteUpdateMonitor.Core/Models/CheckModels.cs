using DLsiteUpdateMonitor.Core.Http;

namespace DLsiteUpdateMonitor.Core.Models
{
    public enum ProductCheckFailureScope
    {
        None = 0,
        LocalRecord = 10,
        RemoteProduct = 20
    }

    public sealed class ProductCheckResult
    {
        public string ProductId { get; set; }
        public bool FromCache { get; set; }
        // True only when a validated remote snapshot exists and may safely be reused for
        // another Playnite game that points at the same DLsite product.
        public bool HasReusableSnapshot { get; set; }
        public ProductCheckFailureScope FailureScope { get; set; }
        public CheckHealth Health { get; set; }
        public ComparisonResult Comparison { get; set; }
        public DlsiteFetchResult Fetch { get; set; }
        public DlsiteParseResult Parse { get; set; }
        public string Message { get; set; }
    }
}

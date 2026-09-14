using System.Collections.Generic;

namespace DLsiteUpdateMonitor.Core.Models
{
    public sealed class DlsiteTarget
    {
        public string ProductId { get; set; }
        public string RegisteredUrl { get; set; }
    }

    public sealed class LinkResolutionResult
    {
        public LinkResolutionStatus Status { get; set; }
        public DlsiteTarget Target { get; set; }
        public IReadOnlyList<DlsiteTarget> Candidates { get; set; }
        public string Reason { get; set; }
    }
}

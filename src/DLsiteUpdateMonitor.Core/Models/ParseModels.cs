using System.Collections.Generic;

namespace DLsiteUpdateMonitor.Core.Models
{
    public enum ParseHealth
    {
        Healthy = 10,
        Degraded = 20,
        ProductUnavailable = 25,
        Error = 30
    }

    public sealed class DlsiteParseResult
    {
        public ParseHealth Health { get; set; }
        public RemoteSnapshot Snapshot { get; set; }
        public IReadOnlyList<string> Diagnostics { get; set; }
    }
}

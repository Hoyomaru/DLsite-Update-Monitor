using System;

namespace DLsiteUpdateMonitor.Core.Models
{
    public sealed class RemoteSnapshot
    {
        public int SnapshotSchemaVersion { get; set; } = 1;
        public int ParserVersion { get; set; } = 1;
        public string ProductId { get; set; }
        public string SourceUrl { get; set; }
        public string ResolvedUrl { get; set; }
        public string WorkName { get; set; }
        public DateTimeOffset FetchedAtUtc { get; set; }
        public ObservedField<string> UpdateInfo { get; set; }
        public ObservedField<long> FileSize { get; set; }
        public string Fingerprint { get; set; }
    }
}

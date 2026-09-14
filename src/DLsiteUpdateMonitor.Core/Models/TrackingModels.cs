using System;
using System.Collections.Generic;

namespace DLsiteUpdateMonitor.Core.Models
{
    public sealed class TrackingDatabase
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset LastSavedAtUtc { get; set; }
        public Dictionary<Guid, GameTrackingRecord> Games { get; set; } = new Dictionary<Guid, GameTrackingRecord>();
    }

    public sealed class GameTrackingRecord
    {
        public Guid PlayniteGameId { get; set; }
        public string RegisteredUrl { get; set; }
        public string RequestedProductId { get; set; }
        public string ResolvedUrl { get; set; }
        public string ResolvedProductId { get; set; }
        public MonitoringState MonitoringState { get; set; } = MonitoringState.Uninitialized;
        public CheckHealth LastCheckHealth { get; set; } = CheckHealth.NeverChecked;
        public RemoteSnapshot AcknowledgedSnapshot { get; set; }
        public RemoteSnapshot CurrentSnapshot { get; set; }
        public RemoteSnapshot LastObservation { get; set; }
        public DateTimeOffset? FirstCheckedAtUtc { get; set; }
        public DateTimeOffset? LastAttemptAtUtc { get; set; }
        public DateTimeOffset? LastSuccessfulCheckAtUtc { get; set; }
        public CheckError LastError { get; set; }
        public List<TrackingHistoryEntry> History { get; set; } = new List<TrackingHistoryEntry>();
    }

    public sealed class CheckError
    {
        public CheckHealth Type { get; set; }
        public DateTimeOffset OccurredAtUtc { get; set; }
        public int? HttpStatusCode { get; set; }
        public string Message { get; set; }
        public string Url { get; set; }
    }

    public sealed class TrackingHistoryEntry
    {
        public DateTimeOffset TimestampUtc { get; set; }
        public HistoryEventType EventType { get; set; }
        public string OldFingerprint { get; set; }
        public string NewFingerprint { get; set; }
        public ChangeFlags Changes { get; set; }
        public string Note { get; set; }
    }
}

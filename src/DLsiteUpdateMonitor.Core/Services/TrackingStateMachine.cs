using System;
using System.Linq;
using DLsiteUpdateMonitor.Core.Models;

namespace DLsiteUpdateMonitor.Core.Services
{
    public sealed class TrackingStateMachine
    {
        private readonly SnapshotComparer comparer;
        private readonly int historyLimit;

        public TrackingStateMachine(SnapshotComparer comparer = null, int historyLimit = 50)
        {
            if (historyLimit < 1) throw new ArgumentOutOfRangeException(nameof(historyLimit));
            this.comparer = comparer ?? new SnapshotComparer();
            this.historyLimit = historyLimit;
        }

        public ComparisonResult ApplySuccessfulSnapshot(GameTrackingRecord record, RemoteSnapshot candidate, DateTimeOffset nowUtc)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));

            record.LastAttemptAtUtc = nowUtc;
            record.LastObservation = SnapshotCloner.Clone(candidate);

            var result = comparer.Compare(record.AcknowledgedSnapshot, candidate);
            if (result.Outcome == ComparisonOutcome.Indeterminate)
            {
                record.LastCheckHealth = CheckHealth.ParseDegraded;
                record.LastError = new CheckError
                {
                    Type = CheckHealth.ParseDegraded,
                    OccurredAtUtc = nowUtc,
                    Message = result.Reason,
                    Url = candidate.ResolvedUrl ?? candidate.SourceUrl
                };
                return result;
            }

            if (result.Outcome == ComparisonOutcome.IdentityMismatch)
            {
                record.LastCheckHealth = CheckHealth.RedirectedToDifferentProduct;
                record.LastError = new CheckError
                {
                    Type = CheckHealth.RedirectedToDifferentProduct,
                    OccurredAtUtc = nowUtc,
                    Message = result.Reason,
                    Url = candidate.ResolvedUrl ?? candidate.SourceUrl
                };
                return result;
            }

            var previousState = record.MonitoringState;
            var previousCurrent = record.CurrentSnapshot;

            record.LastCheckHealth = CheckHealth.Healthy;
            record.LastError = null;
            var observedAt = candidate.FetchedAtUtc == default(DateTimeOffset) ? nowUtc : candidate.FetchedAtUtc;
            record.LastSuccessfulCheckAtUtc = observedAt;
            if (!record.FirstCheckedAtUtc.HasValue) record.FirstCheckedAtUtc = observedAt;

            if (result.Outcome == ComparisonOutcome.BaselineCreated)
            {
                record.AcknowledgedSnapshot = SnapshotCloner.Clone(candidate);
                record.CurrentSnapshot = SnapshotCloner.Clone(candidate);
                record.MonitoringState = MonitoringState.Clean;
                AddHistory(record, new TrackingHistoryEntry
                {
                    TimestampUtc = nowUtc,
                    EventType = HistoryEventType.BaselineCreated,
                    NewFingerprint = candidate.Fingerprint,
                    Changes = ChangeFlags.None
                });
                return result;
            }

            record.CurrentSnapshot = SnapshotCloner.Clone(candidate);
            if (result.TargetState.HasValue)
            {
                record.MonitoringState = result.TargetState.Value;
            }

            if (result.Outcome == ComparisonOutcome.NoChange)
            {
                if (previousState != MonitoringState.Clean && previousState != MonitoringState.Uninitialized)
                {
                    AddHistory(record, new TrackingHistoryEntry
                    {
                        TimestampUtc = nowUtc,
                        EventType = HistoryEventType.ChangeReturnedToBaseline,
                        OldFingerprint = previousCurrent?.Fingerprint,
                        NewFingerprint = candidate.Fingerprint,
                        Changes = ChangeFlags.None
                    });
                }
                return result;
            }

            if (result.Outcome == ComparisonOutcome.Changed)
            {
                var currentChangedAgain = previousCurrent != null
                    && !string.Equals(previousCurrent.Fingerprint, candidate.Fingerprint, StringComparison.Ordinal);
                var eventType = previousState == MonitoringState.Clean || previousState == MonitoringState.Uninitialized
                    ? HistoryEventType.ChangeDetected
                    : currentChangedAgain
                        ? HistoryEventType.ChangeUpdated
                        : (HistoryEventType?)null;

                if (eventType.HasValue)
                {
                    AddHistory(record, new TrackingHistoryEntry
                    {
                        TimestampUtc = nowUtc,
                        EventType = eventType.Value,
                        OldFingerprint = previousCurrent?.Fingerprint ?? record.AcknowledgedSnapshot?.Fingerprint,
                        NewFingerprint = candidate.Fingerprint,
                        Changes = result.Changes
                    });
                }
            }

            return result;
        }

        public void RecordCheckFailure(GameTrackingRecord record, CheckHealth health, CheckError error, DateTimeOffset nowUtc)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (health == CheckHealth.Healthy || health == CheckHealth.NeverChecked)
                throw new ArgumentException("Failure health must describe an error or cancellation.", nameof(health));

            record.LastAttemptAtUtc = nowUtc;
            record.LastCheckHealth = health;
            record.LastError = error;
            // Intentionally do not touch MonitoringState, AcknowledgedSnapshot, CurrentSnapshot or LastSuccessfulCheckAtUtc.
        }

        public bool AcknowledgeCurrent(GameTrackingRecord record, bool ignored, DateTimeOffset nowUtc)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.CurrentSnapshot == null) return false;
            if (record.MonitoringState != MonitoringState.PendingUpdateInfo
                && record.MonitoringState != MonitoringState.PendingFileChange
                && record.MonitoringState != MonitoringState.PendingUpdateAndFileChange)
            {
                return false;
            }

            var old = record.AcknowledgedSnapshot;
            record.AcknowledgedSnapshot = SnapshotCloner.Clone(record.CurrentSnapshot);
            record.MonitoringState = MonitoringState.Clean;

            AddHistory(record, new TrackingHistoryEntry
            {
                TimestampUtc = nowUtc,
                EventType = ignored ? HistoryEventType.Ignored : HistoryEventType.Applied,
                OldFingerprint = old?.Fingerprint,
                NewFingerprint = record.CurrentSnapshot.Fingerprint,
                Changes = ChangeFlags.None
            });
            return true;
        }

        public void ResetMonitoring(GameTrackingRecord record, DateTimeOffset nowUtc)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var oldFingerprint = record.AcknowledgedSnapshot?.Fingerprint;

            record.AcknowledgedSnapshot = null;
            record.CurrentSnapshot = null;
            record.LastObservation = null;
            record.RegisteredUrl = null;
            record.RequestedProductId = null;
            record.ResolvedUrl = null;
            record.ResolvedProductId = null;
            record.MonitoringState = MonitoringState.Uninitialized;
            record.LastCheckHealth = CheckHealth.NeverChecked;
            record.LastError = null;
            record.FirstCheckedAtUtc = null;
            record.LastAttemptAtUtc = null;
            record.LastSuccessfulCheckAtUtc = null;

            AddHistory(record, new TrackingHistoryEntry
            {
                TimestampUtc = nowUtc,
                EventType = HistoryEventType.MonitoringReset,
                OldFingerprint = oldFingerprint,
                Changes = ChangeFlags.None
            });
        }

        private void AddHistory(GameTrackingRecord record, TrackingHistoryEntry entry)
        {
            if (record.History == null) record.History = new System.Collections.Generic.List<TrackingHistoryEntry>();
            record.History.Add(entry);
            if (record.History.Count > historyLimit)
            {
                var remove = record.History.Count - historyLimit;
                record.History.RemoveRange(0, remove);
            }
        }
    }
}

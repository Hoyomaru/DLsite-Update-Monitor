using System;
using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Services;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class TrackingStateMachineTests
    {
        private readonly DateTimeOffset t0 = DateTimeOffset.Parse("2026-09-14T10:00:00Z");

        [Fact]
        public void FirstSuccessfulSnapshot_CreatesBaseline()
        {
            var record = NewRecord();
            var machine = new TrackingStateMachine();
            var snapshot = WithFingerprint(TestSnapshots.Make());

            var result = machine.ApplySuccessfulSnapshot(record, snapshot, t0);

            Assert.Equal(ComparisonOutcome.BaselineCreated, result.Outcome);
            Assert.Equal(MonitoringState.Clean, record.MonitoringState);
            Assert.NotNull(record.AcknowledgedSnapshot);
            Assert.NotNull(record.CurrentSnapshot);
            Assert.Single(record.History);
            Assert.Equal(HistoryEventType.BaselineCreated, record.History[0].EventType);
        }

        [Fact]
        public void NetworkFailure_DoesNotErasePendingUpdate()
        {
            var record = NewRecord();
            var machine = new TrackingStateMachine();
            machine.ApplySuccessfulSnapshot(record, WithFingerprint(TestSnapshots.Make(size: 850)), t0);
            machine.ApplySuccessfulSnapshot(record, WithFingerprint(TestSnapshots.Make(size: 851)), t0.AddMinutes(1));
            Assert.Equal(MonitoringState.PendingFileChange, record.MonitoringState);
            var current = record.CurrentSnapshot.Fingerprint;
            var acknowledged = record.AcknowledgedSnapshot.Fingerprint;

            machine.RecordCheckFailure(record, CheckHealth.RateLimited, new CheckError
            {
                Type = CheckHealth.RateLimited,
                OccurredAtUtc = t0.AddMinutes(2),
                HttpStatusCode = 429,
                Message = "Too Many Requests"
            }, t0.AddMinutes(2));

            Assert.Equal(MonitoringState.PendingFileChange, record.MonitoringState);
            Assert.Equal(current, record.CurrentSnapshot.Fingerprint);
            Assert.Equal(acknowledged, record.AcknowledgedSnapshot.Fingerprint);
            Assert.Equal(CheckHealth.RateLimited, record.LastCheckHealth);
        }

        [Fact]
        public void IndeterminateSnapshot_DoesNotOverwriteCurrentOrAcknowledged()
        {
            var record = NewRecord();
            var machine = new TrackingStateMachine();
            machine.ApplySuccessfulSnapshot(record, WithFingerprint(TestSnapshots.Make()), t0);
            var ack = record.AcknowledgedSnapshot.Fingerprint;
            var current = record.CurrentSnapshot.Fingerprint;

            var bad = TestSnapshots.Make(sizeState: ObservationState.Missing);
            bad.Fingerprint = "bad";
            var result = machine.ApplySuccessfulSnapshot(record, bad, t0.AddMinutes(1));

            Assert.Equal(ComparisonOutcome.Indeterminate, result.Outcome);
            Assert.Equal(ack, record.AcknowledgedSnapshot.Fingerprint);
            Assert.Equal(current, record.CurrentSnapshot.Fingerprint);
            Assert.Equal(MonitoringState.Clean, record.MonitoringState);
            Assert.Equal(CheckHealth.ParseDegraded, record.LastCheckHealth);
        }

        [Theory]
        [InlineData(false, HistoryEventType.Applied)]
        [InlineData(true, HistoryEventType.Ignored)]
        public void AcknowledgeCurrent_ClearsPendingAndRecordsReason(bool ignored, HistoryEventType expectedEvent)
        {
            var record = NewRecord();
            var machine = new TrackingStateMachine();
            machine.ApplySuccessfulSnapshot(record, WithFingerprint(TestSnapshots.Make(size: 850)), t0);
            machine.ApplySuccessfulSnapshot(record, WithFingerprint(TestSnapshots.Make(size: 851)), t0.AddMinutes(1));

            Assert.True(machine.AcknowledgeCurrent(record, ignored, t0.AddMinutes(2)));
            Assert.Equal(MonitoringState.Clean, record.MonitoringState);
            Assert.Equal(record.CurrentSnapshot.Fingerprint, record.AcknowledgedSnapshot.Fingerprint);
            Assert.Equal(expectedEvent, record.History[record.History.Count - 1].EventType);
        }

        [Fact]
        public void AcknowledgeCurrent_WhenClean_IsNoOp()
        {
            var record = NewRecord();
            var machine = new TrackingStateMachine();
            machine.ApplySuccessfulSnapshot(record, WithFingerprint(TestSnapshots.Make(size: 850)), t0);
            var historyCount = record.History.Count;

            Assert.False(machine.AcknowledgeCurrent(record, false, t0.AddMinutes(1)));
            Assert.Equal(MonitoringState.Clean, record.MonitoringState);
            Assert.Equal(historyCount, record.History.Count);
        }

        [Fact]
        public void CandidateReturningToAcknowledgedState_ClearsPending()
        {
            var record = NewRecord();
            var machine = new TrackingStateMachine();
            var baseline = WithFingerprint(TestSnapshots.Make(size: 850));
            machine.ApplySuccessfulSnapshot(record, baseline, t0);
            machine.ApplySuccessfulSnapshot(record, WithFingerprint(TestSnapshots.Make(size: 851)), t0.AddMinutes(1));
            Assert.Equal(MonitoringState.PendingFileChange, record.MonitoringState);

            machine.ApplySuccessfulSnapshot(record, WithFingerprint(TestSnapshots.Make(size: 850)), t0.AddMinutes(2));

            Assert.Equal(MonitoringState.Clean, record.MonitoringState);
            Assert.Equal(HistoryEventType.ChangeReturnedToBaseline, record.History[record.History.Count - 1].EventType);
        }


        [Fact]
        public void ResetMonitoring_ClearsProductIdentityBookkeeping()
        {
            var record = NewRecord();
            record.RegisteredUrl = "https://www.dlsite.com/maniax/work/=/product_id/RJ11111111.html";
            record.RequestedProductId = "RJ11111111";
            record.ResolvedUrl = record.RegisteredUrl;
            record.ResolvedProductId = "RJ11111111";

            var machine = new TrackingStateMachine();
            machine.ResetMonitoring(record, t0);

            Assert.Null(record.RegisteredUrl);
            Assert.Null(record.RequestedProductId);
            Assert.Null(record.ResolvedUrl);
            Assert.Null(record.ResolvedProductId);
            Assert.Equal(MonitoringState.Uninitialized, record.MonitoringState);
        }

        private static GameTrackingRecord NewRecord()
        {
            return new GameTrackingRecord { PlayniteGameId = Guid.NewGuid() };
        }

        private static RemoteSnapshot WithFingerprint(RemoteSnapshot snapshot)
        {
            snapshot.Fingerprint = SnapshotFingerprint.Compute(snapshot);
            return snapshot;
        }
    }
}

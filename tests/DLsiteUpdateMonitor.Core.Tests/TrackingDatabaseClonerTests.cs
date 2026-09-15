using System;
using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Services;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class TrackingDatabaseClonerTests
    {
        [Fact]
        public void Clone_IsDeepEnoughForStagedMutations()
        {
            var id = Guid.NewGuid();
            var original = new TrackingDatabase
            {
                CreatedAtUtc = DateTimeOffset.Parse("2026-09-14T00:00:00Z"),
                LastSavedAtUtc = DateTimeOffset.Parse("2026-09-14T01:00:00Z")
            };
            original.Games[id] = new GameTrackingRecord
            {
                PlayniteGameId = id,
                MonitoringState = MonitoringState.PendingFileChange,
                AcknowledgedSnapshot = TestSnapshots.Make(size: 1000),
                CurrentSnapshot = TestSnapshots.Make(size: 1200),
                LastError = new CheckError
                {
                    Type = CheckHealth.Timeout,
                    OccurredAtUtc = DateTimeOffset.Parse("2026-09-14T02:00:00Z"),
                    Message = "timeout"
                }
            };
            original.Games[id].History.Add(new TrackingHistoryEntry
            {
                TimestampUtc = DateTimeOffset.Parse("2026-09-14T03:00:00Z"),
                EventType = HistoryEventType.ChangeDetected,
                Note = "original"
            });

            var clone = TrackingDatabaseCloner.Clone(original);
            clone.Games[id].MonitoringState = MonitoringState.Clean;
            clone.Games[id].CurrentSnapshot.ProductId = "RJ87654321";
            clone.Games[id].LastError.Message = "changed";
            clone.Games[id].History[0].Note = "changed";

            Assert.Equal(MonitoringState.PendingFileChange, original.Games[id].MonitoringState);
            Assert.Equal("RJ01234567", original.Games[id].CurrentSnapshot.ProductId);
            Assert.Equal("timeout", original.Games[id].LastError.Message);
            Assert.Equal("original", original.Games[id].History[0].Note);
        }
    }
}

using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Services;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class SnapshotComparerTests
    {
        private readonly SnapshotComparer comparer = new SnapshotComparer();

        [Fact]
        public void NoAcknowledgedSnapshot_CreatesBaseline()
        {
            var result = comparer.Compare(null, TestSnapshots.Make());
            Assert.Equal(ComparisonOutcome.BaselineCreated, result.Outcome);
            Assert.Equal(MonitoringState.Clean, result.TargetState);
        }

        [Fact]
        public void SameSignals_IsClean()
        {
            var result = comparer.Compare(TestSnapshots.Make(), TestSnapshots.Make());
            Assert.Equal(ComparisonOutcome.NoChange, result.Outcome);
            Assert.Equal(MonitoringState.Clean, result.TargetState);
        }

        [Fact]
        public void UpdateInfoChanged_IsPendingUpdateInfo()
        {
            var oldSnapshot = TestSnapshots.Make(updateNormalized: "2026-09-10");
            var newSnapshot = TestSnapshots.Make(updateNormalized: "2026-09-14");

            var result = comparer.Compare(oldSnapshot, newSnapshot);

            Assert.Equal(ComparisonOutcome.Changed, result.Outcome);
            Assert.Equal(ChangeFlags.UpdateInfo, result.Changes);
            Assert.Equal(MonitoringState.PendingUpdateInfo, result.TargetState);
        }

        [Fact]
        public void FileSizeChanged_IsPendingFileChange()
        {
            var oldSnapshot = TestSnapshots.Make(size: 850_000_000);
            var newSnapshot = TestSnapshots.Make(size: 851_000_000);

            var result = comparer.Compare(oldSnapshot, newSnapshot);

            Assert.Equal(ChangeFlags.FileSize, result.Changes);
            Assert.Equal(MonitoringState.PendingFileChange, result.TargetState);
        }

        [Fact]
        public void BothChanged_IsPendingBoth()
        {
            var oldSnapshot = TestSnapshots.Make(updateNormalized: "2026-09-10", size: 850_000_000);
            var newSnapshot = TestSnapshots.Make(updateNormalized: "2026-09-14", size: 851_000_000);

            var result = comparer.Compare(oldSnapshot, newSnapshot);

            Assert.Equal(ChangeFlags.UpdateInfo | ChangeFlags.FileSize, result.Changes);
            Assert.Equal(MonitoringState.PendingUpdateAndFileChange, result.TargetState);
        }

        [Fact]
        public void UpdateInfoMissingToPresent_IsChange()
        {
            var oldSnapshot = TestSnapshots.Make(updateState: ObservationState.Missing);
            var newSnapshot = TestSnapshots.Make(updateState: ObservationState.Parsed, updateNormalized: "2026-09-14");

            var result = comparer.Compare(oldSnapshot, newSnapshot);

            Assert.Equal(ComparisonOutcome.Changed, result.Outcome);
            Assert.True((result.Changes & ChangeFlags.UpdateInfo) != 0);
        }

        [Fact]
        public void UpdateInfoPresentToMissing_IsIndeterminate()
        {
            var oldSnapshot = TestSnapshots.Make(updateState: ObservationState.Parsed);
            var newSnapshot = TestSnapshots.Make(updateState: ObservationState.Missing);

            var result = comparer.Compare(oldSnapshot, newSnapshot);

            Assert.Equal(ComparisonOutcome.Indeterminate, result.Outcome);
            Assert.Null(result.TargetState);
        }

        [Fact]
        public void CandidateFileSizeMissing_IsIndeterminate()
        {
            var result = comparer.Compare(
                TestSnapshots.Make(),
                TestSnapshots.Make(sizeState: ObservationState.Missing));

            Assert.Equal(ComparisonOutcome.Indeterminate, result.Outcome);
            Assert.Null(result.TargetState);
        }

        [Fact]
        public void CandidateUpdateInfoUnparsed_IsIndeterminate()
        {
            var result = comparer.Compare(
                TestSnapshots.Make(),
                TestSnapshots.Make(updateState: ObservationState.Unparsed));

            Assert.Equal(ComparisonOutcome.Indeterminate, result.Outcome);
        }

        [Fact]
        public void DifferentProductId_IsIdentityMismatch()
        {
            var result = comparer.Compare(
                TestSnapshots.Make(productId: "RJ01234567"),
                TestSnapshots.Make(productId: "RJ07654321"));

            Assert.Equal(ComparisonOutcome.IdentityMismatch, result.Outcome);
            Assert.Null(result.TargetState);
        }

        [Fact]
        public void ComparisonIsAgainstAcknowledgedSnapshot_NotPreviousCandidate()
        {
            var acknowledged = TestSnapshots.Make(size: 850_000_000);
            var candidateSeenYesterday = TestSnapshots.Make(size: 851_000_000);
            var candidateSeenToday = TestSnapshots.Make(size: 851_000_000);

            Assert.Equal(ComparisonOutcome.Changed, comparer.Compare(acknowledged, candidateSeenYesterday).Outcome);
            Assert.Equal(ComparisonOutcome.Changed, comparer.Compare(acknowledged, candidateSeenToday).Outcome);
        }

        [Fact]
        public void CandidateReturningToAcknowledgedState_IsClean()
        {
            var acknowledged = TestSnapshots.Make(size: 850_000_000);
            var returned = TestSnapshots.Make(size: 850_000_000);

            var result = comparer.Compare(acknowledged, returned);

            Assert.Equal(ComparisonOutcome.NoChange, result.Outcome);
            Assert.Equal(MonitoringState.Clean, result.TargetState);
        }
    }
}

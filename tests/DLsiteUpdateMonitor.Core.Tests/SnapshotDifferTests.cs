using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Services;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class SnapshotDifferTests
    {
        [Fact]
        public void Diff_ReportsConfirmedUpdateAndFileSizeChanges()
        {
            var before = TestSnapshots.Make(updateNormalized: "old", size: 100);
            var after = TestSnapshots.Make(updateNormalized: "new", size: 150);
            var differ = new SnapshotDiffer();

            var diff = differ.Diff(before, after);

            Assert.True(diff.Available);
            Assert.True(diff.SameProduct);
            Assert.True(diff.UpdateInfo.Comparable);
            Assert.True(diff.UpdateInfo.Changed);
            Assert.True(diff.FileSize.Comparable);
            Assert.True(diff.FileSize.Changed);
            Assert.True(diff.HasConfirmedChanges);
        }

        [Fact]
        public void Diff_KnownUpdateRegressingToMissing_IsNotConfirmedChange()
        {
            var before = TestSnapshots.Make(updateNormalized: "known");
            var after = TestSnapshots.Make(updateState: ObservationState.Missing);
            var differ = new SnapshotDiffer();

            var diff = differ.Diff(before, after);

            Assert.True(diff.Available);
            Assert.False(diff.UpdateInfo.Comparable);
            Assert.False(diff.UpdateInfo.Changed);
        }

        [Fact]
        public void Diff_DifferentProducts_IsUnavailable()
        {
            var before = TestSnapshots.Make(productId: "RJ01234567");
            var after = TestSnapshots.Make(productId: "RJ07654321");
            var differ = new SnapshotDiffer();

            var diff = differ.Diff(before, after);

            Assert.False(diff.Available);
            Assert.False(diff.SameProduct);
            Assert.False(diff.HasConfirmedChanges);
        }
    }
}

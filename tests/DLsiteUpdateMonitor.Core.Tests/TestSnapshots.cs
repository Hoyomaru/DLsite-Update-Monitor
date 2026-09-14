using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Services;

namespace DLsiteUpdateMonitor.Core.Tests
{
    internal static class TestSnapshots
    {
        public static RemoteSnapshot Make(
            string productId = "RJ01234567",
            ObservationState updateState = ObservationState.Parsed,
            string updateNormalized = "2026-09-10",
            ObservationState sizeState = ObservationState.Parsed,
            long size = 850_000_000)
        {
            var update = updateState == ObservationState.Parsed
                ? ObservedField<string>.Parsed(updateNormalized, updateNormalized, updateNormalized)
                : updateState == ObservationState.Missing
                    ? ObservedField<string>.Missing()
                    : ObservedField<string>.Unparsed("???");

            var fileSize = sizeState == ObservationState.Parsed
                ? ObservedField<long>.Parsed(size.ToString(), size.ToString(), size)
                : sizeState == ObservationState.Missing
                    ? ObservedField<long>.Missing()
                    : ObservedField<long>.Unparsed("???");

            var snapshot = new RemoteSnapshot
            {
                ProductId = productId,
                UpdateInfo = update,
                FileSize = fileSize
            };
            snapshot.Fingerprint = SnapshotFingerprint.Compute(snapshot);
            return snapshot;
        }
    }
}

using System;
using DLsiteUpdateMonitor.Core.Models;

namespace DLsiteUpdateMonitor.Core.Services
{
    public sealed class SnapshotDiffer
    {
        public SnapshotDiff Diff(RemoteSnapshot acknowledged, RemoteSnapshot current)
        {
            if (acknowledged == null || current == null)
            {
                return new SnapshotDiff
                {
                    Available = false,
                    SameProduct = false,
                    UpdateInfo = new SnapshotFieldDiff<string>
                    {
                        Before = acknowledged?.UpdateInfo,
                        After = current?.UpdateInfo
                    },
                    FileSize = new SnapshotFieldDiff<long>
                    {
                        Before = acknowledged?.FileSize,
                        After = current?.FileSize
                    }
                };
            }

            var sameProduct = !string.IsNullOrWhiteSpace(acknowledged.ProductId)
                && !string.IsNullOrWhiteSpace(current.ProductId)
                && string.Equals(acknowledged.ProductId, current.ProductId, StringComparison.OrdinalIgnoreCase);

            return new SnapshotDiff
            {
                Available = sameProduct,
                SameProduct = sameProduct,
                UpdateInfo = DiffUpdateInfo(acknowledged.UpdateInfo, current.UpdateInfo, sameProduct),
                FileSize = DiffFileSize(acknowledged.FileSize, current.FileSize, sameProduct)
            };
        }

        private static SnapshotFieldDiff<string> DiffUpdateInfo(
            ObservedField<string> before,
            ObservedField<string> after,
            bool sameProduct)
        {
            var result = new SnapshotFieldDiff<string> { Before = before, After = after };
            if (!sameProduct || before == null || after == null) return result;
            if (before.State == ObservationState.Unparsed || after.State == ObservationState.Unparsed) return result;

            if (before.State == ObservationState.Missing && after.State == ObservationState.Missing)
            {
                result.Comparable = true;
                return result;
            }

            if (before.State == ObservationState.Missing && after.State == ObservationState.Parsed)
            {
                result.Comparable = true;
                result.Changed = true;
                return result;
            }

            // Keep the diff view aligned with SnapshotComparer: a known value regressing to
            // missing is not a confirmed change and must remain "comparison unavailable".
            if (before.State == ObservationState.Parsed && after.State == ObservationState.Missing)
            {
                return result;
            }

            if (before.State == ObservationState.Parsed && after.State == ObservationState.Parsed)
            {
                result.Comparable = true;
                result.Changed = !string.Equals(before.Normalized, after.Normalized, StringComparison.Ordinal);
            }

            return result;
        }

        private static SnapshotFieldDiff<long> DiffFileSize(
            ObservedField<long> before,
            ObservedField<long> after,
            bool sameProduct)
        {
            var result = new SnapshotFieldDiff<long> { Before = before, After = after };
            if (!sameProduct || before == null || after == null) return result;
            if (before.State != ObservationState.Parsed || after.State != ObservationState.Parsed) return result;

            result.Comparable = true;
            result.Changed = before.Value != after.Value;
            return result;
        }
    }
}

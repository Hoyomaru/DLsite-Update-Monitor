using System;
using DLsiteUpdateMonitor.Core.Models;

namespace DLsiteUpdateMonitor.Core.Services
{
    public sealed class SnapshotValidationResult
    {
        public bool IsComparisonEligible { get; set; }
        public string Reason { get; set; }
    }

    public sealed class SnapshotValidator
    {
        public SnapshotValidationResult Validate(RemoteSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return Invalid("Snapshot is null.");
            }

            if (string.IsNullOrWhiteSpace(snapshot.ProductId))
            {
                return Invalid("Product ID is missing.");
            }

            if (snapshot.FileSize == null || snapshot.FileSize.State != ObservationState.Parsed)
            {
                return Invalid("File size is not parsed; comparison would be unsafe.");
            }

            if (snapshot.UpdateInfo == null)
            {
                return Invalid("UpdateInfo observation is missing from the snapshot model.");
            }

            if (snapshot.UpdateInfo.State == ObservationState.Unparsed)
            {
                return Invalid("Update information exists but could not be parsed safely.");
            }

            return new SnapshotValidationResult { IsComparisonEligible = true };
        }

        private static SnapshotValidationResult Invalid(string reason)
        {
            return new SnapshotValidationResult
            {
                IsComparisonEligible = false,
                Reason = reason
            };
        }
    }
}

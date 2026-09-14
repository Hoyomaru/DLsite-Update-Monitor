using System;
using DLsiteUpdateMonitor.Core.Models;

namespace DLsiteUpdateMonitor.Core.Services
{
    public sealed class SnapshotComparer
    {
        private readonly SnapshotValidator validator;

        public SnapshotComparer(SnapshotValidator validator = null)
        {
            this.validator = validator ?? new SnapshotValidator();
        }

        public ComparisonResult Compare(RemoteSnapshot acknowledged, RemoteSnapshot candidate)
        {
            var candidateValidation = validator.Validate(candidate);
            if (!candidateValidation.IsComparisonEligible)
            {
                return Indeterminate(candidateValidation.Reason);
            }

            if (acknowledged == null)
            {
                return ComparisonResult.Baseline();
            }

            var acknowledgedValidation = validator.Validate(acknowledged);
            if (!acknowledgedValidation.IsComparisonEligible)
            {
                return Indeterminate("Acknowledged snapshot is not comparison eligible: " + acknowledgedValidation.Reason);
            }

            if (!string.Equals(acknowledged.ProductId, candidate.ProductId, StringComparison.OrdinalIgnoreCase))
            {
                return new ComparisonResult
                {
                    Outcome = ComparisonOutcome.IdentityMismatch,
                    Changes = ChangeFlags.None,
                    TargetState = null,
                    Reason = "The candidate belongs to a different DLsite product ID."
                };
            }

            ChangeFlags changes = ChangeFlags.None;

            var updateInfoComparison = CompareUpdateInfo(acknowledged.UpdateInfo, candidate.UpdateInfo);
            if (updateInfoComparison == FieldComparison.Indeterminate)
            {
                return Indeterminate("Update information regressed from a known value to a missing/unusable value.");
            }
            if (updateInfoComparison == FieldComparison.Changed)
            {
                changes |= ChangeFlags.UpdateInfo;
            }

            var fileSizeComparison = CompareFileSize(acknowledged.FileSize, candidate.FileSize);
            if (fileSizeComparison == FieldComparison.Indeterminate)
            {
                return Indeterminate("File size could not be compared safely.");
            }
            if (fileSizeComparison == FieldComparison.Changed)
            {
                changes |= ChangeFlags.FileSize;
            }

            if (changes == ChangeFlags.None)
            {
                return new ComparisonResult
                {
                    Outcome = ComparisonOutcome.NoChange,
                    Changes = ChangeFlags.None,
                    TargetState = MonitoringState.Clean,
                    Reason = "Candidate matches the acknowledged DLsite state."
                };
            }

            return new ComparisonResult
            {
                Outcome = ComparisonOutcome.Changed,
                Changes = changes,
                TargetState = ToMonitoringState(changes),
                Reason = "One or more monitored DLsite distribution signals changed."
            };
        }

        private static FieldComparison CompareUpdateInfo(ObservedField<string> before, ObservedField<string> after)
        {
            if (before.State == ObservationState.Unparsed || after.State == ObservationState.Unparsed)
            {
                return FieldComparison.Indeterminate;
            }

            if (before.State == ObservationState.Missing && after.State == ObservationState.Missing)
            {
                return FieldComparison.Same;
            }

            if (before.State == ObservationState.Missing && after.State == ObservationState.Parsed)
            {
                return FieldComparison.Changed;
            }

            if (before.State == ObservationState.Parsed && after.State == ObservationState.Missing)
            {
                return FieldComparison.Indeterminate;
            }

            if (before.State == ObservationState.Parsed && after.State == ObservationState.Parsed)
            {
                return string.Equals(before.Normalized, after.Normalized, StringComparison.Ordinal)
                    ? FieldComparison.Same
                    : FieldComparison.Changed;
            }

            return FieldComparison.Indeterminate;
        }

        private static FieldComparison CompareFileSize(ObservedField<long> before, ObservedField<long> after)
        {
            if (before.State != ObservationState.Parsed || after.State != ObservationState.Parsed)
            {
                return FieldComparison.Indeterminate;
            }

            return before.Value == after.Value ? FieldComparison.Same : FieldComparison.Changed;
        }

        private static MonitoringState ToMonitoringState(ChangeFlags changes)
        {
            var update = (changes & ChangeFlags.UpdateInfo) != 0;
            var file = (changes & ChangeFlags.FileSize) != 0;

            if (update && file) return MonitoringState.PendingUpdateAndFileChange;
            if (update) return MonitoringState.PendingUpdateInfo;
            if (file) return MonitoringState.PendingFileChange;
            return MonitoringState.Clean;
        }

        private static ComparisonResult Indeterminate(string reason)
        {
            return new ComparisonResult
            {
                Outcome = ComparisonOutcome.Indeterminate,
                Changes = ChangeFlags.None,
                TargetState = null,
                Reason = reason
            };
        }

        private enum FieldComparison
        {
            Same,
            Changed,
            Indeterminate
        }
    }
}

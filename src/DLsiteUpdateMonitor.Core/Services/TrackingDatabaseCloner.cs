using System;
using System.Collections.Generic;
using DLsiteUpdateMonitor.Core.Models;

namespace DLsiteUpdateMonitor.Core.Services
{
    public static class TrackingDatabaseCloner
    {
        public static TrackingDatabase Clone(TrackingDatabase source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var clone = new TrackingDatabase
            {
                SchemaVersion = source.SchemaVersion,
                CreatedAtUtc = source.CreatedAtUtc,
                LastSavedAtUtc = source.LastSavedAtUtc,
                Games = new Dictionary<Guid, GameTrackingRecord>()
            };

            if (source.Games == null)
            {
                return clone;
            }

            foreach (var pair in source.Games)
            {
                clone.Games[pair.Key] = CloneRecord(pair.Value);
            }

            return clone;
        }

        private static GameTrackingRecord CloneRecord(GameTrackingRecord source)
        {
            if (source == null) return null;

            return new GameTrackingRecord
            {
                PlayniteGameId = source.PlayniteGameId,
                RegisteredUrl = source.RegisteredUrl,
                RequestedProductId = source.RequestedProductId,
                ResolvedUrl = source.ResolvedUrl,
                ResolvedProductId = source.ResolvedProductId,
                MonitoringState = source.MonitoringState,
                LastCheckHealth = source.LastCheckHealth,
                AcknowledgedSnapshot = SnapshotCloner.Clone(source.AcknowledgedSnapshot),
                CurrentSnapshot = SnapshotCloner.Clone(source.CurrentSnapshot),
                LastObservation = SnapshotCloner.Clone(source.LastObservation),
                FirstCheckedAtUtc = source.FirstCheckedAtUtc,
                LastAttemptAtUtc = source.LastAttemptAtUtc,
                LastSuccessfulCheckAtUtc = source.LastSuccessfulCheckAtUtc,
                LastError = CloneError(source.LastError),
                History = CloneHistory(source.History)
            };
        }

        private static CheckError CloneError(CheckError source)
        {
            if (source == null) return null;
            return new CheckError
            {
                Type = source.Type,
                OccurredAtUtc = source.OccurredAtUtc,
                HttpStatusCode = source.HttpStatusCode,
                Message = source.Message,
                Url = source.Url
            };
        }

        private static List<TrackingHistoryEntry> CloneHistory(List<TrackingHistoryEntry> source)
        {
            var result = new List<TrackingHistoryEntry>();
            if (source == null) return result;

            foreach (var entry in source)
            {
                if (entry == null)
                {
                    result.Add(null);
                    continue;
                }

                result.Add(new TrackingHistoryEntry
                {
                    TimestampUtc = entry.TimestampUtc,
                    EventType = entry.EventType,
                    OldFingerprint = entry.OldFingerprint,
                    NewFingerprint = entry.NewFingerprint,
                    Changes = entry.Changes,
                    Note = entry.Note
                });
            }

            return result;
        }
    }
}

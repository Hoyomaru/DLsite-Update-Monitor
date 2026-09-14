using DLsiteUpdateMonitor.Core.Models;

namespace DLsiteUpdateMonitor.Core.Services
{
    public static class SnapshotCloner
    {
        public static RemoteSnapshot Clone(RemoteSnapshot source)
        {
            if (source == null) return null;
            return new RemoteSnapshot
            {
                SnapshotSchemaVersion = source.SnapshotSchemaVersion,
                ParserVersion = source.ParserVersion,
                ProductId = source.ProductId,
                SourceUrl = source.SourceUrl,
                ResolvedUrl = source.ResolvedUrl,
                WorkName = source.WorkName,
                FetchedAtUtc = source.FetchedAtUtc,
                UpdateInfo = CloneField(source.UpdateInfo),
                FileSize = CloneField(source.FileSize),
                Fingerprint = source.Fingerprint
            };
        }

        private static ObservedField<T> CloneField<T>(ObservedField<T> source)
        {
            if (source == null) return null;
            return new ObservedField<T>
            {
                State = source.State,
                Raw = source.Raw,
                Normalized = source.Normalized,
                Value = source.Value
            };
        }
    }
}

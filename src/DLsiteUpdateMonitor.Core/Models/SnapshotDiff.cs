namespace DLsiteUpdateMonitor.Core.Models
{
    public sealed class SnapshotDiff
    {
        public bool Available { get; set; }
        public bool SameProduct { get; set; }
        public SnapshotFieldDiff<string> UpdateInfo { get; set; }
        public SnapshotFieldDiff<long> FileSize { get; set; }
        public bool HasConfirmedChanges =>
            (UpdateInfo != null && UpdateInfo.Comparable && UpdateInfo.Changed)
            || (FileSize != null && FileSize.Comparable && FileSize.Changed);
    }

    public sealed class SnapshotFieldDiff<T>
    {
        public ObservedField<T> Before { get; set; }
        public ObservedField<T> After { get; set; }
        public bool Comparable { get; set; }
        public bool Changed { get; set; }
    }
}

using System;
using System.Collections.Generic;
using DLsiteUpdateMonitor.Core.Models;

namespace DLsiteUpdateMonitor.Core.Services
{
    public sealed class SnapshotCache
    {
        private sealed class Entry
        {
            public RemoteSnapshot Snapshot { get; set; }
            public DateTimeOffset StoredAtUtc { get; set; }
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly object sync = new object();
        private readonly TimeSpan ttl;

        public SnapshotCache(TimeSpan ttl)
        {
            if (ttl < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
            this.ttl = ttl;
        }

        public bool TryGet(string productId, DateTimeOffset nowUtc, out RemoteSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(productId)) return false;

            lock (sync)
            {
                Entry entry;
                if (!entries.TryGetValue(productId, out entry)) return false;
                if (ttl == TimeSpan.Zero || nowUtc - entry.StoredAtUtc > ttl)
                {
                    entries.Remove(productId);
                    return false;
                }
                snapshot = SnapshotCloner.Clone(entry.Snapshot);
                return true;
            }
        }

        public void Put(string productId, RemoteSnapshot snapshot, DateTimeOffset nowUtc)
        {
            if (string.IsNullOrWhiteSpace(productId)) throw new ArgumentException("Product ID is required.", nameof(productId));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (sync)
            {
                entries[productId] = new Entry
                {
                    Snapshot = SnapshotCloner.Clone(snapshot),
                    StoredAtUtc = nowUtc
                };
            }
        }

        public void Remove(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return;
            lock (sync) entries.Remove(productId);
        }

        public void Clear()
        {
            lock (sync) entries.Clear();
        }
    }
}

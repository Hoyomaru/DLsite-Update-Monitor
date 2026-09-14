using System;
using DLsiteUpdateMonitor.Core.Services;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class SnapshotCacheTests
    {
        [Fact]
        public void CacheExpiresAfterTtl()
        {
            var t0 = DateTimeOffset.Parse("2026-09-14T00:00:00Z");
            var cache = new SnapshotCache(TimeSpan.FromHours(24));
            var snapshot = TestSnapshots.Make();
            cache.Put(snapshot.ProductId, snapshot, t0);

            Assert.True(cache.TryGet(snapshot.ProductId, t0.AddHours(23), out _));
            Assert.False(cache.TryGet(snapshot.ProductId, t0.AddHours(25), out _));
        }

        [Fact]
        public void CacheReturnsClone_NotMutableStoredReference()
        {
            var t0 = DateTimeOffset.Parse("2026-09-14T00:00:00Z");
            var cache = new SnapshotCache(TimeSpan.FromHours(24));
            var snapshot = TestSnapshots.Make();
            cache.Put(snapshot.ProductId, snapshot, t0);
            Assert.True(cache.TryGet(snapshot.ProductId, t0, out var first));
            first.ProductId = "RJ00000000";

            Assert.True(cache.TryGet(snapshot.ProductId, t0, out var second));
            Assert.Equal("RJ01234567", second.ProductId);
        }
    }
}

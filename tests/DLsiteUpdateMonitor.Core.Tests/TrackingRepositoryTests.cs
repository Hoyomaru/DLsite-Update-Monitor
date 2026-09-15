using System;
using System.IO;
using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Persistence;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class TrackingRepositoryTests : IDisposable
    {
        private readonly string directory;

        public TrackingRepositoryTests()
        {
            directory = Path.Combine(Path.GetTempPath(), "DLsiteUpdateMonitorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            var repo = new TrackingRepository(directory);
            var db = new TrackingDatabase
            {
                CreatedAtUtc = DateTimeOffset.Parse("2026-09-14T00:00:00Z")
            };
            var id = Guid.NewGuid();
            db.Games[id] = new GameTrackingRecord { PlayniteGameId = id, RequestedProductId = "RJ01234567" };

            repo.Save(db, DateTimeOffset.Parse("2026-09-14T01:00:00Z"));
            var loaded = repo.Load();

            Assert.Equal(TrackingLoadSource.Primary, loaded.Source);
            Assert.True(loaded.Database.Games.ContainsKey(id));
        }

        [Fact]
        public void CorruptPrimary_UsesBackup()
        {
            var repo = new TrackingRepository(directory);
            var db = new TrackingDatabase { CreatedAtUtc = DateTimeOffset.UtcNow };
            repo.Save(db, DateTimeOffset.Parse("2026-09-14T01:00:00Z"));
            repo.Save(db, DateTimeOffset.Parse("2026-09-14T02:00:00Z")); // creates backup

            File.WriteAllText(Path.Combine(directory, "tracking.json"), "{ definitely broken");
            var loaded = repo.Load();

            Assert.Equal(TrackingLoadSource.Backup, loaded.Source);
            Assert.NotNull(loaded.Database);
            Assert.False(string.IsNullOrWhiteSpace(loaded.Warning));
        }

        [Fact]
        public void CorruptPrimary_LoadBackupThenSave_PreservesRecoverableBackup()
        {
            var id = Guid.NewGuid();
            var repo = new TrackingRepository(directory);
            var db = new TrackingDatabase { CreatedAtUtc = DateTimeOffset.UtcNow };
            db.Games[id] = new GameTrackingRecord { PlayniteGameId = id, RequestedProductId = "RJ01234567" };

            repo.Save(db, DateTimeOffset.Parse("2026-09-14T01:00:00Z"));
            repo.Save(db, DateTimeOffset.Parse("2026-09-14T02:00:00Z"));

            var primary = Path.Combine(directory, "tracking.json");
            File.WriteAllText(primary, "{ definitely broken");

            var recovered = repo.Load();
            Assert.Equal(TrackingLoadSource.Backup, recovered.Source);
            repo.Save(recovered.Database, DateTimeOffset.Parse("2026-09-14T03:00:00Z"));
            Assert.NotEmpty(Directory.GetFiles(directory, "tracking.json.corrupt-*"));

            // Prove the healthy backup survived the recovery save by corrupting the new primary too.
            File.WriteAllText(primary, "{ broken again");
            var secondRepo = new TrackingRepository(directory);
            var secondRecovery = secondRepo.Load();

            Assert.Equal(TrackingLoadSource.Backup, secondRecovery.Source);
            Assert.True(secondRecovery.Database.Games.ContainsKey(id));
        }

        [Fact]
        public void NullGameRecordInPrimary_UsesBackup()
        {
            var id = Guid.NewGuid();
            var repo = new TrackingRepository(directory);
            var db = new TrackingDatabase { CreatedAtUtc = DateTimeOffset.UtcNow };
            db.Games[id] = new GameTrackingRecord { PlayniteGameId = id, RequestedProductId = "RJ01234567" };
            repo.Save(db, DateTimeOffset.Parse("2026-09-14T01:00:00Z"));
            repo.Save(db, DateTimeOffset.Parse("2026-09-14T02:00:00Z"));

            File.WriteAllText(
                Path.Combine(directory, "tracking.json"),
                "{\"SchemaVersion\":1,\"Games\":{\"" + id + "\":null}}");

            var loaded = repo.Load();

            Assert.Equal(TrackingLoadSource.Backup, loaded.Source);
            Assert.NotNull(loaded.Database.Games[id]);
        }

        [Fact]
        public void CorruptPrimaryWithoutBackup_IsPreservedBeforeNewSave()
        {
            var primary = Path.Combine(directory, "tracking.json");
            File.WriteAllText(primary, "{ broken");
            var repo = new TrackingRepository(directory);
            var loaded = repo.Load();
            Assert.Equal(TrackingLoadSource.NewDatabase, loaded.Source);

            repo.Save(loaded.Database, DateTimeOffset.Parse("2026-09-14T03:00:00Z"));

            Assert.True(File.Exists(primary));
            Assert.NotEmpty(Directory.GetFiles(directory, "tracking.json.corrupt-*"));
        }

        [Fact]
        public void NewerSchema_IsRejectedInsteadOfOverwritten()
        {
            File.WriteAllText(Path.Combine(directory, "tracking.json"), "{\"SchemaVersion\":999,\"Games\":{}}");
            var repo = new TrackingRepository(directory);

            Assert.Throws<UnsupportedTrackingSchemaException>(() => repo.Load());
        }

        public void Dispose()
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch { }
        }
    }
}

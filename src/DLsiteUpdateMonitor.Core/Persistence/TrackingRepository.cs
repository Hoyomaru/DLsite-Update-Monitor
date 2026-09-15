using System;
using System.IO;
using System.Text;
using DLsiteUpdateMonitor.Core.Models;
using Newtonsoft.Json;

namespace DLsiteUpdateMonitor.Core.Persistence
{
    public enum TrackingLoadSource
    {
        NewDatabase,
        Primary,
        Backup
    }

    public sealed class TrackingLoadResult
    {
        public TrackingDatabase Database { get; set; }
        public TrackingLoadSource Source { get; set; }
        public string Warning { get; set; }
    }

    public sealed class UnsupportedTrackingSchemaException : Exception
    {
        public UnsupportedTrackingSchemaException(int schema)
            : base("Unsupported tracking schema version: " + schema) { }
    }

    public sealed class TrackingRepository
    {
        public const int CurrentSchemaVersion = 1;
        private readonly string primaryPath;
        private readonly string backupPath;
        private readonly string tempPath;
        private readonly object sync = new object();
        private bool preserveCorruptFilesOnNextSave;
        private bool preserveCorruptPrimaryOnNextSave;

        public TrackingRepository(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Directory is required.", nameof(directory));
            Directory.CreateDirectory(directory);
            primaryPath = Path.Combine(directory, "tracking.json");
            backupPath = Path.Combine(directory, "tracking.backup.json");
            tempPath = Path.Combine(directory, "tracking.tmp");
        }

        public TrackingLoadResult Load()
        {
            lock (sync)
            {
                TrackingDatabase db;
                Exception primaryError = null;

                if (File.Exists(primaryPath))
                {
                    try
                    {
                        db = ReadAndValidate(primaryPath);
                        return new TrackingLoadResult { Database = db, Source = TrackingLoadSource.Primary };
                    }
                    catch (UnsupportedTrackingSchemaException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        primaryError = ex;
                    }
                }

                if (File.Exists(backupPath))
                {
                    try
                    {
                        db = ReadAndValidate(backupPath);
                        if (primaryError != null)
                        {
                            preserveCorruptPrimaryOnNextSave = true;
                        }
                        return new TrackingLoadResult
                        {
                            Database = db,
                            Source = TrackingLoadSource.Backup,
                            Warning = "Primary tracking data could not be loaded; backup was used. " + primaryError?.Message
                        };
                    }
                    catch (UnsupportedTrackingSchemaException)
                    {
                        throw;
                    }
                    catch (Exception backupError)
                    {
                        preserveCorruptFilesOnNextSave = true;
                        preserveCorruptPrimaryOnNextSave = false;
                        return new TrackingLoadResult
                        {
                            Database = CreateNew(),
                            Source = TrackingLoadSource.NewDatabase,
                            Warning = "Both primary and backup tracking data are unreadable. Corrupt files will be preserved before the next save. Primary: "
                                + primaryError?.Message + " Backup: " + backupError.Message
                        };
                    }
                }

                if (primaryError != null) preserveCorruptFilesOnNextSave = true;
                return new TrackingLoadResult
                {
                    Database = CreateNew(),
                    Source = TrackingLoadSource.NewDatabase,
                    Warning = primaryError == null ? null : "Primary tracking data is unreadable and no backup exists. The corrupt file will be preserved before the next save: " + primaryError.Message
                };
            }
        }

        public void Save(TrackingDatabase database, DateTimeOffset nowUtc)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (database.SchemaVersion != CurrentSchemaVersion)
                throw new UnsupportedTrackingSchemaException(database.SchemaVersion);

            lock (sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(primaryPath));
                if (preserveCorruptFilesOnNextSave)
                {
                    PreserveCorruptFiles(nowUtc);
                    preserveCorruptFilesOnNextSave = false;
                    preserveCorruptPrimaryOnNextSave = false;
                }
                else if (preserveCorruptPrimaryOnNextSave)
                {
                    PreserveCorruptPrimary(nowUtc);
                    preserveCorruptPrimaryOnNextSave = false;
                }

                database.LastSavedAtUtc = nowUtc;
                var json = JsonConvert.SerializeObject(database, Formatting.Indented);

                WriteTempDurably(json);
                ReadAndValidate(tempPath); // fail closed before touching current data

                if (File.Exists(primaryPath))
                {
                    try
                    {
                        File.Replace(tempPath, primaryPath, backupPath, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        FallbackReplace();
                    }
                    catch (IOException)
                    {
                        FallbackReplace();
                    }
                }
                else
                {
                    File.Move(tempPath, primaryPath);
                }
            }
        }

        private void PreserveCorruptFiles(DateTimeOffset nowUtc)
        {
            var stamp = nowUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss");
            PreserveOne(primaryPath, primaryPath + ".corrupt-" + stamp);
            PreserveOne(backupPath, backupPath + ".corrupt-" + stamp);
        }

        private void PreserveCorruptPrimary(DateTimeOffset nowUtc)
        {
            var stamp = nowUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss");
            PreserveOne(primaryPath, primaryPath + ".corrupt-" + stamp);
        }

        private static void PreserveOne(string source, string destination)
        {
            if (!File.Exists(source)) return;
            var candidate = destination;
            var suffix = 1;
            while (File.Exists(candidate))
            {
                candidate = destination + "-" + suffix++;
            }
            File.Move(source, candidate);
        }

        private void WriteTempDurably(string json)
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private void FallbackReplace()
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Copy(primaryPath, backupPath, true);
            File.Delete(primaryPath);
            File.Move(tempPath, primaryPath);
        }

        private static TrackingDatabase ReadAndValidate(string path)
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var db = JsonConvert.DeserializeObject<TrackingDatabase>(json);
            if (db == null) throw new InvalidDataException("Tracking JSON deserialized to null.");
            if (db.SchemaVersion > CurrentSchemaVersion) throw new UnsupportedTrackingSchemaException(db.SchemaVersion);
            if (db.SchemaVersion < 1) throw new InvalidDataException("Tracking schema version is invalid.");
            if (db.Games == null) db.Games = new System.Collections.Generic.Dictionary<Guid, GameTrackingRecord>();
            return db;
        }

        private static TrackingDatabase CreateNew()
        {
            var now = DateTimeOffset.UtcNow;
            return new TrackingDatabase
            {
                SchemaVersion = CurrentSchemaVersion,
                CreatedAtUtc = now,
                LastSavedAtUtc = now
            };
        }
    }
}

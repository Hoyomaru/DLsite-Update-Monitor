using System;
using System.Security.Cryptography;
using System.Text;
using DLsiteUpdateMonitor.Core.Models;

namespace DLsiteUpdateMonitor.Core.Services
{
    public static class SnapshotFingerprint
    {
        public static string Compute(RemoteSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var update = snapshot.UpdateInfo == null
                ? "<null>"
                : snapshot.UpdateInfo.State + ":" + (snapshot.UpdateInfo.Normalized ?? string.Empty);
            var size = snapshot.FileSize == null
                ? "<null>"
                : snapshot.FileSize.State + ":" + snapshot.FileSize.Value;

            var payload = snapshot.SnapshotSchemaVersion + "\n"
                + (snapshot.ProductId ?? string.Empty).ToUpperInvariant() + "\n"
                + update + "\n"
                + size;

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}

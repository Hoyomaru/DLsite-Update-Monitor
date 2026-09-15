using System;
using System.IO;
using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Parsing;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class DlsitePageParserTests
    {
        private readonly DlsitePageParser parser = new DlsitePageParser();

        [Fact]
        public void HealthyFixture_ProducesComparableSnapshot()
        {
            var result = ParseFixture("healthy.html");

            Assert.Equal(ParseHealth.Healthy, result.Health);
            Assert.Equal("RJ01234567", result.Snapshot.ProductId);
            Assert.Equal("テスト作品", result.Snapshot.WorkName);
            Assert.Equal(ObservationState.Parsed, result.Snapshot.UpdateInfo.State);
            Assert.Contains("2026-09-20", result.Snapshot.UpdateInfo.Normalized);
            Assert.Equal(ObservationState.Parsed, result.Snapshot.FileSize.State);
            Assert.False(string.IsNullOrWhiteSpace(result.Snapshot.Fingerprint));
        }

        [Fact]
        public void ResolvedUrlWithoutProductId_IsDegradedInsteadOfFallingBackToRequestedId()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "healthy.html");
            var html = File.ReadAllText(path);

            var result = parser.Parse(
                html,
                "https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html",
                "https://www.dlsite.com/maniax/",
                "RJ01234567",
                DateTimeOffset.Parse("2026-09-14T10:00:00Z"));

            Assert.Equal(ParseHealth.Degraded, result.Health);
            Assert.Null(result.Snapshot.ProductId);
            Assert.Contains(result.Diagnostics, x => x.Contains("Product ID is missing"));
        }

        [Fact]
        public void MissingUpdateRow_IsHealthyMissing_NotParseFailure()
        {
            var result = ParseFixture("no-update-info.html");

            Assert.Equal(ParseHealth.Healthy, result.Health);
            Assert.Equal(ObservationState.Missing, result.Snapshot.UpdateInfo.State);
            Assert.Equal(ObservationState.Parsed, result.Snapshot.FileSize.State);
        }

        [Fact]
        public void InvalidFileSize_IsDegraded_AndCannotBeCompared()
        {
            var result = ParseFixture("bad-size.html");

            Assert.Equal(ParseHealth.Degraded, result.Health);
            Assert.Equal(ObservationState.Unparsed, result.Snapshot.FileSize.State);
        }

        [Fact]
        public void MissingTitle_IsParseError_WithoutSnapshot()
        {
            var result = ParseFixture("missing-title.html");

            Assert.Equal(ParseHealth.Error, result.Health);
            Assert.Null(result.Snapshot);
        }

        [Fact]
        public void MissingOutline_IsParseError_WithoutSnapshot()
        {
            var result = ParseFixture("missing-outline.html");

            Assert.Equal(ParseHealth.Error, result.Health);
            Assert.Null(result.Snapshot);
        }

        [Fact]
        public void ProductUnavailableErrorBox_IsClassifiedWithoutSnapshot()
        {
            var result = ParseFixture("unavailable.html");

            Assert.Equal(ParseHealth.ProductUnavailable, result.Health);
            Assert.Null(result.Snapshot);
            Assert.Contains(result.Diagnostics, x => x.Contains("ご利用いただけません"));
        }

        private DlsiteParseResult ParseFixture(string name)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
            var html = File.ReadAllText(path);
            return parser.Parse(
                html,
                "https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html",
                "https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html",
                "RJ01234567",
                DateTimeOffset.Parse("2026-09-14T10:00:00Z"));
        }
    }
}

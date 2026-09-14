using DLsiteUpdateMonitor.Core.Parsing;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class NormalizerTests
    {
        [Theory]
        [InlineData("2026年09月20日", "2026-09-20")]
        [InlineData("2026年9月20日", "2026-09-20")]
        [InlineData("2026/09/20", "2026-09-20")]
        [InlineData("  2026年09月20日   Ver1.2  ", "2026-09-20 Ver1.2")]
        public void UpdateInfo_NormalizesEquivalentDates(string raw, string expected)
        {
            Assert.Equal(expected, UpdateInfoNormalizer.Normalize(raw));
        }

        [Theory]
        [InlineData("1.20 GB", 1288490189L)]
        [InlineData("1228.8 MB", 1288490189L)]
        [InlineData("843.21MB", 884169769L)]
        [InlineData("1,024 KB", 1048576L)]
        public void FileSize_ParsesToBytes(string raw, long expected)
        {
            var result = FileSizeNormalizer.Parse(raw);
            Assert.True(result.Success);
            Assert.Equal(expected, result.Bytes);
        }

        [Fact]
        public void FileSize_InvalidText_FailsClosed()
        {
            Assert.False(FileSizeNormalizer.Parse("unknown").Success);
        }
    }
}

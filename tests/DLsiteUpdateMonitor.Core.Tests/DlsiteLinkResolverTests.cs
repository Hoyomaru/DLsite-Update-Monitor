using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Services;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class DlsiteLinkResolverTests
    {
        private readonly DlsiteLinkResolver resolver = new DlsiteLinkResolver();

        [Theory]
        [InlineData("https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html", "RJ01234567")]
        [InlineData("https://www.dlsite.com/home/work/=/product_id/RJ123456.html", "RJ123456")]
        [InlineData("https://www.dlsite.com/soft/work/=/product_id/VJ12345678.html?locale=ja_JP", "VJ12345678")]
        [InlineData("https://www.dlsite.com/pro/work/=/product_id/BJ123456.html", "BJ123456")]
        public void ValidDlsiteUrl_Resolves(string url, string expected)
        {
            var result = resolver.Resolve(new[] { url });
            Assert.Equal(LinkResolutionStatus.Resolved, result.Status);
            Assert.Equal(expected, result.Target.ProductId);
        }

        [Fact]
        public void LinkDisplayNameIsIrrelevant_BecauseResolverConsumesUrlsOnly()
        {
            var result = resolver.Resolve(new[]
            {
                "https://example.com/foo",
                "https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html"
            });

            Assert.Equal(LinkResolutionStatus.Resolved, result.Status);
        }

        [Fact]
        public void DuplicateUrlsForSameProduct_AreNotAmbiguous()
        {
            var result = resolver.Resolve(new[]
            {
                "https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html",
                "https://www.dlsite.com/home/work/=/product_id/RJ01234567.html"
            });

            Assert.Equal(LinkResolutionStatus.Resolved, result.Status);
            Assert.Equal("RJ01234567", result.Target.ProductId);
        }

        [Fact]
        public void DifferentProductIds_AreAmbiguous()
        {
            var result = resolver.Resolve(new[]
            {
                "https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html",
                "https://www.dlsite.com/maniax/work/=/product_id/RJ07654321.html"
            });

            Assert.Equal(LinkResolutionStatus.Ambiguous, result.Status);
            Assert.Equal(2, result.Candidates.Count);
        }

        [Fact]
        public void LookalikeHost_IsRejected()
        {
            var result = resolver.Resolve(new[]
            {
                "https://dlsite.com.example.com/maniax/work/=/product_id/RJ01234567.html"
            });

            Assert.Equal(LinkResolutionStatus.NoDlsiteLink, result.Status);
        }

        [Fact]
        public void DlsiteUrlWithoutProductId_IsInvalid()
        {
            var result = resolver.Resolve(new[] { "https://www.dlsite.com/maniax/" });
            Assert.Equal(LinkResolutionStatus.Invalid, result.Status);
        }
    }
}

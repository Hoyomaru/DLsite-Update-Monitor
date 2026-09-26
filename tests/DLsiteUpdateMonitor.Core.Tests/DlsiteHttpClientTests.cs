using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DLsiteUpdateMonitor.Core.Http;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class DlsiteHttpClientTests
    {
        [Fact]
        public async Task Success_ReturnsHtmlAndResolvedUrl()
        {
            var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>ok</html>"),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html")
            });
            using var client = Create(handler, retryCount: 0);

            var result = await client.FetchAsync("https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.Success, result.Status);
            Assert.Equal("<html>ok</html>", result.Html);
            Assert.Equal(1, result.Attempts);
        }

        [Fact]
        public async Task ExternalResolvedUrl_IsRejectedAndNotRetried()
        {
            var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>fake</html>"),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/product_id/RJ01234567.html")
            });
            using var client = Create(handler, retryCount: 2);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.UntrustedRedirect, result.Status);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task HttpResolvedUrl_IsRejectedAndNotRetried()
        {
            var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>fake</html>"),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://www.dlsite.com/product_id/RJ01234567.html")
            });
            using var client = Create(handler, retryCount: 2);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.UntrustedRedirect, result.Status);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task ExternalRedirectLocation_IsRejectedBeforeFollowUpRequest()
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("https://example.com/private");
            var handler = new SequenceHandler(
                redirect,
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("must-not-be-requested") });
            using var client = Create(handler, retryCount: 2);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.UntrustedRedirect, result.Status);
            Assert.Equal(1, handler.CallCount);
            Assert.Equal("https://example.com/private", result.ResolvedUrl);
        }

        [Fact]
        public async Task HttpRedirectLocation_IsRejectedBeforeFollowUpRequest()
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("http://www.dlsite.com/unsafe");
            var handler = new SequenceHandler(redirect);
            using var client = Create(handler, retryCount: 0);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.UntrustedRedirect, result.Status);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task TrustedRedirect_IsFollowedManually()
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("/redirected", UriKind.Relative);
            var handler = new SequenceHandler(
                redirect,
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
            using var client = Create(handler, retryCount: 0);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.Success, result.Status);
            Assert.Equal(2, handler.CallCount);
            Assert.Equal("https://www.dlsite.com/redirected", result.ResolvedUrl);
        }

        [Fact]
        public async Task Forbidden_IsNotRetried()
        {
            var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.Forbidden));
            using var client = Create(handler, retryCount: 2);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.AccessDenied, result.Status);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task BadRequest_IsClientErrorAndNotRetried()
        {
            var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));
            using var client = Create(handler, retryCount: 2);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.ClientError, result.Status);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task NotFound_IsProductUnavailableAndNotRetried()
        {
            var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
            using var client = Create(handler, retryCount: 2);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.ProductUnavailable, result.Status);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task RateLimit_RetriesAndHonorsRetryAfter()
        {
            var first = new HttpResponseMessage((HttpStatusCode)429);
            first.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            var second = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
            var handler = new SequenceHandler(first, second);
            var delay = new RecordingDelay();
            using var client = Create(handler, retryCount: 1, delay: delay);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.Success, result.Status);
            Assert.Equal(2, handler.CallCount);
            Assert.Contains(TimeSpan.FromSeconds(7), delay.Delays);
        }

        [Fact]
        public async Task LongRetryAfter_DoesNotOccupyOperationWithLongDelay()
        {
            var first = new HttpResponseMessage((HttpStatusCode)429);
            first.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(2));
            var handler = new SequenceHandler(
                first,
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("must-not-be-requested") });
            var delay = new RecordingDelay();
            using var client = Create(handler, retryCount: 1, delay: delay, maxRetryAfterDelay: TimeSpan.FromSeconds(30));

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.RateLimited, result.Status);
            Assert.Equal(1, handler.CallCount);
            Assert.Empty(delay.Delays);
            Assert.Contains("deferred", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ResponseLargerThanLimit_IsRejected()
        {
            var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[101])
            });
            using var client = Create(handler, retryCount: 0, maxResponseBytes: 100);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.NetworkError, result.Status);
            Assert.Contains("size limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ServerError_RetriesThenSucceeds()
        {
            var handler = new SequenceHandler(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
            using var client = Create(handler, retryCount: 1);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.Success, result.Status);
            Assert.Equal(2, handler.CallCount);
        }

        private static DlsiteHttpClient Create(
            SequenceHandler handler,
            int retryCount,
            IAsyncDelay delay = null,
            TimeSpan? maxRetryAfterDelay = null,
            int maxResponseBytes = 10 * 1024 * 1024)
        {
            var http = new HttpClient(handler);
            return new DlsiteHttpClient(http, new DlsiteHttpOptions
            {
                RetryCount = retryCount,
                MinimumRequestInterval = TimeSpan.Zero,
                Timeout = TimeSpan.FromSeconds(5),
                FirstRetryDelay = TimeSpan.Zero,
                SecondRetryDelay = TimeSpan.Zero,
                MaxRetryAfterDelay = maxRetryAfterDelay ?? TimeSpan.FromSeconds(30),
                MaxRedirects = 5,
                MaxResponseBytes = maxResponseBytes
            }, new FakeClock(), delay ?? new RecordingDelay(), ownsClient: true);
        }

        private sealed class SequenceHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> responses;
            public int CallCount { get; private set; }

            public SequenceHandler(params HttpResponseMessage[] responses)
            {
                this.responses = new Queue<HttpResponseMessage>(responses);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                if (responses.Count == 0) throw new InvalidOperationException("No response queued.");
                var response = responses.Dequeue();
                if (response.RequestMessage == null) response.RequestMessage = request;
                return Task.FromResult(response);
            }
        }

        private sealed class RecordingDelay : IAsyncDelay
        {
            public List<TimeSpan> Delays { get; } = new List<TimeSpan>();
            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                Delays.Add(delay);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-14T00:00:00Z");
        }
    }
}

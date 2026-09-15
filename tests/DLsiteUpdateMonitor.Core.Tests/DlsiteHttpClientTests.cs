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
        public async Task Forbidden_IsNotRetried()
        {
            var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.Forbidden));
            using var client = Create(handler, retryCount: 2);

            var result = await client.FetchAsync("https://www.dlsite.com/test", CancellationToken.None);

            Assert.Equal(DlsiteFetchStatus.AccessDenied, result.Status);
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

        private static DlsiteHttpClient Create(SequenceHandler handler, int retryCount, IAsyncDelay delay = null)
        {
            var http = new HttpClient(handler);
            return new DlsiteHttpClient(http, new DlsiteHttpOptions
            {
                RetryCount = retryCount,
                MinimumRequestInterval = TimeSpan.Zero,
                Timeout = TimeSpan.FromSeconds(5),
                FirstRetryDelay = TimeSpan.Zero,
                SecondRetryDelay = TimeSpan.Zero
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

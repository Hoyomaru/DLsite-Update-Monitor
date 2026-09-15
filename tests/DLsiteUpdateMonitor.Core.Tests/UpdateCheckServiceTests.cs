using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DLsiteUpdateMonitor.Core.Http;
using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Parsing;
using DLsiteUpdateMonitor.Core.Services;
using Xunit;

namespace DLsiteUpdateMonitor.Core.Tests
{
    public sealed class UpdateCheckServiceTests
    {
        private const string ProductId = "RJ01234567";
        private const string Url = "https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html";

        [Fact]
        public async Task FirstHealthyCheck_CreatesBaseline()
        {
            var handler = new SequenceHandler(Response(HttpStatusCode.OK, Url, HealthyHtml("2026年09月10日", "843.21MB")));
            using var http = CreateHttp(handler);
            var service = CreateService(http);
            var record = new GameTrackingRecord();

            var result = await service.CheckAsync(record, Target(), true, CancellationToken.None);

            Assert.Equal(CheckHealth.Healthy, result.Health);
            Assert.Equal(ComparisonOutcome.BaselineCreated, result.Comparison.Outcome);
            Assert.Equal(MonitoringState.Clean, record.MonitoringState);
            Assert.NotNull(record.AcknowledgedSnapshot);
            Assert.Equal(record.AcknowledgedSnapshot.Fingerprint, record.CurrentSnapshot.Fingerprint);
        }

        [Fact]
        public async Task RedirectToDifferentProduct_DoesNotCreateBaseline()
        {
            const string redirected = "https://www.dlsite.com/maniax/work/=/product_id/RJ87654321.html";
            var handler = new SequenceHandler(Response(HttpStatusCode.OK, redirected, HealthyHtml("2026年09月10日", "843.21MB")));
            using var http = CreateHttp(handler);
            var service = CreateService(http);
            var record = new GameTrackingRecord();

            var result = await service.CheckAsync(record, Target(), true, CancellationToken.None);

            Assert.Equal(CheckHealth.RedirectedToDifferentProduct, result.Health);
            Assert.Equal(ProductCheckFailureScope.RemoteProduct, result.FailureScope);
            Assert.Null(record.AcknowledgedSnapshot);
            Assert.Null(record.CurrentSnapshot);
            Assert.Equal(MonitoringState.Uninitialized, record.MonitoringState);
        }

        [Fact]
        public async Task HttpFailure_PreservesPendingStateAndSnapshots()
        {
            var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, Url)
            });
            using var http = CreateHttp(handler);
            var service = CreateService(http);
            var acknowledged = TestSnapshots.Make(productId: ProductId, updateNormalized: "2026-09-01", size: 1000);
            var current = TestSnapshots.Make(productId: ProductId, updateNormalized: "2026-09-10", size: 1200);
            var record = new GameTrackingRecord
            {
                MonitoringState = MonitoringState.PendingUpdateAndFileChange,
                AcknowledgedSnapshot = SnapshotCloner.Clone(acknowledged),
                CurrentSnapshot = SnapshotCloner.Clone(current)
            };

            var result = await service.CheckAsync(record, Target(), true, CancellationToken.None);

            Assert.Equal(CheckHealth.AccessDenied, result.Health);
            Assert.Equal(ProductCheckFailureScope.RemoteProduct, result.FailureScope);
            Assert.Equal(MonitoringState.PendingUpdateAndFileChange, record.MonitoringState);
            Assert.Equal(acknowledged.Fingerprint, record.AcknowledgedSnapshot.Fingerprint);
            Assert.Equal(current.Fingerprint, record.CurrentSnapshot.Fingerprint);
        }

        [Fact]
        public async Task DegradedParse_DoesNotOverwriteCurrentSnapshot()
        {
            var handler = new SequenceHandler(Response(HttpStatusCode.OK, Url, HealthyHtml("2026年09月20日", "not-a-size")));
            using var http = CreateHttp(handler);
            var service = CreateService(http);
            var acknowledged = TestSnapshots.Make(productId: ProductId, updateNormalized: "2026-09-01", size: 1000);
            var current = TestSnapshots.Make(productId: ProductId, updateNormalized: "2026-09-10", size: 1200);
            var record = new GameTrackingRecord
            {
                MonitoringState = MonitoringState.PendingUpdateAndFileChange,
                AcknowledgedSnapshot = SnapshotCloner.Clone(acknowledged),
                CurrentSnapshot = SnapshotCloner.Clone(current)
            };

            var result = await service.CheckAsync(record, Target(), true, CancellationToken.None);

            Assert.Equal(CheckHealth.ParseDegraded, result.Health);
            Assert.Equal(ProductCheckFailureScope.RemoteProduct, result.FailureScope);
            Assert.Equal(MonitoringState.PendingUpdateAndFileChange, record.MonitoringState);
            Assert.Equal(current.Fingerprint, record.CurrentSnapshot.Fingerprint);
            Assert.NotNull(record.LastObservation);
        }

        [Fact]
        public async Task CacheHit_ReusesObservationWithoutSecondHttpCall()
        {
            var handler = new SequenceHandler(Response(HttpStatusCode.OK, Url, HealthyHtml("2026年09月10日", "843.21MB")));
            using var http = CreateHttp(handler);
            var service = CreateService(http);

            var first = new GameTrackingRecord();
            var second = new GameTrackingRecord();
            var firstResult = await service.CheckAsync(first, Target(), true, CancellationToken.None);
            var secondResult = await service.CheckAsync(second, Target(), false, CancellationToken.None);

            Assert.Equal(CheckHealth.Healthy, firstResult.Health);
            Assert.True(secondResult.FromCache);
            Assert.Equal(1, handler.CallCount);
            Assert.Equal(ComparisonOutcome.BaselineCreated, secondResult.Comparison.Outcome);
        }

        [Fact]
        public async Task Http200UnavailablePage_IsProductUnavailable_AndPreservesPendingState()
        {
            var html = "<html><body><div class='error_box_work'><div class='error_large_text'>利用不可</div></div></body></html>";
            var handler = new SequenceHandler(Response(HttpStatusCode.OK, Url, html));
            using var http = CreateHttp(handler);
            var service = CreateService(http);
            var acknowledged = TestSnapshots.Make(productId: ProductId, updateNormalized: "2026-09-01", size: 1000);
            var current = TestSnapshots.Make(productId: ProductId, updateNormalized: "2026-09-10", size: 1200);
            var record = new GameTrackingRecord
            {
                MonitoringState = MonitoringState.PendingUpdateAndFileChange,
                AcknowledgedSnapshot = SnapshotCloner.Clone(acknowledged),
                CurrentSnapshot = SnapshotCloner.Clone(current)
            };

            var result = await service.CheckAsync(record, Target(), true, CancellationToken.None);

            Assert.Equal(CheckHealth.ProductUnavailable, result.Health);
            Assert.Equal(ProductCheckFailureScope.RemoteProduct, result.FailureScope);
            Assert.Equal(MonitoringState.PendingUpdateAndFileChange, record.MonitoringState);
            Assert.Equal(acknowledged.Fingerprint, record.AcknowledgedSnapshot.Fingerprint);
            Assert.Equal(current.Fingerprint, record.CurrentSnapshot.Fingerprint);
        }

        [Fact]
        public async Task HealthyRemoteObservation_IsCachedEvenWhenFirstRecordsAcknowledgedSnapshotIsInvalid()
        {
            var handler = new SequenceHandler(Response(HttpStatusCode.OK, Url, HealthyHtml("2026年09月10日", "843.21MB")));
            using var http = CreateHttp(handler);
            var service = CreateService(http);

            var invalidAcknowledged = TestSnapshots.Make(productId: ProductId, updateNormalized: "2026-09-01", size: 1000);
            invalidAcknowledged.FileSize = ObservedField<long>.Missing();
            invalidAcknowledged.Fingerprint = SnapshotFingerprint.Compute(invalidAcknowledged);

            var first = new GameTrackingRecord
            {
                MonitoringState = MonitoringState.Clean,
                AcknowledgedSnapshot = invalidAcknowledged
            };
            var second = new GameTrackingRecord();

            var firstResult = await service.CheckAsync(first, Target(), true, CancellationToken.None);
            var secondResult = await service.CheckAsync(second, Target(), false, CancellationToken.None);

            Assert.Equal(CheckHealth.ParseDegraded, firstResult.Health);
            Assert.True(firstResult.HasReusableSnapshot);
            Assert.Equal(ProductCheckFailureScope.None, firstResult.FailureScope);
            Assert.True(secondResult.FromCache);
            Assert.True(secondResult.HasReusableSnapshot);
            Assert.Equal(CheckHealth.Healthy, secondResult.Health);
            Assert.Equal(ComparisonOutcome.BaselineCreated, secondResult.Comparison.Outcome);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task ChangedRegisteredProductId_RequiresExplicitReset_WithoutHttpCall()
        {
            var handler = new SequenceHandler();
            using var http = CreateHttp(handler);
            var service = CreateService(http);
            var oldSnapshot = TestSnapshots.Make(productId: "RJ11111111", updateNormalized: "2026-09-01", size: 1000);
            var record = new GameTrackingRecord
            {
                RequestedProductId = "RJ11111111",
                MonitoringState = MonitoringState.PendingUpdateInfo,
                AcknowledgedSnapshot = SnapshotCloner.Clone(oldSnapshot),
                CurrentSnapshot = SnapshotCloner.Clone(oldSnapshot)
            };

            var result = await service.CheckAsync(record, Target(), true, CancellationToken.None);

            Assert.Equal(CheckHealth.LinkError, result.Health);
            Assert.Equal(ProductCheckFailureScope.LocalRecord, result.FailureScope);
            Assert.Contains("Reset monitoring", result.Message);
            Assert.Equal(MonitoringState.PendingUpdateInfo, record.MonitoringState);
            Assert.Equal("RJ11111111", record.RequestedProductId);
            Assert.Equal("RJ11111111", record.AcknowledgedSnapshot.ProductId);
            Assert.Equal(0, handler.CallCount);
        }

        private static UpdateCheckService CreateService(DlsiteHttpClient http)
        {
            return new UpdateCheckService(
                http,
                new DlsitePageParser(),
                new TrackingStateMachine(),
                new SnapshotCache(TimeSpan.FromHours(24)),
                new FakeClock());
        }

        private static DlsiteHttpClient CreateHttp(SequenceHandler handler)
        {
            return new DlsiteHttpClient(
                new HttpClient(handler),
                new DlsiteHttpOptions
                {
                    RetryCount = 0,
                    MinimumRequestInterval = TimeSpan.Zero,
                    Timeout = TimeSpan.FromSeconds(5)
                },
                new FakeClock(),
                new NoDelay(),
                ownsClient: true);
        }

        private static DlsiteTarget Target()
        {
            return new DlsiteTarget { ProductId = ProductId, RegisteredUrl = Url };
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string resolvedUrl, string html)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(html),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, resolvedUrl)
            };
        }

        private static string HealthyHtml(string updateInfo, string fileSize)
        {
            return "<html><body>"
                + "<h1 id='work_name'>Test Work</h1>"
                + "<table id='work_outline'>"
                + "<tr><th>更新情報</th><td>" + updateInfo + "</td></tr>"
                + "<tr><th>ファイル容量</th><td>" + fileSize + "</td></tr>"
                + "</table></body></html>";
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

        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-14T10:00:00Z");
        }

        private sealed class NoDelay : IAsyncDelay
        {
            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}

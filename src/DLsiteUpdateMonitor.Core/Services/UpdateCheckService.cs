using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DLsiteUpdateMonitor.Core.Http;
using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Parsing;

namespace DLsiteUpdateMonitor.Core.Services
{
    public sealed class UpdateCheckService
    {
        private readonly DlsiteHttpClient httpClient;
        private readonly DlsitePageParser parser;
        private readonly TrackingStateMachine stateMachine;
        private readonly SnapshotCache cache;
        private readonly IClock clock;

        public UpdateCheckService(
            DlsiteHttpClient httpClient,
            DlsitePageParser parser,
            TrackingStateMachine stateMachine,
            SnapshotCache cache,
            IClock clock = null)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
            this.stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.clock = clock ?? new SystemClock();
        }

        public async Task<ProductCheckResult> CheckAsync(GameTrackingRecord record, DlsiteTarget target, bool forceRefresh, CancellationToken cancellationToken)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var now = clock.UtcNow;
            var previouslyTrackedProductId = !string.IsNullOrWhiteSpace(record.RequestedProductId)
                ? record.RequestedProductId
                : record.AcknowledgedSnapshot?.ProductId;

            record.RegisteredUrl = target.RegisteredUrl;

            // A Playnite Link being changed to a different RJ/RE/BJ/VJ product is not a remote
            // update. Never carry an old baseline across product identity. Require an explicit reset
            // so the user cannot accidentally acknowledge one product as another.
            if (!string.IsNullOrWhiteSpace(previouslyTrackedProductId)
                && !string.Equals(previouslyTrackedProductId, target.ProductId, StringComparison.OrdinalIgnoreCase))
            {
                var message = "The registered DLsite product changed from " + previouslyTrackedProductId
                    + " to " + target.ProductId + ". Reset monitoring before creating a new baseline.";
                stateMachine.RecordCheckFailure(record, CheckHealth.LinkError, new CheckError
                {
                    Type = CheckHealth.LinkError,
                    OccurredAtUtc = now,
                    Message = message,
                    Url = target.RegisteredUrl
                }, now);

                return new ProductCheckResult
                {
                    ProductId = target.ProductId,
                    Health = CheckHealth.LinkError,
                    Message = message
                };
            }

            record.RequestedProductId = target.ProductId;

            RemoteSnapshot cached;
            if (!forceRefresh && cache.TryGet(target.ProductId, now, out cached))
            {
                if (!IdentityMatches(target.ProductId, cached.ProductId))
                {
                    return RecordIdentityMismatch(record, target, cached.ResolvedUrl, now, true);
                }

                var comparison = stateMachine.ApplySuccessfulSnapshot(record, cached, now);
                return new ProductCheckResult
                {
                    ProductId = target.ProductId,
                    FromCache = true,
                    HasReusableSnapshot = true,
                    Health = record.LastCheckHealth,
                    Comparison = comparison,
                    Message = comparison.Reason
                };
            }

            var fetch = await httpClient.FetchAsync(target.RegisteredUrl, cancellationToken).ConfigureAwait(false);
            if (!fetch.Success)
            {
                var health = MapFetchHealth(fetch.Status);
                stateMachine.RecordCheckFailure(record, health, new CheckError
                {
                    Type = health,
                    OccurredAtUtc = now,
                    HttpStatusCode = fetch.HttpStatusCode.HasValue ? (int?)fetch.HttpStatusCode.Value : null,
                    Message = fetch.ErrorMessage ?? fetch.Status.ToString(),
                    Url = fetch.ResolvedUrl ?? fetch.SourceUrl
                }, now);

                return new ProductCheckResult
                {
                    ProductId = target.ProductId,
                    Health = health,
                    Fetch = fetch,
                    Message = fetch.ErrorMessage
                };
            }

            var parse = parser.Parse(fetch.Html, fetch.SourceUrl, fetch.ResolvedUrl, target.ProductId, now);
            if (parse.Health == ParseHealth.ProductUnavailable)
            {
                var message = string.Join("; ", parse.Diagnostics ?? Array.Empty<string>());
                stateMachine.RecordCheckFailure(record, CheckHealth.ProductUnavailable, new CheckError
                {
                    Type = CheckHealth.ProductUnavailable,
                    OccurredAtUtc = now,
                    HttpStatusCode = fetch.HttpStatusCode.HasValue ? (int?)fetch.HttpStatusCode.Value : null,
                    Message = message,
                    Url = fetch.ResolvedUrl
                }, now);

                return new ProductCheckResult
                {
                    ProductId = target.ProductId,
                    Health = CheckHealth.ProductUnavailable,
                    Fetch = fetch,
                    Parse = parse,
                    Message = message
                };
            }

            if (parse.Health == ParseHealth.Error || parse.Snapshot == null)
            {
                var message = string.Join("; ", parse.Diagnostics ?? Array.Empty<string>());
                stateMachine.RecordCheckFailure(record, CheckHealth.ParseError, new CheckError
                {
                    Type = CheckHealth.ParseError,
                    OccurredAtUtc = now,
                    Message = message,
                    Url = fetch.ResolvedUrl
                }, now);

                return new ProductCheckResult
                {
                    ProductId = target.ProductId,
                    Health = CheckHealth.ParseError,
                    Fetch = fetch,
                    Parse = parse,
                    Message = message
                };
            }

            record.ResolvedUrl = parse.Snapshot.ResolvedUrl;
            record.ResolvedProductId = parse.Snapshot.ProductId;

            if (!IdentityMatches(target.ProductId, parse.Snapshot.ProductId))
            {
                return RecordIdentityMismatch(record, target, fetch.ResolvedUrl, now, false, fetch, parse);
            }

            // Cache validity belongs to the remote observation, not to this game's local tracking state.
            // A malformed/legacy acknowledged snapshot for one Playnite game must not force a second HTTP
            // request (or failure) for another game that points at the same DLsite product.
            if (parse.Health == ParseHealth.Healthy)
            {
                cache.Put(target.ProductId, parse.Snapshot, now);
            }

            var result = stateMachine.ApplySuccessfulSnapshot(record, parse.Snapshot, now);

            return new ProductCheckResult
            {
                ProductId = target.ProductId,
                FromCache = false,
                HasReusableSnapshot = parse.Health == ParseHealth.Healthy,
                Health = record.LastCheckHealth,
                Comparison = result,
                Fetch = fetch,
                Parse = parse,
                Message = result.Reason
            };
        }

        private ProductCheckResult RecordIdentityMismatch(
            GameTrackingRecord record,
            DlsiteTarget target,
            string resolvedUrl,
            DateTimeOffset now,
            bool fromCache,
            DlsiteFetchResult fetch = null,
            DlsiteParseResult parse = null)
        {
            var message = "DLsite resolved to a different product ID. Requested " + target.ProductId
                + ", resolved " + (parse?.Snapshot?.ProductId ?? "unknown") + ".";
            stateMachine.RecordCheckFailure(record, CheckHealth.RedirectedToDifferentProduct, new CheckError
            {
                Type = CheckHealth.RedirectedToDifferentProduct,
                OccurredAtUtc = now,
                Message = message,
                Url = resolvedUrl
            }, now);

            return new ProductCheckResult
            {
                ProductId = target.ProductId,
                FromCache = fromCache,
                Health = CheckHealth.RedirectedToDifferentProduct,
                Fetch = fetch,
                Parse = parse,
                Message = message
            };
        }

        private static bool IdentityMatches(string requested, string resolved)
        {
            return !string.IsNullOrWhiteSpace(requested)
                && !string.IsNullOrWhiteSpace(resolved)
                && string.Equals(requested, resolved, StringComparison.OrdinalIgnoreCase);
        }

        private static CheckHealth MapFetchHealth(DlsiteFetchStatus status)
        {
            switch (status)
            {
                case DlsiteFetchStatus.RateLimited: return CheckHealth.RateLimited;
                case DlsiteFetchStatus.AccessDenied: return CheckHealth.AccessDenied;
                case DlsiteFetchStatus.Timeout: return CheckHealth.Timeout;
                case DlsiteFetchStatus.ProductUnavailable: return CheckHealth.ProductUnavailable;
                case DlsiteFetchStatus.Cancelled: return CheckHealth.Cancelled;
                default: return CheckHealth.NetworkError;
            }
        }
    }
}

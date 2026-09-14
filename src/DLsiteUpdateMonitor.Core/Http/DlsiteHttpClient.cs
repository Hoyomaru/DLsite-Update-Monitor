using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DLsiteUpdateMonitor.Core.Http
{
    public sealed class DlsiteHttpClient : IDisposable
    {
        private readonly HttpClient client;
        private readonly bool ownsClient;
        private readonly DlsiteHttpOptions options;
        private readonly IClock clock;
        private readonly IAsyncDelay delay;
        private readonly SemaphoreSlim fetchGate = new SemaphoreSlim(1, 1);
        private DateTimeOffset? lastRequestStartedUtc;
        private bool disposed;

        public DlsiteHttpClient(HttpClient client, DlsiteHttpOptions options = null, IClock clock = null, IAsyncDelay delay = null, bool ownsClient = false)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.options = options ?? new DlsiteHttpOptions();
            this.clock = clock ?? new SystemClock();
            this.delay = delay ?? new SystemAsyncDelay();
            this.ownsClient = ownsClient;

            if (this.options.MinimumRequestInterval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
            if (this.options.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
            if (this.options.RetryCount < 0) throw new ArgumentOutOfRangeException(nameof(options));
        }

        public static DlsiteHttpClient CreateDefault(DlsiteHttpOptions options = null)
        {
            var handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                AllowAutoRedirect = true
            };
            var baseUri = new Uri("https://www.dlsite.com");
            handler.CookieContainer.Add(baseUri, new Cookie("locale", "ja_JP", "/", ".dlsite.com"));
            handler.CookieContainer.Add(baseUri, new Cookie("loginchecked", "1", "/", ".dlsite.com"));
            return new DlsiteHttpClient(new HttpClient(handler), options, ownsClient: true);
        }

        public async Task<DlsiteFetchResult> FetchAsync(string url, CancellationToken cancellationToken)
        {
            if (disposed) throw new ObjectDisposedException(nameof(DlsiteHttpClient));
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL is required.", nameof(url));

            await fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var maxAttempts = options.RetryCount + 1;
                DlsiteFetchResult last = null;

                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await EnforceMinimumIntervalAsync(cancellationToken).ConfigureAwait(false);

                    var result = await SendOnceAsync(url, attempt, cancellationToken).ConfigureAwait(false);
                    last = result;

                    if (result.Success || !ShouldRetry(result) || attempt == maxAttempts)
                    {
                        return result;
                    }

                    var retryDelay = result.RetryAfter ?? GetRetryDelay(attempt);
                    if (retryDelay > TimeSpan.Zero)
                    {
                        await delay.DelayAsync(retryDelay, cancellationToken).ConfigureAwait(false);
                    }
                }

                return last ?? new DlsiteFetchResult
                {
                    Status = DlsiteFetchStatus.NetworkError,
                    SourceUrl = url,
                    ErrorMessage = "No HTTP attempt was made."
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new DlsiteFetchResult
                {
                    Status = DlsiteFetchStatus.Cancelled,
                    SourceUrl = url,
                    ErrorMessage = "Operation cancelled."
                };
            }
            finally
            {
                fetchGate.Release();
            }
        }

        private async Task<DlsiteFetchResult> SendOnceAsync(string url, int attempt, CancellationToken externalCancellation)
        {
            lastRequestStartedUtc = clock.UtcNow;
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation))
            {
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/136 Safari/537.36");
                request.Headers.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en;q=0.5");
                timeoutCts.CancelAfter(options.Timeout);

                try
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token).ConfigureAwait(false))
                    {
                        var status = response.StatusCode;
                        var resolvedUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

                        if (status == HttpStatusCode.OK)
                        {
                            return new DlsiteFetchResult
                            {
                                Status = DlsiteFetchStatus.Success,
                                HttpStatusCode = status,
                                Html = await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                                SourceUrl = url,
                                ResolvedUrl = resolvedUrl,
                                Attempts = attempt
                            };
                        }

                        if ((int)status == 429)
                        {
                            return new DlsiteFetchResult
                            {
                                Status = DlsiteFetchStatus.RateLimited,
                                HttpStatusCode = status,
                                SourceUrl = url,
                                ResolvedUrl = resolvedUrl,
                                Attempts = attempt,
                                RetryAfter = ParseRetryAfter(response)
                            };
                        }

                        if (status == HttpStatusCode.Forbidden)
                        {
                            return Error(DlsiteFetchStatus.AccessDenied, status, url, resolvedUrl, attempt, "DLsite returned HTTP 403.");
                        }

                        if (status == HttpStatusCode.NotFound || status == HttpStatusCode.Gone)
                        {
                            return Error(DlsiteFetchStatus.ProductUnavailable, status, url, resolvedUrl, attempt, "DLsite product is unavailable.");
                        }

                        if ((int)status >= 500 && (int)status <= 599)
                        {
                            return Error(DlsiteFetchStatus.ServerError, status, url, resolvedUrl, attempt, "DLsite server error: HTTP " + (int)status + ".");
                        }

                        return Error(DlsiteFetchStatus.NetworkError, status, url, resolvedUrl, attempt, "Unexpected HTTP status: " + (int)status + ".");
                    }
                }
                catch (TaskCanceledException) when (!externalCancellation.IsCancellationRequested)
                {
                    return new DlsiteFetchResult
                    {
                        Status = DlsiteFetchStatus.Timeout,
                        SourceUrl = url,
                        Attempts = attempt,
                        ErrorMessage = "DLsite request timed out."
                    };
                }
                catch (HttpRequestException ex)
                {
                    return new DlsiteFetchResult
                    {
                        Status = DlsiteFetchStatus.NetworkError,
                        SourceUrl = url,
                        Attempts = attempt,
                        ErrorMessage = ex.Message
                    };
                }
            }
        }

        private async Task EnforceMinimumIntervalAsync(CancellationToken cancellationToken)
        {
            if (!lastRequestStartedUtc.HasValue) return;
            var elapsed = clock.UtcNow - lastRequestStartedUtc.Value;
            var wait = options.MinimumRequestInterval - elapsed;
            if (wait > TimeSpan.Zero)
            {
                await delay.DelayAsync(wait, cancellationToken).ConfigureAwait(false);
            }
        }

        private bool ShouldRetry(DlsiteFetchResult result)
        {
            switch (result.Status)
            {
                case DlsiteFetchStatus.RateLimited:
                case DlsiteFetchStatus.Timeout:
                case DlsiteFetchStatus.NetworkError:
                case DlsiteFetchStatus.ServerError:
                    return true;
                default:
                    return false;
            }
        }

        private TimeSpan GetRetryDelay(int completedAttempt)
        {
            if (completedAttempt <= 1) return options.FirstRetryDelay;
            return options.SecondRetryDelay;
        }

        private TimeSpan? ParseRetryAfter(HttpResponseMessage response)
        {
            var retry = response.Headers.RetryAfter;
            if (retry == null) return null;
            if (retry.Delta.HasValue && retry.Delta.Value >= TimeSpan.Zero) return retry.Delta.Value;
            if (retry.Date.HasValue)
            {
                var value = retry.Date.Value - clock.UtcNow;
                return value > TimeSpan.Zero ? value : TimeSpan.Zero;
            }
            return null;
        }

        private static DlsiteFetchResult Error(DlsiteFetchStatus fetchStatus, HttpStatusCode status, string source, string resolved, int attempt, string message)
        {
            return new DlsiteFetchResult
            {
                Status = fetchStatus,
                HttpStatusCode = status,
                SourceUrl = source,
                ResolvedUrl = resolved,
                Attempts = attempt,
                ErrorMessage = message
            };
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            fetchGate.Dispose();
            if (ownsClient) client.Dispose();
        }
    }
}

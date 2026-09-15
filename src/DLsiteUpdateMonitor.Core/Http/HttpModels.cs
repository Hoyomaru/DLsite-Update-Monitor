using System;
using System.Net;

namespace DLsiteUpdateMonitor.Core.Http
{
    public enum DlsiteFetchStatus
    {
        Success = 10,
        NetworkError = 20,
        RateLimited = 21,
        AccessDenied = 22,
        Timeout = 23,
        UntrustedRedirect = 24,
        ClientError = 25,
        ProductUnavailable = 30,
        ServerError = 31,
        Cancelled = 40
    }

    public sealed class DlsiteFetchResult
    {
        public DlsiteFetchStatus Status { get; set; }
        public HttpStatusCode? HttpStatusCode { get; set; }
        public string Html { get; set; }
        public string SourceUrl { get; set; }
        public string ResolvedUrl { get; set; }
        public string ErrorMessage { get; set; }
        public int Attempts { get; set; }
        public TimeSpan? RetryAfter { get; set; }

        public bool Success => Status == DlsiteFetchStatus.Success;
    }

    public sealed class DlsiteHttpOptions
    {
        public TimeSpan MinimumRequestInterval { get; set; } = TimeSpan.FromSeconds(2);
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
        public int RetryCount { get; set; } = 2;
        public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromSeconds(2);
        public TimeSpan SecondRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DLsiteUpdateMonitor.Core.Models;

namespace DLsiteUpdateMonitor.Core.Services
{
    public sealed class DlsiteLinkResolver
    {
        private static readonly Regex ProductIdRegex = new Regex(
            @"/product_id/(?<id>[A-Z]{2}(?:\d{6}|\d{8}))(?:\.html)?(?:/|$|[?#])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public LinkResolutionResult Resolve(IEnumerable<string> urls)
        {
            if (urls == null)
            {
                return NoLink();
            }

            var candidates = new List<DlsiteTarget>();
            var sawDlsiteHost = false;
            var sawMalformedDlsiteUrl = false;

            foreach (var raw in urls)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                Uri uri;
                if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out uri))
                {
                    continue;
                }

                if (!IsDlsiteHost(uri.Host))
                {
                    continue;
                }

                sawDlsiteHost = true;
                if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    && !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                {
                    sawMalformedDlsiteUrl = true;
                    continue;
                }

                var match = ProductIdRegex.Match(uri.PathAndQuery);
                if (!match.Success)
                {
                    sawMalformedDlsiteUrl = true;
                    continue;
                }

                candidates.Add(new DlsiteTarget
                {
                    ProductId = match.Groups["id"].Value.ToUpperInvariant(),
                    RegisteredUrl = CanonicalizeHttps(uri)
                });
            }

            var distinct = candidates
                .GroupBy(x => x.ProductId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.ProductId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinct.Count == 1)
            {
                return new LinkResolutionResult
                {
                    Status = LinkResolutionStatus.Resolved,
                    Target = distinct[0],
                    Candidates = distinct
                };
            }

            if (distinct.Count > 1)
            {
                return new LinkResolutionResult
                {
                    Status = LinkResolutionStatus.Ambiguous,
                    Candidates = distinct,
                    Reason = "Multiple different DLsite product IDs are registered for one game."
                };
            }

            if (sawDlsiteHost && sawMalformedDlsiteUrl)
            {
                return new LinkResolutionResult
                {
                    Status = LinkResolutionStatus.Invalid,
                    Candidates = distinct,
                    Reason = "A DLsite URL was found, but it was not a supported HTTP(S) product_id URL."
                };
            }

            return NoLink();
        }

        public static bool IsDlsiteHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            var normalized = host.TrimEnd('.');
            return normalized.Equals("dlsite.com", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".dlsite.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string CanonicalizeHttps(Uri uri)
        {
            if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return uri.AbsoluteUri;
            }

            var builder = new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttps,
                Port = -1
            };
            return builder.Uri.AbsoluteUri;
        }

        private static LinkResolutionResult NoLink()
        {
            return new LinkResolutionResult
            {
                Status = LinkResolutionStatus.NoDlsiteLink,
                Candidates = new List<DlsiteTarget>()
            };
        }
    }
}

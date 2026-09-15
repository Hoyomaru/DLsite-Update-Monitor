using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Parser.Html;
using DLsiteUpdateMonitor.Core.Models;
using DLsiteUpdateMonitor.Core.Services;

namespace DLsiteUpdateMonitor.Core.Parsing
{
    public sealed class DlsitePageParser
    {
        public const int CurrentParserVersion = 1;

        public DlsiteParseResult Parse(string html, string sourceUrl, string resolvedUrl, string expectedProductId, DateTimeOffset fetchedAtUtc)
        {
            var diagnostics = new List<string>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return Error("HTML is empty.", diagnostics);
            }

            var parser = new HtmlParser();
            var document = parser.Parse(html);

            // DLsite can return HTTP 200 with a product-unavailable error box instead of 404/410.
            // Detect that page shape before the normal work-page contract so it is not misreported as
            // a parser regression. This mirrors the behavior used by the maintained DLsite metadata addon.
            var unavailableBox = document.QuerySelector(".error_box_work");
            if (unavailableBox != null)
            {
                var large = CleanText(unavailableBox.QuerySelector(".error_large_text")?.TextContent);
                var detail = CleanText(unavailableBox.QuerySelector(".title_text")?.TextContent);
                var message = string.Join(" : ", new[] { large, detail }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (string.IsNullOrWhiteSpace(message)) message = "DLsite reports that this product is unavailable.";
                diagnostics.Add(message);
                return new DlsiteParseResult
                {
                    Health = ParseHealth.ProductUnavailable,
                    Snapshot = null,
                    Diagnostics = diagnostics
                };
            }

            var title = CleanText(document.QuerySelector("#work_name")?.TextContent);
            var outline = document.QuerySelector("#work_outline");

            if (string.IsNullOrWhiteSpace(title))
            {
                return Error("DLsite work title (#work_name) is missing.", diagnostics);
            }

            if (outline == null)
            {
                return Error("DLsite work outline table is missing.", diagnostics);
            }

            var updateField = ObservedField<string>.Missing();
            ObservedField<long> fileSizeField = ObservedField<long>.Missing();
            var sawUpdateRow = false;
            var sawFileSizeRow = false;

            foreach (var row in outline.QuerySelectorAll("tr"))
            {
                var header = CleanText(row.QuerySelector("th")?.TextContent);
                var cell = row.QuerySelector("td");
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                if (IsUpdateHeader(header))
                {
                    sawUpdateRow = true;
                    var raw = ExtractDirectAndVisibleText(cell);
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        updateField = ObservedField<string>.Unparsed(raw);
                        diagnostics.Add("Update information row exists but contains no usable text.");
                    }
                    else
                    {
                        var normalized = UpdateInfoNormalizer.Normalize(raw);
                        updateField = string.IsNullOrWhiteSpace(normalized)
                            ? ObservedField<string>.Unparsed(raw)
                            : ObservedField<string>.Parsed(raw, normalized, normalized);
                    }
                    continue;
                }

                if (IsFileSizeHeader(header))
                {
                    sawFileSizeRow = true;
                    var raw = CleanText(cell?.TextContent);
                    var parsed = FileSizeNormalizer.Parse(raw);
                    fileSizeField = parsed.Success
                        ? ObservedField<long>.Parsed(raw, parsed.Normalized, parsed.Bytes)
                        : ObservedField<long>.Unparsed(raw);
                    if (!parsed.Success)
                    {
                        diagnostics.Add("File size row exists but could not be parsed.");
                    }
                }
            }

            if (!sawUpdateRow)
            {
                updateField = ObservedField<string>.Missing();
            }

            if (!sawFileSizeRow)
            {
                diagnostics.Add("File size row is missing.");
            }

            var actualProductId = !string.IsNullOrWhiteSpace(resolvedUrl)
                ? ExtractProductId(resolvedUrl)
                : (ExtractProductId(sourceUrl) ?? expectedProductId);
            var snapshot = new RemoteSnapshot
            {
                ParserVersion = CurrentParserVersion,
                ProductId = actualProductId,
                SourceUrl = sourceUrl,
                ResolvedUrl = resolvedUrl,
                WorkName = title,
                FetchedAtUtc = fetchedAtUtc,
                UpdateInfo = updateField,
                FileSize = fileSizeField
            };
            snapshot.Fingerprint = SnapshotFingerprint.Compute(snapshot);

            var validator = new SnapshotValidator();
            var validation = validator.Validate(snapshot);
            if (!validation.IsComparisonEligible)
            {
                diagnostics.Add(validation.Reason);
                return new DlsiteParseResult
                {
                    Health = ParseHealth.Degraded,
                    Snapshot = snapshot,
                    Diagnostics = diagnostics
                };
            }

            return new DlsiteParseResult
            {
                Health = ParseHealth.Healthy,
                Snapshot = snapshot,
                Diagnostics = diagnostics
            };
        }

        private static bool IsUpdateHeader(string header)
        {
            return header.IndexOf("更新情報", StringComparison.OrdinalIgnoreCase) >= 0
                || header.IndexOf("Update information", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsFileSizeHeader(string header)
        {
            return header.IndexOf("ファイル容量", StringComparison.OrdinalIgnoreCase) >= 0
                || header.IndexOf("File size", StringComparison.OrdinalIgnoreCase) >= 0
                || header.IndexOf("Filesize", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractDirectAndVisibleText(IElement element)
        {
            if (element == null) return null;

            // Prefer direct text nodes. This mirrors the current DLsite metadata scraper and avoids
            // unrelated nested labels/links becoming update signals. If the page changes to nested-only
            // content, fall back to TextContent rather than silently treating the row as missing.
            var direct = element.ChildNodes
                .Where(node => node.NodeType == NodeType.Text)
                .Select(node => CleanText(node.TextContent))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
            if (direct.Count > 0) return string.Join(" ", direct);
            return CleanText(element.TextContent);
        }

        private static string ExtractProductId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var result = new DlsiteLinkResolver().Resolve(new[] { url });
            return result.Status == LinkResolutionStatus.Resolved ? result.Target.ProductId : null;
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Trim();
        }

        private static DlsiteParseResult Error(string message, List<string> diagnostics)
        {
            diagnostics.Add(message);
            return new DlsiteParseResult
            {
                Health = ParseHealth.Error,
                Snapshot = null,
                Diagnostics = diagnostics
            };
        }
    }
}

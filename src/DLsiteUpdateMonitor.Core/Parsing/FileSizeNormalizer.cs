using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DLsiteUpdateMonitor.Core.Parsing
{
    public sealed class FileSizeParseResult
    {
        public bool Success { get; set; }
        public long Bytes { get; set; }
        public string Normalized { get; set; }
    }

    public static class FileSizeNormalizer
    {
        private static readonly Regex SizePattern = new Regex(
            @"(?<number>\d+(?:\.\d+)?)\s*(?<unit>B|KB|MB|GB|KIB|MIB|GIB)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static FileSizeParseResult Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Failed();
            }

            var text = raw.Normalize(NormalizationForm.FormKC)
                .Replace(",", string.Empty)
                .Trim();

            var matches = SizePattern.Matches(text);
            if (matches.Count == 0)
            {
                return Failed();
            }

            var values = new List<long>();
            foreach (Match match in matches)
            {
                long bytes;
                if (!TryConvertToBytes(match, out bytes))
                {
                    return Failed();
                }
                values.Add(bytes);
            }

            // A cell can contain the same size in two units (for example "1 GB / 1024 MB").
            // That is still unambiguous. Conflicting sizes, however, may describe multiple
            // download variants and must not be silently reduced to the first number.
            var distinct = values.Distinct().ToList();
            if (distinct.Count != 1)
            {
                return Failed();
            }

            return new FileSizeParseResult
            {
                Success = true,
                Bytes = distinct[0],
                Normalized = distinct[0].ToString(CultureInfo.InvariantCulture) + " B"
            };
        }

        private static bool TryConvertToBytes(Match match, out long bytes)
        {
            bytes = 0;
            decimal number;
            if (!decimal.TryParse(match.Groups["number"].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out number) || number < 0)
            {
                return false;
            }

            var unit = match.Groups["unit"].Value.ToUpperInvariant();
            decimal multiplier;
            switch (unit)
            {
                case "B": multiplier = 1m; break;
                case "KB":
                case "KIB": multiplier = 1024m; break;
                case "MB":
                case "MIB": multiplier = 1024m * 1024m; break;
                case "GB":
                case "GIB": multiplier = 1024m * 1024m * 1024m; break;
                default: return false;
            }

            try
            {
                var bytesDecimal = decimal.Round(number * multiplier, 0, MidpointRounding.AwayFromZero);
                if (bytesDecimal > long.MaxValue)
                {
                    return false;
                }

                bytes = (long)bytesDecimal;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static FileSizeParseResult Failed()
        {
            return new FileSizeParseResult { Success = false };
        }
    }
}

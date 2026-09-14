using System;
using System.Globalization;
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

            var match = SizePattern.Match(text);
            if (!match.Success)
            {
                return Failed();
            }

            decimal number;
            if (!decimal.TryParse(match.Groups["number"].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out number) || number < 0)
            {
                return Failed();
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
                default: return Failed();
            }

            try
            {
                var bytesDecimal = decimal.Round(number * multiplier, 0, MidpointRounding.AwayFromZero);
                if (bytesDecimal > long.MaxValue)
                {
                    return Failed();
                }

                var bytes = (long)bytesDecimal;
                return new FileSizeParseResult
                {
                    Success = true,
                    Bytes = bytes,
                    Normalized = bytes.ToString(CultureInfo.InvariantCulture) + " B"
                };
            }
            catch (OverflowException)
            {
                return Failed();
            }
        }

        private static FileSizeParseResult Failed()
        {
            return new FileSizeParseResult { Success = false };
        }
    }
}

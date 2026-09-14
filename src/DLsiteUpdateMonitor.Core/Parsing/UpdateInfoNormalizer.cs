using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DLsiteUpdateMonitor.Core.Parsing
{
    public static class UpdateInfoNormalizer
    {
        private static readonly Regex JapaneseDate = new Regex(
            @"(?<!\d)(?<y>20\d{2})\s*年\s*(?<m>\d{1,2})\s*月\s*(?<d>\d{1,2})\s*日",
            RegexOptions.Compiled);

        private static readonly Regex SeparatedDate = new Regex(
            @"(?<!\d)(?<y>20\d{2})\s*[/\-.]\s*(?<m>\d{1,2})\s*[/\-.]\s*(?<d>\d{1,2})(?!\d)",
            RegexOptions.Compiled);

        private static readonly Regex HorizontalWhitespace = new Regex(@"[\t\u00A0\u3000 ]+", RegexOptions.Compiled);
        private static readonly Regex TooManyNewlines = new Regex(@"\n{3,}", RegexOptions.Compiled);

        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var text = raw.Normalize(NormalizationForm.FormKC)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

            text = JapaneseDate.Replace(text, NormalizeDateMatch);
            text = SeparatedDate.Replace(text, NormalizeDateMatch);

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = HorizontalWhitespace.Replace(lines[i], " ").Trim();
            }

            text = string.Join("\n", lines).Trim();
            return TooManyNewlines.Replace(text, "\n\n");
        }

        private static string NormalizeDateMatch(Match match)
        {
            int year;
            int month;
            int day;
            if (!int.TryParse(match.Groups["y"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out year)
                || !int.TryParse(match.Groups["m"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out month)
                || !int.TryParse(match.Groups["d"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out day))
            {
                return match.Value;
            }

            try
            {
                return new DateTime(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return match.Value;
            }
        }
    }
}

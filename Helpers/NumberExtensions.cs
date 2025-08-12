using System;
using System.Globalization;

#nullable enable

namespace F95ZoneMetadataProvider
{
    public static class NumberExtensions
    {
        private static readonly string[] Suffix = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; // Longs run out around EB  (lol?)

        /// <summary>
        /// Converts the specified byte count into a human-readable file size string,
        /// choosing the appropriate suffix (B, KB, MB, etc.) and formatting to one decimal place.
        /// </summary>
        /// <param name="byteCount">The number of bytes to convert.</param>
        /// <returns>
        /// A formatted file size string, including one decimal place and the appropriate unit suffix.
        /// Returns "0B" if <paramref name="byteCount"/> is zero.
        /// </returns>
        public static string ToFileSizeString(this long byteCount)
        {
            if (byteCount == 0)
                return "0" + Suffix[0];

            var bytes = Math.Abs(byteCount);
            var place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            var num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return Math.Sign(byteCount) * num + Suffix[place];
        }

        /// <summary>
        /// Attempts to parse the specified string as a double-precision floating-point number
        /// using invariant culture formatting.
        /// </summary>
        /// <param name="s">The string to convert. Can be <c>null</c>.</param>
        /// <param name="result">
        /// When this method returns, contains the parsed <see cref="double"/> value if
        /// the conversion succeeded; otherwise, <see cref="double.NaN"/>.
        /// </param>
        /// <returns>
        /// <c>true</c> if <paramref name="s"/> is not <c>null</c> and the parse operation succeeded;
        /// otherwise, <c>false</c>.
        /// </returns>
        public static bool TryParse(string? s, out double result)
        {
            result = double.NaN;
            return s is not null &&
                   double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }
}
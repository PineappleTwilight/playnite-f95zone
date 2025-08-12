using System;
using System.Globalization;
using System.Windows.Data;

namespace F95ZoneMetadataProvider
{
    public class IntToStringConverter : IValueConverter
    {
        /// <summary>
        /// Converts an integer value to its string representation, or returns "-1" if the conversion is not possible.
        /// </summary>
        /// <param name="value">The value to convert. Expected to be an integer.</param>
        /// <param name="targetType">The target type of the conversion. Must be <see cref="string"/>.</param>
        /// <param name="parameter">An optional parameter that is not used in this implementation.</param>
        /// <param name="culture">The culture to use during the conversion. This parameter is not used in this implementation.</param>
        /// <returns>A string representation of the integer value if <paramref name="value"/> is an integer and <paramref
        /// name="targetType"/> is <see cref="string"/>; otherwise, "-1".</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(string)) return "-1";
            return value is not int number ? "-1" : number.ToString();
        }

        /// <summary>
        /// Converts the provided <paramref name="value"/> back to an integer if possible; returns -1 otherwise.
        /// </summary>
        /// <param name="value">The value to convert, expected to be a string.</param>
        /// <param name="targetType">The desired target type; conversion proceeds only if this is <see cref="int"/>.</param>
        /// <param name="parameter">An optional parameter (not used).</param>
        /// <param name="culture">The culture information to use when parsing the string.</param>
        /// <returns>
        /// The parsed integer when <paramref name="value"/> is a valid integer string and <paramref name="targetType"/> is <see cref="int"/>; otherwise -1.
        /// </returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(int)) return -1;
            if (value is not string s) return -1;
            if (int.TryParse(s, out var result)) return result;
            return -1;
        }
    }
}
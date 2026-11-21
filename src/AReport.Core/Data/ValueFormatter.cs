using System.Globalization;

namespace AReport.Data;

/// <summary>The single place where values become text (used by templates and table cells).</summary>
public static class ValueFormatter
{
    public static string Format(object? value, string? format, CultureInfo culture)
    {
        if (value is null) return "";

        if (value is IFormattable formattable)
        {
            if (!string.IsNullOrEmpty(format))
            {
                try
                {
                    return formattable.ToString(format, culture);
                }
                catch (FormatException)
                {
                    // fall through to the default representation
                }
            }
            return formattable.ToString(null, culture);
        }

        return Convert.ToString(value, culture) ?? "";
    }
}

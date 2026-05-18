using System;
using System.Globalization;
using System.Windows.Data;

namespace Flow.Converters;

public class HmsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double d) return "00:00:00";
        return Format(d);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return ParseAsSeconds((value as string ?? "").Trim());
    }

    public static string Format(double totalSeconds)
    {
        long s = (long)Math.Floor(Math.Abs(totalSeconds));
        long h = s / 3600;
        long m = (s % 3600) / 60;
        long sec = s % 60;
        return $"{h:00}:{m:00}:{sec:00}";
    }

    private static double ParseAsSeconds(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var parts = text.Split(':');
        if (parts.Length == 3 &&
            TryParseDouble(parts[0], out double h) &&
            TryParseDouble(parts[1], out double m) &&
            TryParseDouble(parts[2], out double s))
            return h * 3600 + m * 60 + s;

        if (parts.Length == 2 &&
            TryParseDouble(parts[0], out h) &&
            TryParseDouble(parts[1], out m))
            return h * 3600 + m * 60;

        if (TryParseDouble(text, out double v))
            return v;

        return 0;
    }

    private static bool TryParseDouble(string s, out double result) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
}

using System.Windows.Media;

namespace Flow.Services;

public static class ColorBrushService
{
    public static Brush CreateFrozenBrush(string? colorText, Brush? fallback = null)
    {
        if (!TryParseColor(colorText, out var color))
            return fallback ?? Brushes.Transparent;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Color ParseColorOrTransparent(string? colorText) =>
        TryParseColor(colorText, out var color) ? color : Colors.Transparent;

    public static Brush CreateReadableTextBrush(Color backgroundColor)
    {
        double luminance = (0.299 * backgroundColor.R + 0.587 * backgroundColor.G + 0.114 * backgroundColor.B) / 255.0;
        return luminance > 0.62
            ? CreateFrozenBrush("#202124", Brushes.Black)
            : Brushes.White;
    }

    private static bool TryParseColor(string? colorText, out Color color)
    {
        if (string.IsNullOrWhiteSpace(colorText))
        {
            color = Colors.Transparent;
            return false;
        }

        try
        {
            color = (Color)ColorConverter.ConvertFromString(colorText);
            return true;
        }
        catch
        {
            color = Colors.Transparent;
            return false;
        }
    }
}

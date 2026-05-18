using System;
using System.Windows;
using System.Windows.Media;

namespace Flow.Services;

public static class ThemeService
{
    public const string LightThemeKey = "Light";
    public const string DarkThemeKey = "Dark";
    public const string DefaultAccentColor = "#4285F4";

    public static event EventHandler? ThemeChanged;

    public static ThemePalette CurrentPalette { get; private set; } = CreatePalette(LightThemeKey, DefaultAccentColor);

    public static void ApplyTheme(string? themeKey, string? accentColor)
    {
        string normalizedTheme = NormalizeThemeKey(themeKey);
        string normalizedAccent = NormalizeAccentColor(accentColor);

        var palette = CreatePalette(normalizedTheme, normalizedAccent);
        CurrentPalette = palette;

        var resources = Application.Current?.Resources;
        if (resources != null)
            ApplyResources(resources, palette);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string NormalizeThemeKey(string? themeKey) =>
        string.Equals(themeKey, DarkThemeKey, StringComparison.OrdinalIgnoreCase)
            ? DarkThemeKey
            : LightThemeKey;

    public static string NormalizeAccentColor(string? accentColor)
    {
        if (string.IsNullOrWhiteSpace(accentColor))
            return DefaultAccentColor;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(accentColor);
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch
        {
            return DefaultAccentColor;
        }
    }

    private static void ApplyResources(ResourceDictionary resources, ThemePalette palette)
    {
        Set(resources, "AppWindowBackgroundBrush", palette.WindowBackground);
        Set(resources, "AppSurfaceBrush", palette.Surface);
        Set(resources, "AppSurfaceAltBrush", palette.SurfaceAlt);
        Set(resources, "AppSurfaceMutedBrush", palette.SurfaceMuted);
        Set(resources, "AppBorderBrush", palette.Border);
        Set(resources, "AppBorderSoftBrush", palette.BorderSoft);
        Set(resources, "AppBorderStrongBrush", palette.BorderStrong);
        Set(resources, "AppTextPrimaryBrush", palette.TextPrimary);
        Set(resources, "AppTextSecondaryBrush", palette.TextSecondary);
        Set(resources, "AppTextMutedBrush", palette.TextMuted);
        Set(resources, "AppInverseTextBrush", palette.InverseText);
        Set(resources, "AppAccentBrush", palette.Accent);
        Set(resources, "AppAccentHoverBrush", palette.AccentHover);
        Set(resources, "AppAccentPressedBrush", palette.AccentPressed);
        Set(resources, "AppAccentStrongBrush", palette.AccentStrong);
        Set(resources, "AppAccentOutlineBrush", palette.AccentOutline);
        Set(resources, "AppAccentSubtleBrush", palette.AccentSubtle);
        Set(resources, "AppAccentSubtleStrongBrush", palette.AccentSubtleStrong);
        Set(resources, "AppAccentGhostBrush", palette.AccentGhost);
        Set(resources, "AppAccentFaintBrush", palette.AccentFaint);
        Set(resources, "AppAccentTextBrush", palette.AccentText);
        Set(resources, "AppInfoBrush", palette.Info);
        Set(resources, "AppInfoSurfaceBrush", palette.InfoSurface);
        Set(resources, "AppSuccessBrush", palette.Success);
        Set(resources, "AppSuccessSurfaceBrush", palette.SuccessSurface);
        Set(resources, "AppNeutralBrush", palette.Neutral);
        Set(resources, "AppNeutralSurfaceBrush", palette.NeutralSurface);
        Set(resources, "AppDangerBrush", palette.Danger);
        Set(resources, "AppDangerSurfaceBrush", palette.DangerSurface);
        Set(resources, "AppDangerSoftBrush", palette.DangerSoft);
        Set(resources, "AppWarningBrush", palette.Warning);
        Set(resources, "AppWarningTextBrush", palette.WarningText);
        Set(resources, "AppWarningSurfaceBrush", palette.WarningSurface);
        Set(resources, "AppWarningBorderBrush", palette.WarningBorder);
        Set(resources, "AppActivityBarBackgroundBrush", palette.ActivityBarBackground);
        Set(resources, "AppActivityBarBorderBrush", palette.ActivityBarBorder);
        Set(resources, "AppActivityBarHoverBrush", palette.ActivityBarHover);
        Set(resources, "AppActivityBarSelectedBackgroundBrush", palette.ActivityBarSelectedBackground);
        Set(resources, "AppActivityBarIconBrush", palette.ActivityBarIcon);
        Set(resources, "AppActivityBarIconHoverBrush", palette.ActivityBarIconHover);
        Set(resources, "AppToastBackgroundBrush", palette.ToastBackground);
        resources["AppPopupShadowColor"] = palette.PopupShadowColor;
    }

    private static void Set(ResourceDictionary resources, string key, Brush value) => resources[key] = value;

    private static ThemePalette CreatePalette(string themeKey, string accentHex)
    {
        bool isDark = string.Equals(themeKey, DarkThemeKey, StringComparison.Ordinal);
        Color accent = (Color)ColorConverter.ConvertFromString(accentHex);

        Color window = isDark ? FromHex("#1A1D23") : FromHex("#F0F2F5");
        Color surface = isDark ? FromHex("#232731") : FromHex("#FFFFFF");
        Color surfaceAlt = isDark ? FromHex("#282D38") : FromHex("#F8F9FA");
        Color surfaceMuted = isDark ? FromHex("#2E3440") : FromHex("#F2F4F8");
        Color border = isDark ? FromHex("#3B4252") : FromHex("#DDE3EE");
        Color borderSoft = isDark ? FromHex("#333947") : FromHex("#E8EAED");
        Color borderStrong = isDark ? FromHex("#4B5364") : FromHex("#C8CCD4");
        Color textPrimary = isDark ? FromHex("#F5F7FA") : FromHex("#202124");
        Color textSecondary = isDark ? FromHex("#C3CAD5") : FromHex("#5F6368");
        Color textMuted = isDark ? FromHex("#90A0B2") : FromHex("#9AA0A6");
        Color inverseText = isDark ? FromHex("#F5F7FA") : Colors.White;

        Color success = isDark ? FromHex("#63D39B") : FromHex("#2E7D32");
        Color danger = isDark ? FromHex("#FF9A9A") : FromHex("#C62828");
        Color warning = isDark ? FromHex("#FFD27A") : FromHex("#F57F17");

        return new ThemePalette
        {
            IsDark = isDark,
            WindowBackground = Brush(window),
            Surface = Brush(surface),
            SurfaceAlt = Brush(surfaceAlt),
            SurfaceMuted = Brush(surfaceMuted),
            Border = Brush(border),
            BorderSoft = Brush(borderSoft),
            BorderStrong = Brush(borderStrong),
            TextPrimary = Brush(textPrimary),
            TextSecondary = Brush(textSecondary),
            TextMuted = Brush(textMuted),
            InverseText = Brush(inverseText),
            Accent = Brush(accent),
            AccentHover = Brush(Darken(accent, isDark ? 0.06 : 0.10)),
            AccentPressed = Brush(Darken(accent, isDark ? 0.16 : 0.20)),
            AccentStrong = Brush(Darken(accent, isDark ? 0.10 : 0.14)),
            AccentOutline = Brush(Mix(accent, borderStrong, isDark ? 0.52 : 0.38)),
            AccentSubtle = Brush(Mix(accent, surfaceAlt, isDark ? 0.24 : 0.14)),
            AccentSubtleStrong = Brush(Mix(accent, surfaceAlt, isDark ? 0.36 : 0.22)),
            AccentGhost = Brush(WithAlpha(accent, isDark ? (byte)120 : (byte)100)),
            AccentFaint = Brush(WithAlpha(accent, isDark ? (byte)30 : (byte)15)),
            AccentText = Brush(GetContrastText(accent)),
            Info = Brush(accent),
            InfoSurface = Brush(Mix(accent, surface, isDark ? 0.20 : 0.13)),
            Success = Brush(success),
            SuccessSurface = Brush(Mix(success, surface, isDark ? 0.18 : 0.12)),
            Neutral = Brush(textSecondary),
            NeutralSurface = Brush(Mix(textMuted, surface, isDark ? 0.18 : 0.10)),
            Danger = Brush(danger),
            DangerSurface = Brush(Mix(danger, surface, isDark ? 0.18 : 0.12)),
            DangerSoft = Brush(Mix(danger, surface, isDark ? 0.46 : 0.35)),
            Warning = Brush(warning),
            WarningText = Brush(isDark ? warning : FromHex("#E65100")),
            WarningSurface = Brush(Mix(warning, surface, isDark ? 0.18 : 0.14)),
            WarningBorder = Brush(Mix(warning, border, isDark ? 0.45 : 0.32)),
            ActivityBarBackground = Brush(isDark ? FromHex("#111318") : FromHex("#252526")),
            ActivityBarBorder = Brush(isDark ? FromHex("#0D0F13") : FromHex("#1A1A1A")),
            ActivityBarHover = Brush(isDark ? FromHex("#1D222B") : FromHex("#3C3C3C")),
            ActivityBarSelectedBackground = Brush(isDark ? FromHex("#27303B") : FromHex("#37373D")),
            ActivityBarIcon = Brush(isDark ? FromHex("#8B95A7") : FromHex("#858585")),
            ActivityBarIconHover = Brush(isDark ? FromHex("#F5F7FA") : FromHex("#CCCCCC")),
            ToastBackground = Brush(isDark ? FromHex("#13161B") : FromHex("#323232")),
            PopupShadowColor = isDark ? FromHex("#000000") : FromHex("#000000"),
        };
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color FromHex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color Darken(Color color, double amount) => Mix(Colors.Black, color, amount);

    private static Color Mix(Color overlay, Color baseColor, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte a = (byte)Math.Round(baseColor.A + (overlay.A - baseColor.A) * amount);
        byte r = (byte)Math.Round(baseColor.R + (overlay.R - baseColor.R) * amount);
        byte g = (byte)Math.Round(baseColor.G + (overlay.G - baseColor.G) * amount);
        byte b = (byte)Math.Round(baseColor.B + (overlay.B - baseColor.B) * amount);
        return Color.FromArgb(a, r, g, b);
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color GetContrastText(Color background)
    {
        double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255d;
        return luminance > 0.62 ? FromHex("#202124") : Colors.White;
    }
}

public sealed class ThemePalette
{
    public bool IsDark { get; init; }
    public Brush WindowBackground { get; init; } = null!;
    public Brush Surface { get; init; } = null!;
    public Brush SurfaceAlt { get; init; } = null!;
    public Brush SurfaceMuted { get; init; } = null!;
    public Brush Border { get; init; } = null!;
    public Brush BorderSoft { get; init; } = null!;
    public Brush BorderStrong { get; init; } = null!;
    public Brush TextPrimary { get; init; } = null!;
    public Brush TextSecondary { get; init; } = null!;
    public Brush TextMuted { get; init; } = null!;
    public Brush InverseText { get; init; } = null!;
    public Brush Accent { get; init; } = null!;
    public Brush AccentHover { get; init; } = null!;
    public Brush AccentPressed { get; init; } = null!;
    public Brush AccentStrong { get; init; } = null!;
    public Brush AccentOutline { get; init; } = null!;
    public Brush AccentSubtle { get; init; } = null!;
    public Brush AccentSubtleStrong { get; init; } = null!;
    public Brush AccentGhost { get; init; } = null!;
    public Brush AccentFaint { get; init; } = null!;
    public Brush AccentText { get; init; } = null!;
    public Brush Info { get; init; } = null!;
    public Brush InfoSurface { get; init; } = null!;
    public Brush Success { get; init; } = null!;
    public Brush SuccessSurface { get; init; } = null!;
    public Brush Neutral { get; init; } = null!;
    public Brush NeutralSurface { get; init; } = null!;
    public Brush Danger { get; init; } = null!;
    public Brush DangerSurface { get; init; } = null!;
    public Brush DangerSoft { get; init; } = null!;
    public Brush Warning { get; init; } = null!;
    public Brush WarningText { get; init; } = null!;
    public Brush WarningSurface { get; init; } = null!;
    public Brush WarningBorder { get; init; } = null!;
    public Brush ActivityBarBackground { get; init; } = null!;
    public Brush ActivityBarBorder { get; init; } = null!;
    public Brush ActivityBarHover { get; init; } = null!;
    public Brush ActivityBarSelectedBackground { get; init; } = null!;
    public Brush ActivityBarIcon { get; init; } = null!;
    public Brush ActivityBarIconHover { get; init; } = null!;
    public Brush ToastBackground { get; init; } = null!;
    public Color PopupShadowColor { get; init; }
}

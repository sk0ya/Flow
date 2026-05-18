using System.Collections.Generic;
using Flow.Services;

namespace Flow.Models;

public class AppState
{
    public string? LastProjectPath { get; set; }
    public List<string> RecentProjectPaths { get; set; } = new();
    public string ThemeKey { get; set; } = ThemeService.LightThemeKey;
    public string AccentColor { get; set; } = ThemeService.DefaultAccentColor;
}

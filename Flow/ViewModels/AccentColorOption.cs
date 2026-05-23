using System.Windows.Media;
using Flow.Services;

namespace Flow.ViewModels;

public sealed class AccentColorOption
{
    public AccentColorOption(string name, string colorHex)
    {
        Name = name;
        ColorHex = colorHex;
        SwatchBrush = CreateBrush(colorHex);
    }

    public string Name { get; }
    public string ColorHex { get; }
    public Brush SwatchBrush { get; }

    private static Brush CreateBrush(string colorHex) =>
        ColorBrushService.CreateFrozenBrush(colorHex);
}

using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Flow.Models;

namespace Flow.ViewModels;

public partial class CategoryViewModel : ObservableObject
{
    private Brush _brush;

    public static readonly CategoryViewModel None = new(Guid.Empty, "（なし）", "");

    public CategoryViewModel()
    {
        _id = Guid.NewGuid();
        _brush = Brushes.Transparent;
    }

    public CategoryViewModel(Guid id, string name, string color)
    {
        _id    = id;
        _name  = name;
        _color = color;
        _brush = CreateBrush(color);
    }

    public CategoryViewModel(ProjectCategory model)
        : this(model.Id, model.Name, model.Color) { }

    [ObservableProperty] private Guid   _id;
    [ObservableProperty] private string _name  = "";
    [ObservableProperty] private string _color = "#94A3B8";

    public bool IsNone => Id == Guid.Empty;
    public Brush Brush => _brush;

    public Color ColorValue =>
        string.IsNullOrEmpty(Color) ? Colors.Transparent
        : TryParseColor(Color, out var c) ? c : Colors.Transparent;

    partial void OnColorChanged(string value)
    {
        _brush = CreateBrush(value);
        OnPropertyChanged(nameof(Brush));
        OnPropertyChanged(nameof(ColorValue));
    }

    public ProjectCategory ToModel() => new() { Id = Id, Name = Name, Color = Color };

    private static Brush CreateBrush(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || !TryParseColor(hex, out var color))
            return Brushes.Transparent;
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        try { color = (Color)ColorConverter.ConvertFromString(hex); return true; }
        catch { color = Colors.Transparent; return false; }
    }
}

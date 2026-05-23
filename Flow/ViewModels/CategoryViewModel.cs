using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Flow.Models;
using Flow.Services;

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
        ColorBrushService.ParseColorOrTransparent(Color);

    partial void OnColorChanged(string value)
    {
        _brush = CreateBrush(value);
        OnPropertyChanged(nameof(Brush));
        OnPropertyChanged(nameof(ColorValue));
    }

    public ProjectCategory ToModel() => new() { Id = Id, Name = Name, Color = Color };

    private static Brush CreateBrush(string? hex) =>
        ColorBrushService.CreateFrozenBrush(hex);
}

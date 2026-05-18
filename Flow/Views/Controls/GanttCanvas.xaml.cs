using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Flow.Converters;
using Flow.Services;
using Flow.ViewModels;

namespace Flow.Views.Controls;

public partial class GanttCanvas : UserControl
{
    // ── Dependency Properties ─────────────────────────────────────────────

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ItemViewModel>),
            typeof(GanttCanvas), new PropertyMetadata(null, OnAnyChanged));
    public static readonly DependencyProperty EdgesProperty =
        DependencyProperty.Register(nameof(Edges), typeof(IEnumerable<DependencyEdge>),
            typeof(GanttCanvas), new PropertyMetadata(null, OnAnyChanged));
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(ItemViewModel),
            typeof(GanttCanvas), new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((GanttCanvas)d).Render()));
    public static readonly DependencyProperty LanesProperty =
        DependencyProperty.Register(nameof(Lanes), typeof(IEnumerable<LaneViewModel>),
            typeof(GanttCanvas), new PropertyMetadata(null, OnAnyChanged));
    public static readonly DependencyProperty TimeUnitProperty =
        DependencyProperty.Register(nameof(TimeUnit), typeof(string),
            typeof(GanttCanvas), new PropertyMetadata("日", (d, _) => ((GanttCanvas)d).Render()));
    public static readonly DependencyProperty TotalDurationProperty =
        DependencyProperty.Register(nameof(TotalDuration), typeof(double),
            typeof(GanttCanvas), new PropertyMetadata(10.0, (d, _) => ((GanttCanvas)d).Render()));
    public static readonly DependencyProperty CellDurationProperty =
        DependencyProperty.Register(nameof(CellDuration), typeof(double),
            typeof(GanttCanvas), new PropertyMetadata(1.0, (d, _) => ((GanttCanvas)d).Render()));
    public static readonly DependencyProperty PixelsPerUnitProperty =
        DependencyProperty.Register(nameof(PixelsPerUnit), typeof(double),
            typeof(GanttCanvas), new PropertyMetadata(80.0, (d, _) => ((GanttCanvas)d).Render()));

    public IEnumerable<ItemViewModel>? ItemsSource
    { get => (IEnumerable<ItemViewModel>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IEnumerable<DependencyEdge>? Edges
    { get => (IEnumerable<DependencyEdge>?)GetValue(EdgesProperty); set => SetValue(EdgesProperty, value); }
    public ItemViewModel? SelectedItem
    { get => (ItemViewModel?)GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public IEnumerable<LaneViewModel>? Lanes
    { get => (IEnumerable<LaneViewModel>?)GetValue(LanesProperty); set => SetValue(LanesProperty, value); }
    public string TimeUnit
    { get => (string)GetValue(TimeUnitProperty); set => SetValue(TimeUnitProperty, value); }
    public double TotalDuration
    { get => (double)GetValue(TotalDurationProperty); set => SetValue(TotalDurationProperty, value); }
    public double CellDuration
    { get => (double)GetValue(CellDurationProperty); set => SetValue(CellDurationProperty, value); }
    public double PixelsPerUnit
    { get => (double)GetValue(PixelsPerUnitProperty); set => SetValue(PixelsPerUnitProperty, value); }

    // ── Layout constants ──────────────────────────────────────────────────
    private const double LaneHeaderW  = 150;
    private const double LaneH        = 36;
    private const double BarH         = 28;
    private const double TimeHeaderH  = 30;
    private const double TimeTickLabelOffset = 4;
    private const double TimeTickLabelWidth  = 36;
    private const double ResizeW      = 8;
    private const double MinBarW      = 4;
    private const double AddLaneZoneH = 32;
    private const int    WmMouseHWheel = 0x020E;
    private const int    WheelDelta    = 120;

    // ── Drag state ────────────────────────────────────────────────────────
    private ItemViewModel? _dragItem;
    private enum DragMode { None, Move, Resize, LaneReorder }
    private DragMode _drag = DragMode.None;
    private double _dragOriginStart;
    private double _dragOriginDuration;
    private double _dragMouseOffsetX;
    private int    _dragLaneIdx;
    private bool   _dragToNewLane;

    // Lane reorder state
    private int _reorderSourceLane = -1;
    private int _reorderDropLane   = -1;

    // Lane rename state
    private LaneViewModel? _renamingLane;
    private TextBox?       _renameBox;
    private HwndSource?    _hwndSource;

    // Task rename state
    private ItemViewModel? _renamingItem;
    private TextBox?       _taskRenameBox;
    private bool           _discardRenamingItemOnCancel;

    // Callbacks set in code-behind
    public Func<Guid>?       AddLaneFunc          { get; set; }
    public Func<Guid, double, ItemViewModel?>? AddItemAtFunc { get; set; }
    public Action<ItemViewModel>? DiscardItemFunc { get; set; }
    public Action<int, int>? ReorderLanesCallback { get; set; }

    // ── Cached rects ──────────────────────────────────────────────────────
    private readonly Dictionary<Guid, Rect> _barRects = new();
    private bool IsRenaming => _renamingLane != null || _renamingItem != null;

    public GanttCanvas()
    {
        InitializeComponent();
        ThemeService.ThemeChanged += OnThemeChanged;

        PreviewMouseLeftButtonDown            += OnPreviewGanttMouseDown;
        RootCanvas.PreviewMouseLeftButtonDown += OnMouseDown;
        RootCanvas.MouseMove                  += OnMouseMove;
        RootCanvas.MouseLeftButtonUp          += OnMouseUp;

        FrozenLaneCanvas.PreviewMouseLeftButtonDown += OnFrozenLaneMouseDown;
        FrozenLaneCanvas.MouseMove                  += OnFrozenLaneMouseMove;
        FrozenLaneCanvas.MouseLeftButtonUp          += OnFrozenLaneMouseUp;
        FrozenLaneCanvas.PreviewMouseWheel         += OnFrozenLayerMouseWheel;
        FrozenTimeHeaderCanvas.PreviewMouseWheel   += OnFrozenLayerMouseWheel;
        FrozenCornerHeader.PreviewMouseWheel       += OnFrozenLayerMouseWheel;

        TimelineScrollViewer.ScrollChanged += OnTimelineScrollChanged;
        PreviewMouseWheel                  += OnPreviewMouseWheel;
        Loaded                             += OnLoaded;
        Unloaded                           += OnUnloaded;
        SizeChanged                        += (_, _) => RenderFrozenLayers();
    }

    // ── Collection subscription ───────────────────────────────────────────

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (GanttCanvas)d;
        if (e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= ctrl.OnColl;
        if (e.NewValue is INotifyCollectionChanged nw)  nw.CollectionChanged  += ctrl.OnColl;
        ctrl.Render();
    }

    private void OnColl(object? s, NotifyCollectionChangedEventArgs e) => Render();

    private void OnTimelineScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_renamingLane != null) return;
        if (e.HorizontalChange == 0 && e.VerticalChange == 0 &&
            e.ViewportWidthChange == 0 && e.ViewportHeightChange == 0) return;
        RenderFrozenLayers();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachWindowHook();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachWindowHook();
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Render();

    private void AttachWindowHook()
    {
        var source = PresentationSource.FromVisual(this) as HwndSource;
        if (source == null || ReferenceEquals(source, _hwndSource)) return;

        DetachWindowHook();
        _hwndSource = source;
        _hwndSource.AddHook(WndProc);
    }

    private void DetachWindowHook()
    {
        if (_hwndSource == null) return;
        _hwndSource.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmMouseHWheel || !IsMouseOver || _drag != DragMode.None)
            return IntPtr.Zero;

        short delta = GetWheelDelta(wParam);
        if (delta == 0) return IntPtr.Zero;

        handled = ScrollHorizontallyBy(delta / (double)WheelDelta * GetHorizontalWheelStep());
        return IntPtr.Zero;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || _drag != DragMode.None) return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;

        if (ScrollHorizontallyBy(-e.Delta / (double)WheelDelta * GetHorizontalWheelStep()))
            e.Handled = true;
    }

    private void OnFrozenLayerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || _drag != DragMode.None) return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;

        if (ScrollVerticallyBy(-e.Delta / (double)WheelDelta * GetVerticalWheelStep()))
            e.Handled = true;
    }

    // ── Render ────────────────────────────────────────────────────────────

    public void Render()
    {
        if (IsRenaming) return;

        var palette = ThemeService.CurrentPalette;
        Brush surface = palette.Surface;
        Brush rowAccent = palette.AccentFaint;
        Brush gridMinor = palette.BorderSoft;
        Brush gridMajor = palette.Border;
        Brush divider = palette.BorderStrong;
        Brush dropLine = palette.Accent;

        RootCanvas.Children.Clear();
        _barRects.Clear();

        var items = ItemsSource?.ToList() ?? new List<ItemViewModel>();
        var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
        if (lanes.Count == 0) lanes.Add(new LaneViewModel("レーン 1"));

        double ppu    = GetPixelsPerTimeUnit();
        double total  = Math.Max(TotalDuration, 1);
        int    nLanes = lanes.Count;
        double totalW = total * ppu + 20;
        double totalH = nLanes * LaneH + AddLaneZoneH + 20;

        // 1. Background
        Add(Rect(totalW, totalH, surface), 0, 0);

        // 2. Alternating lane backgrounds and reorder source highlight
        for (int i = 0; i < nLanes; i++)
        {
            double rowY = i * LaneH;
            if (i % 2 == 0)
                Add(Rect(totalW, LaneH, rowAccent), 0, rowY);

            if (_drag == DragMode.LaneReorder && _reorderSourceLane == i)
                Add(new Rectangle
                {
                    Width = totalW, Height = LaneH,
                    Fill = CreateOverlayBrush(),
                }, 0, rowY);
        }

        // 3. Minor grid lines
        foreach (double t in EnumerateMinorTicks(total))
        {
            Add(VLine(t * ppu, 0, totalH, gridMinor, 0.5), 0, 0);
        }

        // 4. Major grid lines
        foreach (double t in EnumerateMajorTicks(total))
            Add(VLine(t * ppu, 0, totalH, t == 0 ? divider : gridMajor, t == 0 ? 1 : 0.8), 0, 0);

        // 5. Horizontal lane separators
        for (int i = 0; i <= nLanes; i++)
            Add(HLine(0, totalW, i * LaneH, gridMajor, i == 0 ? 1 : 0.6), 0, 0);

        // 6. Lane reorder drop indicator
        if (_drag == DragMode.LaneReorder && _reorderDropLane >= 0)
            Add(new Rectangle { Width = totalW, Height = 2, Fill = dropLine }, 0, _reorderDropLane * LaneH - 1);

        // 7. Compute bar rects
        double pps = GetPixelsPerSecond();
        var laneIndexMap = lanes.Select((lane, idx) => (lane.Id, idx)).ToDictionary(x => x.Id, x => x.idx);
        var itemMap = items.ToDictionary(i => i.Id);

        foreach (var item in items)
        {
            int li = laneIndexMap.TryGetValue(item.LaneId, out var idx) ? idx : 0;
            double bx = item.StartTime * pps;
            double by = li * LaneH + (LaneH - BarH) / 2.0;
            double bw = Math.Max(item.Duration * pps, MinBarW);
            _barRects[item.Id] = new Rect(bx, by, bw, BarH);
        }

        // 8. Dependency arrows
        DrawArrows(itemMap);

        // 9. Task bars
        foreach (var item in items.OrderBy(i => i.StartTime))
        {
            bool isGhost = _drag == DragMode.Move && _dragItem?.Id == item.Id;
            DrawBar(item, isGhost);
        }

        // 10. Drag ghost
        if (_drag == DragMode.Move && _dragItem != null)
            DrawDragGhost(_dragItem, ppu);

        // 11. Add-lane zone
        DrawAddLaneZone(totalW);

        RootCanvas.Width  = totalW;
        RootCanvas.Height = totalH;

        RenderFrozenLayers();
    }

    private void RenderFrozenLayers()
    {
        if (_renamingLane != null) return;
        RenderFrozenTimeHeader();
        RenderFrozenLaneHeader();
    }

    private void RenderFrozenTimeHeader()
    {
        var palette = ThemeService.CurrentPalette;

        FrozenTimeHeaderCanvas.Children.Clear();

        double viewportW = GetHeaderViewportWidth();
        if (viewportW <= 0) return;

        FrozenTimeHeaderCanvas.Width  = viewportW;
        FrozenTimeHeaderCanvas.Height = TimeHeaderH;

        AddTo(FrozenTimeHeaderCanvas, Rect(viewportW, TimeHeaderH, palette.SurfaceMuted), 0, 0);

        double total  = Math.Max(TotalDuration, 1);
        double ppu    = GetPixelsPerTimeUnit();
        double offset = TimelineScrollViewer.HorizontalOffset;

        foreach (double t in EnumerateMinorTicks(total))
        {
            double x = t * ppu - offset;
            if (x < -2 || x > viewportW + 2) continue;
            AddTo(FrozenTimeHeaderCanvas, VLine(x, 0, TimeHeaderH, palette.BorderSoft, 0.5), 0, 0);
        }

        foreach (double t in EnumerateMajorTicks(total))
        {
            double x = t * ppu - offset;
            if (x < -40 || x > viewportW + 40) continue;

            AddTo(FrozenTimeHeaderCanvas,
                VLine(x, 0, TimeHeaderH, t == 0 ? palette.BorderStrong : palette.Border, t == 0 ? 1 : 0.8), 0, 0);
            AddTo(FrozenTimeHeaderCanvas, new TextBlock
            {
                Text = FormatTickLabel(t),
                FontSize = 11,
                Foreground = palette.TextSecondary,
                Width = TimeTickLabelWidth,
                TextAlignment = TextAlignment.Left,
            }, x + TimeTickLabelOffset, (TimeHeaderH - 15) / 2);
        }

        AddTo(FrozenTimeHeaderCanvas, new TextBlock
        {
            Text = $"（{TimeUnit}）",
            FontSize = 10,
            Foreground = palette.TextSecondary,
        }, 4, (TimeHeaderH - 14) / 2);

        AddTo(FrozenTimeHeaderCanvas, HLine(0, viewportW, TimeHeaderH - 0.5, palette.BorderStrong, 1), 0, 0);
    }

    private void RenderFrozenLaneHeader()
    {
        var palette = ThemeService.CurrentPalette;

        FrozenLaneCanvas.Children.Clear();

        double viewportH = GetLaneViewportHeight();
        if (viewportH <= 0) return;

        FrozenLaneCanvas.Width  = LaneHeaderW;
        FrozenLaneCanvas.Height = viewportH;

        AddTo(FrozenLaneCanvas, Rect(LaneHeaderW, viewportH, palette.SurfaceAlt), 0, 0);

        var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
        if (lanes.Count == 0) lanes.Add(new LaneViewModel("レーン 1"));

        double offset = TimelineScrollViewer.VerticalOffset;

        for (int i = 0; i <= lanes.Count; i++)
        {
            double y = i * LaneH - offset;
            if (y < -2 || y > viewportH + 2) continue;
            AddTo(FrozenLaneCanvas, HLine(0, LaneHeaderW, y, palette.Border, i == 0 ? 1 : 0.6), 0, 0);
        }

        for (int i = 0; i < lanes.Count; i++)
        {
            double rowY = i * LaneH - offset;
            if (rowY + LaneH < 0 || rowY > viewportH) continue;

            bool isActiveLane    = SelectedItem != null && SelectedItem.LaneId == lanes[i].Id;
            bool isReorderSource = _drag == DragMode.LaneReorder && _reorderSourceLane == i;

            AddTo(FrozenLaneCanvas, new TextBlock
            {
                Text = "⠿",
                FontSize = 10,
                Cursor = Cursors.SizeNS,
                Foreground = CreateMutedOverlayBrush(),
            }, 5, rowY + (LaneH - 14) / 2);

            AddTo(FrozenLaneCanvas, new TextBlock
            {
                Text = lanes[i].Name,
                FontSize = 12,
                FontWeight = isActiveLane ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = palette.TextPrimary,
                Opacity = isReorderSource ? 0.3 : 1.0,
                Width = LaneHeaderW - 20,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }, 18, rowY + (LaneH - 15) / 2);

            if (isReorderSource)
            {
                AddTo(FrozenLaneCanvas, new Rectangle
                {
                    Width = LaneHeaderW,
                    Height = LaneH,
                    Fill = CreateOverlayBrush(),
                }, 0, rowY);

                AddTo(FrozenLaneCanvas, new TextBlock
                {
                    Text = "⠿",
                    FontSize = 10,
                    Cursor = Cursors.SizeNS,
                    Foreground = CreateMutedOverlayBrush(),
                }, 5, rowY + (LaneH - 14) / 2);

                AddTo(FrozenLaneCanvas, new TextBlock
                {
                    Text = lanes[i].Name,
                    FontSize = 12,
                    FontWeight = isActiveLane ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = palette.TextPrimary,
                    Opacity = 0.3,
                    Width = LaneHeaderW - 20,
                    TextAlignment = TextAlignment.Right,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                }, 18, rowY + (LaneH - 15) / 2);
            }
        }

        if (_drag == DragMode.LaneReorder && _reorderDropLane >= 0)
        {
            double y = _reorderDropLane * LaneH - offset;
            AddTo(FrozenLaneCanvas, new Ellipse { Width = 10, Height = 10, Fill = palette.Accent }, 1, y - 5);
            AddTo(FrozenLaneCanvas, new Rectangle { Width = LaneHeaderW - 11, Height = 2, Fill = palette.Accent }, 11, y - 1);
        }

        DrawFrozenAddLaneZone(lanes.Count, offset);
        AddTo(FrozenLaneCanvas, VLine(LaneHeaderW - 0.5, 0, viewportH, palette.BorderStrong, 1), 0, 0);
    }

    // ── Bar rendering ─────────────────────────────────────────────────────

    private void DrawBar(ItemViewModel item, bool ghost)
    {
        if (!_barRects.TryGetValue(item.Id, out var r)) return;
        bool selected = SelectedItem?.Id == item.Id;
        var palette = ThemeService.CurrentPalette;

        Brush fill = item.HasErrors
            ? palette.DangerSoft
            : palette.Accent;

        var bar = new Border
        {
            Width  = r.Width,
            Height = r.Height,
            Background = fill,
            CornerRadius = new CornerRadius(5),
            Opacity = ghost ? 0.22 : 1.0,
            BorderBrush = selected
                ? palette.TextPrimary
                : (item.HasErrors ? palette.Danger : Brushes.Transparent),
            BorderThickness = selected ? new Thickness(2) : new Thickness(item.HasErrors ? 1.5 : 0),
            Cursor = Cursors.SizeAll,
            ToolTip = BuildTooltip(item),
            ClipToBounds = true,
        };

        if (r.Width > 24)
        {
            bar.Child = new TextBlock
            {
                Text = item.Name,
                FontSize = 11,
                Foreground = palette.AccentText,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(7, 0, ResizeW + 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }

        Add(bar, r.Left, r.Top);

        if (!ghost && r.Width > ResizeW * 2)
        {
            Add(new Border
            {
                Width = ResizeW,
                Height = r.Height,
                Background = CreateResizeHandleBrush(),
                CornerRadius = new CornerRadius(0, 5, 5, 0),
                Cursor = Cursors.SizeWE,
            }, r.Right - ResizeW, r.Top);
        }
    }

    private void DrawDragGhost(ItemViewModel item, double ppu)
    {
        double pps = GetPixelsPerSecond();
        double ghostX = item.StartTime * pps;
        double ghostW = Math.Max(item.Duration * pps, MinBarW);
        double ghostY = _dragLaneIdx * LaneH + (LaneH - BarH) / 2.0;
        var palette = ThemeService.CurrentPalette;

        Add(new Border
        {
            Width = ghostW,
            Height = BarH,
            Background = palette.AccentGhost,
            CornerRadius = new CornerRadius(5),
            BorderBrush = palette.Accent,
            BorderThickness = new Thickness(2),
        }, ghostX, ghostY);
    }

    private void DrawAddLaneZone(double totalW)
    {
        var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
        double zoneY  = lanes.Count * LaneH;
        bool   active = _dragToNewLane;
        var palette = ThemeService.CurrentPalette;

        var zone = new Border
        {
            Width = totalW,
            Height = AddLaneZoneH,
            Background = active
                ? palette.AccentGhost
                : palette.AccentFaint,
            BorderBrush = active ? palette.Accent : palette.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Cursor = AddLaneFunc != null ? Cursors.Hand : Cursors.Arrow,
        };

        if (AddLaneFunc != null)
            zone.MouseLeftButtonDown += OnAddLaneZoneClick;

        Add(zone, 0, zoneY);
    }

    private void DrawFrozenAddLaneZone(int laneCount, double verticalOffset)
    {
        double zoneY  = laneCount * LaneH - verticalOffset;
        bool   active = _dragToNewLane;
        var palette = ThemeService.CurrentPalette;

        var hdr = new Border
        {
            Width = LaneHeaderW,
            Height = AddLaneZoneH,
            Background = active
                ? palette.AccentSubtleStrong
                : palette.SurfaceMuted,
            BorderBrush = active ? palette.Accent : palette.Border,
            BorderThickness = new Thickness(0, 1, 1, 0),
            Cursor = AddLaneFunc != null ? Cursors.Hand : Cursors.Arrow,
            Child = new TextBlock
            {
                Text = "+ 新しいレーン",
                FontSize = 10,
                Foreground = active ? palette.Accent : palette.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            }
        };

        if (AddLaneFunc != null)
            hdr.MouseLeftButtonDown += OnAddLaneZoneClick;

        AddTo(FrozenLaneCanvas, hdr, 0, zoneY);
    }

    private void OnAddLaneZoneClick(object sender, MouseButtonEventArgs e)
    {
        if (_drag != DragMode.None) return;
        AddLaneFunc?.Invoke();
        e.Handled = true;
    }

    private void OnPreviewGanttMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsRenaming || IsClickInsideActiveEditor(e.OriginalSource as DependencyObject)) return;

        // The first click outside an inline editor only confirms the edit.
        CommitActiveRename();
        e.Handled = true;
    }

    private void CommitActiveRename()
    {
        if (_renamingLane != null)
            CommitLaneRename();
        else if (_renamingItem != null)
            CommitTaskRename();
    }

    private bool IsClickInsideActiveEditor(DependencyObject? source) =>
        IsDescendantOf(source, _renameBox) || IsDescendantOf(source, _taskRenameBox);

    private static bool IsDescendantOf(DependencyObject? source, DependencyObject? ancestor)
    {
        while (source != null)
        {
            if (ReferenceEquals(source, ancestor)) return true;
            source = GetParent(source);
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject source)
    {
        if (source is FrameworkContentElement fce)
            return fce.Parent;

        if (source is ContentElement ce)
            return ContentOperations.GetParent(ce);

        if (source is Visual || source is Visual3D)
            return VisualTreeHelper.GetParent(source);

        return null;
    }

    // ── Lane rename ───────────────────────────────────────────────────────

    private void StartLaneRename(LaneViewModel lane, int laneIndex)
    {
        _renamingLane = lane;

        double boxY = laneIndex * LaneH - TimelineScrollViewer.VerticalOffset + (LaneH - 24) / 2.0;

        _renameBox = new TextBox
        {
            Text = lane.Name,
            Width = LaneHeaderW - 10,
            Height = 24,
            FontSize = 11,
            Padding = new Thickness(5, 2, 5, 2),
            BorderBrush = ThemeService.CurrentPalette.Accent,
            BorderThickness = new Thickness(1.5),
            Background = ThemeService.CurrentPalette.Surface,
            Foreground = ThemeService.CurrentPalette.TextPrimary,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _renameBox.KeyDown   += OnRenameKeyDown;
        _renameBox.LostFocus += OnRenameLostFocus;

        Canvas.SetLeft(_renameBox, 4);
        Canvas.SetTop(_renameBox, boxY);
        FrozenLaneCanvas.Children.Add(_renameBox);
        _renameBox.Focus();
        _renameBox.SelectAll();
    }

    private void CommitLaneRename(bool cancel = false)
    {
        var lane = _renamingLane;
        var renameBox = _renameBox;
        if (lane == null || renameBox == null) return;

        _renameBox = null;
        _renamingLane = null;

        renameBox.KeyDown   -= OnRenameKeyDown;
        renameBox.LostFocus -= OnRenameLostFocus;

        if (!cancel)
        {
            var name = renameBox.Text.Trim();
            if (!string.IsNullOrEmpty(name)) lane.Name = name;
        }

        if (renameBox.Parent is Panel panel)
            panel.Children.Remove(renameBox);

        Render();
    }

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            CommitLaneRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CommitLaneRename(cancel: true);
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object sender, RoutedEventArgs e) => CommitLaneRename();

    private void StartTaskRename(ItemViewModel item, bool discardOnCancel = false)
    {
        if (!_barRects.TryGetValue(item.Id, out var rect)) return;

        _renamingItem = item;
        _discardRenamingItemOnCancel = discardOnCancel;

        _taskRenameBox = new TextBox
        {
            Text = item.Name,
            Width = Math.Clamp(rect.Width + 24, 120, 280),
            Height = 24,
            FontSize = 11,
            Padding = new Thickness(5, 2, 5, 2),
            BorderBrush = ThemeService.CurrentPalette.Accent,
            BorderThickness = new Thickness(1.5),
            Background = ThemeService.CurrentPalette.Surface,
            Foreground = ThemeService.CurrentPalette.TextPrimary,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _taskRenameBox.KeyDown += OnTaskRenameKeyDown;
        _taskRenameBox.LostFocus += OnTaskRenameLostFocus;

        Canvas.SetLeft(_taskRenameBox, Math.Max(0, rect.Left + 2));
        Canvas.SetTop(_taskRenameBox, rect.Top + (rect.Height - _taskRenameBox.Height) / 2.0);
        Panel.SetZIndex(_taskRenameBox, int.MaxValue);
        RootCanvas.Children.Add(_taskRenameBox);
        _taskRenameBox.Focus();
        _taskRenameBox.SelectAll();
    }

    private void CommitTaskRename(bool cancel = false)
    {
        var item = _renamingItem;
        var taskRenameBox = _taskRenameBox;
        if (item == null || taskRenameBox == null) return;

        _taskRenameBox = null;
        _renamingItem = null;
        bool discardOnCancel = _discardRenamingItemOnCancel;
        _discardRenamingItemOnCancel = false;

        taskRenameBox.KeyDown   -= OnTaskRenameKeyDown;
        taskRenameBox.LostFocus -= OnTaskRenameLostFocus;

        if (!cancel)
        {
            var name = taskRenameBox.Text.Trim();
            if (!string.IsNullOrEmpty(name)) item.Name = name;
        }

        if (taskRenameBox.Parent is Panel panel)
            panel.Children.Remove(taskRenameBox);

        if (cancel && discardOnCancel)
        {
            DiscardItemFunc?.Invoke(item);
            if (SelectedItem?.Id == item.Id) SelectedItem = null;
            Render();
            return;
        }

        Render();
    }

    private void OnTaskRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            CommitTaskRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CommitTaskRename(cancel: true);
            e.Handled = true;
        }
    }

    private void OnTaskRenameLostFocus(object sender, RoutedEventArgs e) => CommitTaskRename();

    // ── Arrows ────────────────────────────────────────────────────────────

    private void DrawArrows(Dictionary<Guid, ItemViewModel> itemMap)
    {
        var edges = Edges?.ToList() ?? new List<DependencyEdge>();
        var palette = ThemeService.CurrentPalette;
        foreach (var edge in edges)
        {
            if (!_barRects.TryGetValue(edge.FromId, out var src)) continue;
            if (!_barRects.TryGetValue(edge.ToId,   out var dst)) continue;

            bool timeOk = itemMap.TryGetValue(edge.FromId, out var fi) &&
                          itemMap.TryGetValue(edge.ToId,   out var ti) &&
                          (fi!.StartTime + fi.Duration) <= ti!.StartTime + 1e-9;

            var stroke = timeOk
                ? palette.AccentGhost
                : palette.Danger;
            double sw = timeOk ? 1.5 : 2;

            double x1 = src.Right;
            double y1 = src.Top + src.Height / 2;
            double x2 = dst.Left;
            double y2 = dst.Top + dst.Height / 2;

            var fig = new PathFigure { StartPoint = new Point(x1, y1) };
            if (Math.Abs(y1 - y2) < 2)
            {
                fig.Segments.Add(new LineSegment(new Point(x2 - 8, y2), true));
            }
            else
            {
                double cx = Math.Min(40, Math.Abs(x2 - x1) * 0.5);
                fig.Segments.Add(new BezierSegment(new Point(x1 + cx, y1), new Point(x2 - cx, y2), new Point(x2 - 8, y2), true));
            }

            Add(new Path
            {
                Data = new PathGeometry(new[] { fig }),
                Stroke = stroke,
                StrokeThickness = sw,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeDashArray = timeOk ? null : new DoubleCollection { 4, 2 },
            }, 0, 0);

            Add(new Polygon
            {
                Fill = stroke,
                Points = new PointCollection
                {
                    new Point(x2, y2),
                    new Point(x2 - 8, y2 - 4),
                    new Point(x2 - 8, y2 + 4),
                },
            }, 0, 0);

            if (string.IsNullOrEmpty(edge.Condition)) continue;

            Add(new Border
            {
                Background = palette.Surface,
                BorderBrush = palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 1, 3, 1),
                Child = new TextBlock
                {
                    Text = edge.Condition.Length > 16 ? edge.Condition[..13] + "…" : edge.Condition,
                    FontSize = 9,
                    Foreground = timeOk ? palette.TextSecondary : palette.Danger,
                },
            }, (x1 + x2) / 2 - 28, (y1 + y2) / 2 - 11);
        }
    }

    private static Brush CreateOverlayBrush()
    {
        var color = ThemeService.CurrentPalette.IsDark
            ? Color.FromArgb(42, 255, 255, 255)
            : Color.FromArgb(80, 255, 255, 255);
        return new SolidColorBrush(color);
    }

    private static Brush CreateMutedOverlayBrush()
    {
        var baseColor = ((SolidColorBrush)ThemeService.CurrentPalette.TextSecondary).Color;
        return new SolidColorBrush(Color.FromArgb(100, baseColor.R, baseColor.G, baseColor.B));
    }

    private static Brush CreateResizeHandleBrush()
    {
        var alpha = ThemeService.CurrentPalette.IsDark ? (byte)90 : (byte)60;
        return new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
    }

    // ── Collision avoidance ───────────────────────────────────────────────

    private double FindValidStart(ItemViewModel item, double proposedStart, Guid targetLaneId)
    {
        double dur = item.Duration;
        var others = ItemsSource?
            .Where(i => i.Id != item.Id && i.LaneId == targetLaneId)
            .OrderBy(i => i.StartTime)
            .ToList() ?? new List<ItemViewModel>();

        var gaps = new List<(double s, double e)>();
        double cur = 0;
        foreach (var o in others)
        {
            if (o.StartTime > cur + 1e-9) gaps.Add((cur, o.StartTime));
            cur = Math.Max(cur, o.StartTime + o.Duration);
        }
        gaps.Add((cur, double.MaxValue));

        var validGaps = gaps.Where(g => g.e - g.s >= dur - 1e-9).ToList();
        if (validGaps.Count == 0) return Math.Max(0, proposedStart);

        double best = validGaps[0].s;
        double bestDist = double.MaxValue;
        foreach (var g in validGaps)
        {
            double minStart = NormalizeTimelineValue(g.s);
            double maxStart = g.e == double.MaxValue
                ? double.MaxValue
                : NormalizeTimelineValue(g.e - dur);
            if (maxStart < minStart) maxStart = minStart;

            double clamped = Math.Clamp(proposedStart, minStart, maxStart);
            double dist = Math.Abs(proposedStart - clamped);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = clamped;
            }
        }

        return NormalizeTimelineValue(Math.Max(0, best));
    }

    private double FindValidDuration(ItemViewModel item, double proposedDuration)
    {
        var next = ItemsSource?
            .Where(i => i.Id != item.Id && i.LaneId == item.LaneId &&
                        i.StartTime >= item.StartTime - 1e-9)
            .OrderBy(i => i.StartTime)
            .FirstOrDefault();

        double minDuration = GetGridStepInSeconds();
        double maxDur = next != null ? next.StartTime - item.StartTime : double.MaxValue;
        return NormalizeTimelineValue(Math.Clamp(proposedDuration, minDuration, Math.Max(minDuration, maxDur)));
    }

    // ── Drag & Drop ───────────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsRenaming) return;

        var pos = e.GetPosition(RootCanvas);
        var item = HitTestItem(pos);
        if (item == null)
        {
            TryAddTaskAt(pos, e);
            return;
        }

        SelectedItem = item;

        if (_barRects.TryGetValue(item.Id, out var r) &&
            pos.X < r.Right - ResizeW &&
            e.ClickCount == 2)
        {
            StartTaskRename(item);
            e.Handled = true;
            return;
        }

        _dragItem           = item;
        _dragOriginStart    = item.StartTime;
        _dragOriginDuration = item.Duration;
        _dragLaneIdx        = GetLaneIndex(pos.Y);

        if (_barRects.TryGetValue(item.Id, out r) && pos.X >= r.Right - ResizeW)
        {
            _drag = DragMode.Resize;
        }
        else
        {
            _drag = DragMode.Move;
            _dragMouseOffsetX = _barRects.TryGetValue(item.Id, out var barRect)
                ? (pos.X - barRect.Left) / GetPixelsPerSecond()
                : 0;
        }

        RootCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(RootCanvas);
        UpdateTimelineCursor(pos);

        if (_drag == DragMode.None) return;
        if (_dragItem == null) return;

        double pps = GetPixelsPerSecond();

        if (_drag == DragMode.Move)
        {
            var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
            if (lanes.Count == 0) return;

            double rawStart = SnapToSeconds(pos.X / pps - _dragMouseOffsetX);
            int rawLi = GetLaneIndex(pos.Y);

            if (rawLi >= lanes.Count && AddLaneFunc != null)
            {
                _dragToNewLane = true;
                _dragLaneIdx = lanes.Count;
                _dragItem.StartTime = Math.Max(0, rawStart);
            }
            else
            {
                _dragToNewLane = false;
                int li = Math.Clamp(rawLi, 0, lanes.Count - 1);
                _dragLaneIdx = li;
                var targetLaneId = lanes[li].Id;
                _dragItem.StartTime = FindValidStart(_dragItem, rawStart, targetLaneId);
                _dragItem.LaneId = targetLaneId;
            }
        }
        else if (_drag == DragMode.Resize)
        {
            double rawEnd = SnapToSeconds(pos.X / pps);
            double rawDur = rawEnd - _dragItem.StartTime;
            _dragItem.Duration = FindValidDuration(_dragItem, rawDur);
        }

        Render();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        CommitDrag();
        e.Handled = true;
    }

    private void OnFrozenLaneMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsRenaming) return;

        var pos = GetFrozenLaneContentPoint(e);
        var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
        double addLaneTop = lanes.Count * LaneH;

        if (pos.Y >= addLaneTop && pos.Y <= addLaneTop + AddLaneZoneH)
        {
            if (_drag == DragMode.None)
                AddLaneFunc?.Invoke();
            e.Handled = true;
            return;
        }

        int laneIdx = GetLaneIndex(pos.Y);
        if (laneIdx < 0 || laneIdx >= lanes.Count) return;

        if (e.ClickCount == 2)
        {
            StartLaneRename(lanes[laneIdx], laneIdx);
            e.Handled = true;
            return;
        }

        _reorderSourceLane = laneIdx;
        _reorderDropLane   = laneIdx;
        _drag              = DragMode.LaneReorder;
        FrozenLaneCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnFrozenLaneMouseMove(object sender, MouseEventArgs e)
    {
        var pos = GetFrozenLaneContentPoint(e);
        UpdateFrozenLaneCursor(pos);

        if (_drag != DragMode.LaneReorder) return;

        var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
        int rawLi = GetLaneIndex(pos.Y);
        double localY = pos.Y - rawLi * LaneH;
        int dropPos = localY < LaneH / 2.0 ? rawLi : rawLi + 1;
        _reorderDropLane = Math.Clamp(dropPos, 0, lanes.Count);

        Render();
    }

    private void OnFrozenLaneMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == DragMode.LaneReorder)
        {
            CommitDrag();
            e.Handled = true;
        }
    }

    private void CommitDrag()
    {
        if (_drag == DragMode.LaneReorder)
        {
            var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
            int from = _reorderSourceLane;
            int to = _reorderDropLane <= from ? _reorderDropLane : _reorderDropLane - 1;
            if (from != to && from >= 0 && from < lanes.Count && to >= 0 && to < lanes.Count)
                ReorderLanesCallback?.Invoke(from, to);
            _reorderSourceLane = -1;
            _reorderDropLane = -1;
        }
        else if (_dragToNewLane && _dragItem != null && AddLaneFunc != null)
        {
            var newLaneId = AddLaneFunc();
            _dragItem.LaneId = newLaneId;
        }
        else if (_dragItem != null && _drag == DragMode.Move)
        {
            _dragItem.StartTime = NormalizeTimelineValue(Math.Max(0, _dragItem.StartTime));
        }

        _dragToNewLane = false;
        _drag = DragMode.None;
        _dragItem = null;

        if (RootCanvas.IsMouseCaptured) RootCanvas.ReleaseMouseCapture();
        if (FrozenLaneCanvas.IsMouseCaptured) FrozenLaneCanvas.ReleaseMouseCapture();

        Render();
    }

    private void UpdateTimelineCursor(Point pos)
    {
        if (_drag != DragMode.None) return;

        var item = HitTestItem(pos);
        if (item == null)
        {
            RootCanvas.Cursor = Cursors.Arrow;
            return;
        }

        if (_barRects.TryGetValue(item.Id, out var r) && pos.X >= r.Right - ResizeW)
            RootCanvas.Cursor = Cursors.SizeWE;
        else
            RootCanvas.Cursor = Cursors.SizeAll;
    }

    private void UpdateFrozenLaneCursor(Point contentPos)
    {
        if (_drag == DragMode.LaneReorder) return;

        var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
        double addLaneTop = lanes.Count * LaneH;
        if (contentPos.Y >= addLaneTop && contentPos.Y <= addLaneTop + AddLaneZoneH && AddLaneFunc != null)
        {
            FrozenLaneCanvas.Cursor = Cursors.Hand;
            return;
        }

        int laneIdx = GetLaneIndex(contentPos.Y);
        FrozenLaneCanvas.Cursor = laneIdx >= 0 && laneIdx < lanes.Count ? Cursors.SizeNS : Cursors.Arrow;
    }

    private void TryAddTaskAt(Point pos, MouseButtonEventArgs e)
    {
        var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
        int laneIdx = GetLaneIndex(pos.Y);

        if (laneIdx < 0 || laneIdx >= lanes.Count || AddItemAtFunc == null)
        {
            SelectedItem = null;
            return;
        }

        double startTime = SnapToSeconds(Math.Max(0, pos.X / GetPixelsPerSecond()));
        var item = AddItemAtFunc(lanes[laneIdx].Id, startTime);
        if (item != null)
        {
            SelectedItem = item;
            StartTaskRename(item, discardOnCancel: true);
        }

        e.Handled = true;
    }

    // ── Hit testing ───────────────────────────────────────────────────────

    private ItemViewModel? HitTestItem(Point pos)
    {
        var items = ItemsSource?.ToList() ?? new List<ItemViewModel>();
        foreach (var item in items.OrderByDescending(i => i.StartTime))
            if (_barRects.TryGetValue(item.Id, out var r) && r.Contains(pos))
                return item;
        return null;
    }

    private int GetLaneIndex(double y) => (int)(y / LaneH);

    private IEnumerable<double> EnumerateMinorTicks(double total)
    {
        double step = GetGridStep();
        if (step >= 1.0 - 1e-9) yield break;

        for (double t = step; t < total - 1e-9; t += step)
        {
            double normalized = NormalizeTimelineValue(t);
            if (Math.Abs(normalized - Math.Round(normalized)) < 1e-9) continue;
            yield return normalized;
        }
    }

    private IEnumerable<double> EnumerateMajorTicks(double total)
    {
        double step = GetGridStep();
        if (step < 1.0 - 1e-9)
        {
            for (int t = 0; t <= (int)Math.Ceiling(total); t++)
                yield return t;
        }
        else
        {
            for (double t = 0; t <= total + 1e-9; t = NormalizeTimelineValue(t + step))
                yield return t;
        }
    }

    private static string FormatTickLabel(double value) => value.ToString("0.####");

    private double GetGridStep() => Math.Max(CellDuration, 0.0001);

    private double GetPixelsPerTimeUnit() => PixelsPerUnit / GetGridStep();

    private double GetSecondsPerUnit() => TimeUnit switch
    {
        "秒" => 1,
        "分" => 60,
        "時間" => 3600,
        "日" => 86400,
        "週" => 604800,
        "スプリント" => 1209600,
        _ => 1,
    };

    // pixels per second: positions task bars from absolute seconds
    private double GetPixelsPerSecond() => GetPixelsPerTimeUnit() / GetSecondsPerUnit();

    // grid step expressed in seconds
    private double GetGridStepInSeconds() => GetGridStep() * GetSecondsPerUnit();

    private double SnapToSeconds(double seconds) => NormalizeTimelineValue(Snap(seconds, GetGridStepInSeconds()));

    private static double Snap(double value, double snap) =>
        snap <= 0 ? value : Math.Round(value / snap) * snap;

    private static double NormalizeTimelineValue(double value) =>
        Math.Round(value, 10, MidpointRounding.AwayFromZero);

    private Point GetFrozenLaneContentPoint(MouseEventArgs e)
    {
        var pos = e.GetPosition(FrozenLaneCanvas);
        return new Point(pos.X, pos.Y + TimelineScrollViewer.VerticalOffset);
    }

    private bool ScrollHorizontallyBy(double delta)
    {
        if (TimelineScrollViewer.ScrollableWidth <= 0) return false;

        double current = TimelineScrollViewer.HorizontalOffset;
        double target = Math.Clamp(current + delta, 0, TimelineScrollViewer.ScrollableWidth);
        if (Math.Abs(target - current) < 0.5) return false;

        TimelineScrollViewer.ScrollToHorizontalOffset(target);
        return true;
    }

    private bool ScrollVerticallyBy(double delta)
    {
        if (TimelineScrollViewer.ScrollableHeight <= 0) return false;

        double current = TimelineScrollViewer.VerticalOffset;
        double target = Math.Clamp(current + delta, 0, TimelineScrollViewer.ScrollableHeight);
        if (Math.Abs(target - current) < 0.5) return false;

        TimelineScrollViewer.ScrollToVerticalOffset(target);
        return true;
    }

    private double GetHorizontalWheelStep() =>
        Math.Max(GetBaseWheelStep(), PixelsPerUnit * 0.6);

    private static double GetVerticalWheelStep() => GetBaseWheelStep();

    private static double GetBaseWheelStep()
    {
        double lines = SystemParameters.WheelScrollLines;
        return lines < 0 ? 160.0 : Math.Max(48.0, lines * 16.0);
    }

    private static short GetWheelDelta(IntPtr wParam) =>
        unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));

    private double GetHeaderViewportWidth()
    {
        if (TimelineScrollViewer.ViewportWidth > 0) return TimelineScrollViewer.ViewportWidth;
        return Math.Max(0, ActualWidth - LaneHeaderW);
    }

    private double GetLaneViewportHeight()
    {
        if (TimelineScrollViewer.ViewportHeight > 0) return TimelineScrollViewer.ViewportHeight;
        return Math.Max(0, ActualHeight - TimeHeaderH);
    }

    // ── Tooltip ───────────────────────────────────────────────────────────

    private ToolTip BuildTooltip(ItemViewModel item)
    {
        var palette = ThemeService.CurrentPalette;
        var sp = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
        sp.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = palette.TextPrimary,
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"開始: {HmsConverter.Format(item.StartTime)}  ／  所要: {HmsConverter.Format(item.Duration)}  ／  終了: {HmsConverter.Format(item.StartTime + item.Duration)}",
            FontSize = 10,
            Foreground = palette.TextSecondary,
            Margin = new Thickness(0, 2, 0, 0),
        });
        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            sp.Children.Add(new TextBlock
            {
                Text = item.Description,
                FontSize = 11,
                Foreground = palette.TextSecondary,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }
        if (item.HasErrors)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "⚠ " + item.ErrorMessage,
                Foreground = palette.Danger,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280,
            });
        }
        return new ToolTip
        {
            Content = sp,
            Background = palette.Surface,
            BorderBrush = palette.Border,
            Foreground = palette.TextPrimary,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void Add(UIElement el, double x, double y) => AddTo(RootCanvas, el, x, y);

    private static void AddTo(Canvas canvas, UIElement el, double x, double y)
    {
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        canvas.Children.Add(el);
    }

    private static Rectangle Rect(double w, double h, Brush fill) =>
        new() { Width = w, Height = h, Fill = fill };

    private static Line VLine(double x, double y1, double y2, Brush stroke, double sw) =>
        new() { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = stroke, StrokeThickness = sw };

    private static Line HLine(double x1, double x2, double y, Brush stroke, double sw) =>
        new() { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = stroke, StrokeThickness = sw };

    private static string FormatTimelineValue(double value) => value.ToString("0.####");
}

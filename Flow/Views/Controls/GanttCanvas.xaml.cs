using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    public static readonly DependencyProperty CategoriesProperty =
        DependencyProperty.Register(nameof(Categories), typeof(IEnumerable<CategoryViewModel>),
            typeof(GanttCanvas), new PropertyMetadata(null, OnAnyChanged));
    public static readonly DependencyProperty CursorLaneIndexProperty =
        DependencyProperty.Register(nameof(CursorLaneIndex), typeof(int),
            typeof(GanttCanvas), new PropertyMetadata(0, (d, _) => ((GanttCanvas)d).Render()));
    public static readonly DependencyProperty CursorTimeProperty =
        DependencyProperty.Register(nameof(CursorTime), typeof(double),
            typeof(GanttCanvas), new PropertyMetadata(0.0, (d, _) => ((GanttCanvas)d).Render()));

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
    public IEnumerable<CategoryViewModel>? Categories
    { get => (IEnumerable<CategoryViewModel>?)GetValue(CategoriesProperty); set => SetValue(CategoriesProperty, value); }
    public int    CursorLaneIndex
    { get => (int)GetValue(CursorLaneIndexProperty);    set => SetValue(CursorLaneIndexProperty, value); }
    public double CursorTime
    { get => (double)GetValue(CursorTimeProperty);      set => SetValue(CursorTimeProperty, value); }

    // ── Layout constants ──────────────────────────────────────────────────
    private double _laneHeaderW  = 150;
    private double LaneHeaderW   => _laneHeaderW;
    private bool   _needsAutoFit = true;
    private const double LaneH        = 36;
    private const double BarH         = 28;
    private const double TimeHeaderH  = 30;
    private const double TimeTickLabelOffset = 4;
    private const double TimeTickLabelWidth  = 36;
    private const double ResizeW          = 8;
    private const double MinBarW          = 4;
    private const double ResizeSnapPixels = 10.0;
    private const double AddLaneZoneH = 32;
    private const int    WmMouseHWheel = 0x020E;
    private const int    WheelDelta    = 120;

    // ── Drag state ────────────────────────────────────────────────────────
    private ItemViewModel? _dragItem;
    private enum DragMode { None, Move, Resize, LaneReorder, Create }
    private DragMode _drag = DragMode.None;
    private double _dragOriginStart;
    private double _dragOriginDuration;
    private double _dragMouseOffsetX;
    private int    _dragLaneIdx;
    private bool   _dragToNewLane;

    // Create drag state
    private double _createStartTime;
    private double _createCurrentDuration;
    private int    _createLaneIdx;
    private double _createCurrentMouseY;

    // Pending drag state (actual drag starts only after threshold is exceeded)
    private Point _pendingMouseDownPos;
    private enum PendingDragType { None, Create, Move, Resize }
    private PendingDragType _pendingDrag = PendingDragType.None;
    private const double DragThreshold = 5.0;

    // Lane reorder state
    private int    _reorderSourceLane  = -1;
    private int    _reorderDropLane    = -1;
    private double _reorderMouseVisualY = 0;

    // Resize drag state: original positions and touching chain
    private Dictionary<Guid, double> _dragLaneOriginalStarts = new();
    private List<ItemViewModel> _dragTouchingChain = new();

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

    public bool IsEditing => IsRenaming;

    public void ScrollCursorIntoCenter()
    {
        double pps    = GetPixelsPerSecond();
        double cursorX = CursorTime * pps;
        double cursorY = Math.Clamp(CursorLaneIndex, 0, Math.Max(0, (Lanes?.Count() ?? 1) - 1)) * LaneH + LaneH / 2.0;

        double viewW = TimelineScrollViewer.ViewportWidth;
        TimelineScrollViewer.ScrollToHorizontalOffset(
            Math.Clamp(cursorX - viewW / 2, 0, TimelineScrollViewer.ScrollableWidth));

        double viewH = TimelineScrollViewer.ViewportHeight;
        TimelineScrollViewer.ScrollToVerticalOffset(
            Math.Clamp(cursorY - viewH / 2, 0, TimelineScrollViewer.ScrollableHeight));
    }

    public void ScrollCursorIntoView()
    {
        double pps    = GetPixelsPerSecond();
        double cellW  = Math.Max(GetGridStepInSeconds() * pps, 2);

        double cursorLeft  = CursorTime * pps;
        double cursorRight = cursorLeft + cellW;
        double hOffset = TimelineScrollViewer.HorizontalOffset;
        double viewW   = TimelineScrollViewer.ViewportWidth;

        if (cursorLeft < hOffset)
            TimelineScrollViewer.ScrollToHorizontalOffset(cursorLeft);
        else if (cursorRight > hOffset + viewW)
            TimelineScrollViewer.ScrollToHorizontalOffset(cursorRight - viewW);

        int    li         = Math.Clamp(CursorLaneIndex, 0, Math.Max(0, (Lanes?.Count() ?? 1) - 1));
        double laneTop    = li * LaneH;
        double laneBottom = laneTop + LaneH;
        double vOffset = TimelineScrollViewer.VerticalOffset;
        double viewH   = TimelineScrollViewer.ViewportHeight;

        if (laneTop < vOffset)
            TimelineScrollViewer.ScrollToVerticalOffset(laneTop);
        else if (laneBottom > vOffset + viewH)
            TimelineScrollViewer.ScrollToVerticalOffset(laneBottom - viewH);
    }

    public void StartRenameSelectedItem(bool discardOnCancel = false)
    {
        var item = SelectedItem;
        if (item == null || IsRenaming) return;
        StartTaskRename(item, discardOnCancel);
    }

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

        RootCanvas.MouseRightButtonDown += OnRightClick;

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
        FrozenLaneCanvas.SizeChanged       += OnFrozenLaneCanvasSizeChanged;
    }

    // ── Collection subscription ───────────────────────────────────────────

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (GanttCanvas)d;
        if (e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= ctrl.OnColl;
        if (e.NewValue is INotifyCollectionChanged nw)  nw.CollectionChanged  += ctrl.OnColl;
        if (e.Property == LanesProperty) ctrl._needsAutoFit = true;
        ctrl.Render();
    }

    private void OnColl(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (ReferenceEquals(s, Lanes)) _needsAutoFit = true;
        Render();
    }

    private void OnTimelineScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_renamingLane != null) return;
        if (e.HorizontalChange == 0 && e.VerticalChange == 0 &&
            e.ViewportWidthChange == 0 && e.ViewportHeightChange == 0) return;
        RenderFrozenLayers();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachWindowHook();
        Render();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachWindowHook();
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Render();

    public void RequestAutoFitLaneHeader()
    {
        _needsAutoFit = true;
        Render();
    }

    private void OnFrozenLaneCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = e.NewSize.Width;
        if (w > 1 && Math.Abs(w - _laneHeaderW) > 0.5)
        {
            _laneHeaderW = w;
            RenderFrozenLaneHeader();
        }
    }

    private double CalcAutoFitLaneWidth()
    {
        var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
        if (lanes.Count == 0) return 120.0;

        var typeface = new Typeface(new FontFamily("Segoe UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double maxTextW = 0;

        foreach (var lane in lanes)
        {
            var ft = new FormattedText(
                lane.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface, 12, Brushes.Black, dpi);
            maxTextW = Math.Max(maxTextW, ft.Width);
        }

        // left-margin(8) + text + right-margin(8)
        return Math.Clamp(maxTextW + 16, 80, 500);
    }

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

        if (IsLoaded && _needsAutoFit)
        {
            double fw = CalcAutoFitLaneWidth();
            if (Math.Abs(fw - _laneHeaderW) > 1.0)
            {
                GanttLayoutGrid.ColumnDefinitions[0].Width = new GridLength(fw);
                _laneHeaderW = fw;
            }
            _needsAutoFit = false;
        }

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
        double pps    = GetPixelsPerSecond();
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

        // 2.5. Cursor cell
        if (_drag == DragMode.None && nLanes > 0)
        {
            int    cl    = Math.Clamp(CursorLaneIndex, 0, nLanes - 1);
            double cellW = Math.Max(GetGridStepInSeconds() * pps, 2);
            var accentColor = ((SolidColorBrush)palette.Accent).Color;
            Add(new Border
            {
                Width           = cellW,
                Height          = LaneH,
                Background      = new SolidColorBrush(Color.FromArgb(18, accentColor.R, accentColor.G, accentColor.B)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(120, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
            }, CursorTime * pps, cl * LaneH);
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

        if (_drag == DragMode.Create && _createCurrentDuration > 0)
            DrawCreateGhost();

        // 11. Drag info label (duration + end time above right edge)
        if ((_drag == DragMode.Move || _drag == DragMode.Resize) && _dragItem != null)
            DrawDragInfoLabel(_dragItem, pps);

        if (_drag == DragMode.Create && _createCurrentDuration > 0)
            DrawCreateInfoLabel();

        // 12. Lane reorder timeline ghost
        if (_drag == DragMode.LaneReorder && _reorderSourceLane >= 0 && _reorderSourceLane < nLanes)
            DrawLaneReorderTimelineGhost(lanes, items, palette, totalW, pps);

        // 13. Add-lane zone
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

        // Sync with actual column width set by GridSplitter
        double colW = GanttLayoutGrid.ColumnDefinitions[0].ActualWidth;
        if (colW > 1) _laneHeaderW = colW;

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
                Text = lanes[i].Name,
                FontSize = 12,
                FontWeight = isActiveLane ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = palette.TextPrimary,
                Opacity = isReorderSource ? 0.25 : 1.0,
                Width = LaneHeaderW - 16,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }, 8, rowY + (LaneH - 15) / 2);

            if (isReorderSource)
            {
                AddTo(FrozenLaneCanvas, new Rectangle
                {
                    Width = LaneHeaderW,
                    Height = LaneH,
                    Fill = CreateOverlayBrush(),
                }, 0, rowY);
            }

            if (i == CursorLaneIndex && _drag == DragMode.None)
            {
                AddTo(FrozenLaneCanvas, new Rectangle
                {
                    Width  = 3,
                    Height = LaneH,
                    Fill   = palette.Accent,
                }, 0, rowY);
            }
        }

        if (_drag == DragMode.LaneReorder && _reorderDropLane >= 0)
        {
            double y = _reorderDropLane * LaneH - offset;
            AddTo(FrozenLaneCanvas, new Ellipse { Width = 10, Height = 10, Fill = palette.Accent }, 1, y - 5);
            AddTo(FrozenLaneCanvas, new Rectangle { Width = LaneHeaderW - 11, Height = 2, Fill = palette.Accent }, 11, y - 1);
        }

        if (_drag == DragMode.LaneReorder && _reorderSourceLane >= 0 && _reorderSourceLane < lanes.Count)
        {
            var ghostLane = lanes[_reorderSourceLane];
            bool isGhostActive = SelectedItem != null && SelectedItem.LaneId == ghostLane.Id;
            double ghostTop = Math.Clamp(_reorderMouseVisualY - LaneH / 2.0, 0, Math.Max(0, viewportH - LaneH));

            // shadow
            AddTo(FrozenLaneCanvas, new Border
            {
                Width = LaneHeaderW - 6,
                Height = LaneH - 2,
                Background = palette.Border,
                CornerRadius = new CornerRadius(6),
                Opacity = 0.35,
            }, 5, ghostTop + 6);

            // ghost body
            AddTo(FrozenLaneCanvas, new Border
            {
                Width = LaneHeaderW - 6,
                Height = LaneH - 2,
                Background = palette.SurfaceAlt,
                BorderBrush = palette.Accent,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
            }, 3, ghostTop + 2);

            AddTo(FrozenLaneCanvas, new TextBlock
            {
                Text = ghostLane.Name,
                FontSize = 12,
                FontWeight = isGhostActive ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = palette.TextPrimary,
                Width = LaneHeaderW - 20,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }, 8, ghostTop + 2 + (LaneH - 2 - 15) / 2);
        }

        DrawFrozenAddLaneZone(lanes.Count, offset, viewportH);
        AddTo(FrozenLaneCanvas, VLine(LaneHeaderW - 0.5, 0, viewportH, palette.BorderStrong, 1), 0, 0);
    }

    // ── Bar rendering ─────────────────────────────────────────────────────

    private void DrawBar(ItemViewModel item, bool ghost)
    {
        if (!_barRects.TryGetValue(item.Id, out var r)) return;
        bool selected = SelectedItem?.Id == item.Id;
        var palette = ThemeService.CurrentPalette;

        var category = item.CategoryId != Guid.Empty
            ? Categories?.FirstOrDefault(c => c.Id == item.CategoryId)
            : null;
        Brush fill = item.HasErrors
            ? palette.DangerSoft
            : (category != null ? category.Brush : palette.Accent);
        Brush textFill = item.HasErrors || category == null
            ? palette.AccentText
            : GetCategoryTextBrush(category.ColorValue);

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
                Foreground = textFill,
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

        var category = item.CategoryId != Guid.Empty
            ? Categories?.FirstOrDefault(c => c.Id == item.CategoryId)
            : null;
        Brush fill = item.HasErrors
            ? palette.DangerSoft
            : (category != null ? category.Brush : palette.Accent);
        Brush textFill = item.HasErrors || category == null
            ? palette.AccentText
            : GetCategoryTextBrush(category.ColorValue);

        // shadow
        Add(new Border
        {
            Width = ghostW,
            Height = BarH,
            Background = palette.Border,
            CornerRadius = new CornerRadius(5),
            Opacity = 0.35,
        }, ghostX + 3, ghostY + 5);

        // floating bar
        var ghost = new Border
        {
            Width = ghostW,
            Height = BarH,
            Background = fill,
            CornerRadius = new CornerRadius(5),
            BorderBrush = palette.TextPrimary,
            BorderThickness = new Thickness(1.5),
            Opacity = 0.88,
            ClipToBounds = true,
        };
        if (ghostW > 24)
        {
            ghost.Child = new TextBlock
            {
                Text = item.Name,
                FontSize = 11,
                Foreground = textFill,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(7, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }
        Add(ghost, ghostX, ghostY - 2);
    }

    private void DrawLaneReorderTimelineGhost(
        List<LaneViewModel> lanes, List<ItemViewModel> items,
        ThemePalette palette, double totalW, double pps)
    {
        var sourceLane = lanes[_reorderSourceLane];
        double ghostY = _reorderMouseVisualY + TimelineScrollViewer.VerticalOffset - LaneH / 2.0;

        // shadow
        Add(new Rectangle
        {
            Width = totalW, Height = LaneH,
            Fill = palette.Border,
            Opacity = 0.25,
        }, 0, ghostY + 5);

        // row background
        Add(new Border
        {
            Width = totalW, Height = LaneH - 2,
            Background = palette.SurfaceAlt,
            BorderBrush = palette.Accent,
            BorderThickness = new Thickness(0, 1.5, 0, 1.5),
            Opacity = 0.9,
        }, 0, ghostY + 1);

        // task bars in the ghost row
        foreach (var item in items.Where(i => i.LaneId == sourceLane.Id))
        {
            double bx = item.StartTime * pps;
            double bw = Math.Max(item.Duration * pps, MinBarW);
            double by = ghostY + 1 + (LaneH - BarH) / 2.0;

            var category = item.CategoryId != Guid.Empty
                ? Categories?.FirstOrDefault(c => c.Id == item.CategoryId)
                : null;
            Brush fill = item.HasErrors
                ? palette.DangerSoft
                : (category != null ? category.Brush : palette.Accent);

            var bar = new Border
            {
                Width = bw, Height = BarH,
                Background = fill,
                CornerRadius = new CornerRadius(5),
                Opacity = 0.88,
                ClipToBounds = true,
            };
            if (bw > 24)
            {
                Brush textFill = item.HasErrors || category == null
                    ? palette.AccentText
                    : GetCategoryTextBrush(category.ColorValue);
                bar.Child = new TextBlock
                {
                    Text = item.Name,
                    FontSize = 11,
                    Foreground = textFill,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(7, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
            }
            Add(bar, bx, by);
        }
    }

    private void DrawCreateGhost()
    {
        double pps   = GetPixelsPerSecond();
        double ghostX = _createStartTime * pps;
        double ghostW = Math.Max(_createCurrentDuration * pps, MinBarW);
        double ghostY = _createLaneIdx * LaneH + (LaneH - BarH) / 2.0;
        var palette   = ThemeService.CurrentPalette;

        Add(new Border
        {
            Width           = ghostW,
            Height          = BarH,
            Background      = palette.AccentGhost,
            CornerRadius    = new CornerRadius(5),
            BorderBrush     = palette.Accent,
            BorderThickness = new Thickness(2),
        }, ghostX, ghostY);
    }

    private void DrawCreateInfoLabel()
    {
        double pps    = GetPixelsPerSecond();
        double endTime = _createStartTime + _createCurrentDuration;
        double endX    = endTime * pps;
        double barY    = _createLaneIdx * LaneH + (LaneH - BarH) / 2.0;
        var palette    = ThemeService.CurrentPalette;

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text       = $"所要  {HmsConverter.Format(_createCurrentDuration)}",
            FontSize   = 10,
            Foreground = palette.TextSecondary,
        });
        sp.Children.Add(new TextBlock
        {
            Text       = $"完了  {HmsConverter.Format(endTime)}",
            FontSize   = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.TextPrimary,
        });

        var label = new Border
        {
            Background      = palette.Surface,
            BorderBrush     = palette.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(6, 3, 6, 3),
            Child           = sp,
        };
        Panel.SetZIndex(label, int.MaxValue);

        const double labelW = 108;
        const double labelH = 38;
        Add(label, Math.Max(0, endX - labelW / 2), Math.Max(0, barY - labelH - 6));
    }

    private void DrawDragInfoLabel(ItemViewModel item, double pps)
    {
        double endTime = item.StartTime + item.Duration;
        double endX    = endTime * pps;

        double barY;
        if (_dragToNewLane)
            barY = _dragLaneIdx * LaneH + (LaneH - BarH) / 2.0;
        else if (_barRects.TryGetValue(item.Id, out var r))
            barY = r.Top;
        else
            return;

        var palette = ThemeService.CurrentPalette;

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text       = $"所要  {HmsConverter.Format(item.Duration)}",
            FontSize   = 10,
            Foreground = palette.TextSecondary,
        });
        sp.Children.Add(new TextBlock
        {
            Text       = $"完了  {HmsConverter.Format(endTime)}",
            FontSize   = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.TextPrimary,
        });

        var label = new Border
        {
            Background      = palette.Surface,
            BorderBrush     = palette.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(6, 3, 6, 3),
            Child           = sp,
        };
        Panel.SetZIndex(label, int.MaxValue);

        const double labelW = 108;
        const double labelH = 38;
        Add(label, Math.Max(0, endX - labelW / 2), Math.Max(0, barY - labelH - 6));
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

    private void DrawFrozenAddLaneZone(int laneCount, double verticalOffset, double viewportH)
    {
        double naturalY = laneCount * LaneH - verticalOffset;
        double zoneY = Math.Clamp(naturalY, 0, Math.Max(0, viewportH - AddLaneZoneH));
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
                Text = "+",
                FontSize = 16,
                FontWeight = FontWeights.Light,
                Foreground = active ? palette.Accent : palette.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
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

    private static Brush GetCategoryTextBrush(Color color)
    {
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return luminance > 0.62
            ? new SolidColorBrush(Color.FromRgb(32, 33, 36))
            : Brushes.White;
    }

    // ── Successor push (resize cascade) ──────────────────────────────────

    private void PushSuccessors(ItemViewModel anchor, ISet<Guid>? skip = null)
    {
        var successors = ItemsSource?
            .Where(i => i.Id != anchor.Id && i.LaneId == anchor.LaneId &&
                        i.StartTime >= anchor.StartTime - 1e-9 &&
                        (skip == null || !skip.Contains(i.Id)))
            .OrderBy(i => i.StartTime)
            .ToList() ?? new List<ItemViewModel>();

        double pushFront = anchor.StartTime + anchor.Duration;
        foreach (var task in successors)
        {
            if (task.StartTime < pushFront - 1e-9)
            {
                task.StartTime = NormalizeTimelineValue(pushFront);
                pushFront = task.StartTime + task.Duration;
            }
            else
            {
                break;
            }
        }
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

            double snapped = FindBestSnap(proposedStart, minStart, maxStart);
            double dist = Math.Abs(proposedStart - snapped);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = snapped;
            }
        }

        return NormalizeTimelineValue(Math.Max(0, best));
    }

    // Returns the snap candidate (grid position or task boundary) nearest to proposedStart,
    // clamped to [minStart, maxStart].
    private double FindBestSnap(double proposedStart, double minStart, double maxStart)
    {
        double best = Math.Clamp(proposedStart, minStart, maxStart);
        double bestDist = double.MaxValue;

        void TryCandidate(double candidate)
        {
            candidate = NormalizeTimelineValue(Math.Clamp(candidate, minStart, maxStart));
            double d = Math.Abs(proposedStart - candidate);
            if (d < bestDist) { bestDist = d; best = candidate; }
        }

        // Task boundary: flush against preceding task (gap start) or following task (gap end - dur)
        TryCandidate(minStart);
        if (maxStart < double.MaxValue / 2)
            TryCandidate(maxStart);

        // Grid snap: nearest grid position and its two neighbours
        double gridStep = GetGridStepInSeconds();
        if (gridStep > 0)
        {
            double nearest = NormalizeTimelineValue(Math.Round(proposedStart / gridStep) * gridStep);
            TryCandidate(nearest);
            TryCandidate(NormalizeTimelineValue(nearest - gridStep));
            TryCandidate(NormalizeTimelineValue(nearest + gridStep));
        }

        return best;
    }

    private double FindValidDuration(ItemViewModel item, double proposedDuration)
    {
        var next = ItemsSource?
            .Where(i => i.Id != item.Id && i.LaneId == item.LaneId &&
                        i.StartTime >= item.StartTime - 1e-9)
            .OrderBy(i => i.StartTime)
            .FirstOrDefault();

        double minDuration = Math.Min(GetGridStepInSeconds(), 1.0);
        double maxDur = next != null ? next.StartTime - item.StartTime : double.MaxValue;
        return NormalizeTimelineValue(Math.Clamp(proposedDuration, minDuration, Math.Max(minDuration, maxDur)));
    }

    private double SnapResizeEnd(ItemViewModel item, double proposedEnd, double pps)
    {
        double snapRadius = ResizeSnapPixels / pps;
        double minEnd = item.StartTime + Math.Min(GetGridStepInSeconds(), 1.0);

        double best = proposedEnd;
        double bestDist = snapRadius + 1;

        void TrySnap(double candidate)
        {
            if (candidate < minEnd) return;
            double d = Math.Abs(proposedEnd - candidate);
            if (d < bestDist) { bestDist = d; best = candidate; }
        }

        // Grid snap
        double gridStep = GetGridStepInSeconds();
        if (gridStep > 0)
        {
            double nearest = NormalizeTimelineValue(Math.Round(proposedEnd / gridStep) * gridStep);
            TrySnap(nearest);
            TrySnap(NormalizeTimelineValue(nearest - gridStep));
            TrySnap(NormalizeTimelineValue(nearest + gridStep));
        }

        // Snap to task boundaries (start/end) of non-chain tasks in the same lane
        var chainIds = new HashSet<Guid>(_dragTouchingChain.Select(t => t.Id)) { item.Id };
        var others = ItemsSource?
            .Where(i => !chainIds.Contains(i.Id) && i.LaneId == item.LaneId)
            .ToList() ?? new List<ItemViewModel>();

        foreach (var other in others)
        {
            TrySnap(other.StartTime);
            TrySnap(other.StartTime + other.Duration);
        }

        return NormalizeTimelineValue(best);
    }

    // ── Drag & Drop ───────────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsRenaming) return;

        var pos = e.GetPosition(RootCanvas);
        var item = HitTestItem(pos);
        if (item == null)
        {
            var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
            int laneIdx = GetLaneIndex(pos.Y);
            if (laneIdx < 0 || laneIdx >= lanes.Count || AddItemAtFunc == null)
            {
                SelectedItem = null;
                e.Handled = true;
                return;
            }

            // Click = select cell; drag = create task
            // Floor (not round) so clicking anywhere inside a cell selects that cell
            double gridStep = GetGridStepInSeconds();
            double startTime = NormalizeTimelineValue(
                Math.Floor(Math.Max(0, pos.X / GetPixelsPerSecond()) / gridStep) * gridStep);
            CursorTime = startTime;
            CursorLaneIndex = laneIdx;
            SelectedItem = null;

            _createStartTime = startTime;
            _createLaneIdx = laneIdx;
            _pendingMouseDownPos = pos;
            _pendingDrag = PendingDragType.Create;
            RootCanvas.CaptureMouse();
            e.Handled = true;
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
        _pendingMouseDownPos = pos;

        if (_barRects.TryGetValue(item.Id, out r) && pos.X >= r.Right - ResizeW)
        {
            double itemEnd = item.StartTime + item.Duration;
            _dragLaneOriginalStarts = ItemsSource?
                .Where(i => i.LaneId == item.LaneId && i.Id != item.Id &&
                            i.StartTime >= itemEnd - 1e-9)
                .ToDictionary(i => i.Id, i => i.StartTime)
                ?? new Dictionary<Guid, double>();

            _dragTouchingChain = new List<ItemViewModel>();
            double chainFront = itemEnd;
            foreach (var t in (ItemsSource ?? Enumerable.Empty<ItemViewModel>())
                         .Where(i => i.LaneId == item.LaneId && i.Id != item.Id &&
                                     i.StartTime >= itemEnd - 1e-9)
                         .OrderBy(i => i.StartTime))
            {
                if (t.StartTime > chainFront + 1e-9) break;
                if (Math.Abs(t.StartTime - chainFront) < 1e-9)
                {
                    _dragTouchingChain.Add(t);
                    chainFront = t.StartTime + t.Duration;
                }
            }

            _pendingDrag = PendingDragType.Resize;
        }
        else
        {
            _dragMouseOffsetX = _barRects.TryGetValue(item.Id, out var barRect)
                ? (pos.X - barRect.Left) / GetPixelsPerSecond()
                : 0;
            _pendingDrag = PendingDragType.Move;
        }

        RootCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void StartPendingDrag()
    {
        switch (_pendingDrag)
        {
            case PendingDragType.Create:
                _createCurrentDuration = 0;
                _createCurrentMouseY = _pendingMouseDownPos.Y;
                _drag = DragMode.Create;
                break;
            case PendingDragType.Move:
                _drag = DragMode.Move;
                break;
            case PendingDragType.Resize:
                _drag = DragMode.Resize;
                break;
        }
        _pendingDrag = PendingDragType.None;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(RootCanvas);
        UpdateTimelineCursor(pos);

        // Promote pending drag to active once the mouse moves past the threshold
        if (_pendingDrag != PendingDragType.None)
        {
            double dx = pos.X - _pendingMouseDownPos.X;
            double dy = pos.Y - _pendingMouseDownPos.Y;
            if (dx * dx + dy * dy >= DragThreshold * DragThreshold)
                StartPendingDrag();
            else
                return;
        }

        if (_drag == DragMode.Create)
        {
            _createCurrentMouseY = pos.Y;
            double cpps = GetPixelsPerSecond();
            double rawEnd = pos.X / cpps;
            double rawDur = rawEnd - _createStartTime;
            double minDur = Math.Max(GetGridStepInSeconds(), 1.0);
            _createCurrentDuration = rawDur > 0
                ? Math.Max(minDur, SnapToSeconds(rawDur))
                : 0;
            Render();
            return;
        }

        if (_drag == DragMode.None) return;
        if (_dragItem == null) return;

        double pps = GetPixelsPerSecond();

        if (_drag == DragMode.Move)
        {
            var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
            if (lanes.Count == 0) return;

            double rawStart = NormalizeTimelineValue(pos.X / pps - _dragMouseOffsetX);
            int rawLi = GetLaneIndex(pos.Y);

            if (rawLi >= lanes.Count && AddLaneFunc != null)
            {
                _dragToNewLane = true;
                _dragLaneIdx = lanes.Count;
                _dragItem.StartTime = Math.Max(0, SnapToSeconds(rawStart));
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
            // Restore lane to original positions before recomputing each frame
            if (ItemsSource != null)
                foreach (var i in ItemsSource)
                    if (_dragLaneOriginalStarts.TryGetValue(i.Id, out var orig))
                        i.StartTime = orig;

            double rawEnd     = NormalizeTimelineValue(pos.X / pps);
            double snappedEnd = SnapResizeEnd(_dragItem, rawEnd, pps);
            double rawDur     = snappedEnd - _dragItem.StartTime;
            double minDuration = Math.Min(GetGridStepInSeconds(), 1.0);
            _dragItem.Duration = Math.Max(minDuration, NormalizeTimelineValue(rawDur));

            // Move touching chain items together (follows anchor both when growing and shrinking)
            double front = _dragItem.StartTime + _dragItem.Duration;
            foreach (var t in _dragTouchingChain)
            {
                t.StartTime = NormalizeTimelineValue(front);
                front = t.StartTime + t.Duration;
            }

            // Push non-chain items that became overlapped (only when growing closes a gap)
            var chainIds = new HashSet<Guid>(_dragTouchingChain.Select(t => t.Id)) { _dragItem.Id };
            var lastAnchor = _dragTouchingChain.Count > 0 ? _dragTouchingChain[^1] : _dragItem;
            PushSuccessors(lastAnchor, chainIds);
        }

        Render();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingDrag != PendingDragType.None)
        {
            // Mouse released before drag threshold — treat as a plain click
            _pendingDrag = PendingDragType.None;
            _dragItem = null;
            if (RootCanvas.IsMouseCaptured) RootCanvas.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

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
        _reorderMouseVisualY = e.GetPosition(FrozenLaneCanvas).Y;
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
        else if (_drag == DragMode.Create)
        {
            var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
            int finalLane = GetLaneIndex(_createCurrentMouseY);
            if (finalLane >= 0 && finalLane < lanes.Count && _createCurrentDuration > 0 && AddItemAtFunc != null)
            {
                var newItem = AddItemAtFunc(lanes[_createLaneIdx].Id, _createStartTime);
                if (newItem != null)
                {
                    newItem.Duration = _createCurrentDuration;
                    SelectedItem = newItem;
                    StartTaskRename(newItem, discardOnCancel: true);
                }
            }
            _createCurrentDuration = 0;
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

        _pendingDrag = PendingDragType.None;
        _dragToNewLane = false;
        _drag = DragMode.None;
        _dragItem = null;
        _dragLaneOriginalStarts.Clear();
        _dragTouchingChain.Clear();

        if (RootCanvas.IsMouseCaptured) RootCanvas.ReleaseMouseCapture();
        if (FrozenLaneCanvas.IsMouseCaptured) FrozenLaneCanvas.ReleaseMouseCapture();

        Render();
    }

    private void UpdateTimelineCursor(Point pos)
    {
        if (_drag == DragMode.Create)
        {
            RootCanvas.Cursor = Cursors.Cross;
            return;
        }
        if (_drag != DragMode.None) return;

        var item = HitTestItem(pos);
        if (item == null)
        {
            var lanes = Lanes?.ToList() ?? new List<LaneViewModel>();
            int li = GetLaneIndex(pos.Y);
            RootCanvas.Cursor = (li >= 0 && li < lanes.Count) ? Cursors.Cross : Cursors.Arrow;
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
        FrozenLaneCanvas.Cursor = Cursors.Arrow;
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

    // ── Right-click context menu ──────────────────────────────────────────

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (IsRenaming || _drag != DragMode.None) return;

        var pos = e.GetPosition(RootCanvas);
        var item = HitTestItem(pos);
        if (item == null) return;

        SelectedItem = item;

        var menu = BuildBarContextMenu(item);
        menu.PlacementTarget = RootCanvas;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;

        e.Handled = true;
    }

    private ContextMenu BuildBarContextMenu(ItemViewModel item)
    {
        var palette = ThemeService.CurrentPalette;
        var menu = new ContextMenu();

        // ── Category ─────────────────────────────────────────────────────
        menu.Items.Add(MakeMenuLabel("カテゴリ", palette));

        var allCategories = Enumerable.Repeat(CategoryViewModel.None, 1)
            .Concat(Categories ?? Enumerable.Empty<CategoryViewModel>());

        foreach (var cat in allCategories)
        {
            var catCapture = cat;
            bool isSelected = cat.IsNone ? item.CategoryId == Guid.Empty : item.CategoryId == cat.Id;

            var dot = new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = cat.IsNone ? Brushes.Transparent : cat.Brush,
                BorderBrush = cat.IsNone ? palette.Border : Brushes.Transparent,
                BorderThickness = new Thickness(cat.IsNone ? 1 : 0),
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(dot);
            sp.Children.Add(new TextBlock { Text = cat.Name, VerticalAlignment = VerticalAlignment.Center });

            var mi = new MenuItem { Header = sp };
            if (isSelected)
                mi.Icon = new TextBlock { Text = "✓", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            mi.Click += (_, _) => item.CategoryId = catCapture.IsNone ? Guid.Empty : catCapture.Id;
            menu.Items.Add(mi);
        }

        menu.Items.Add(new Separator());

        // ── Pre-conditions ────────────────────────────────────────────────
        menu.Items.Add(MakeMenuLabel("前提条件", palette));
        foreach (var cond in item.PreConditions.ToList())
        {
            var c = cond;
            var mi = new MenuItem
            {
                Header = c.Value,
                Icon = new TextBlock { Text = "×", Foreground = palette.Danger, VerticalAlignment = VerticalAlignment.Center },
            };
            mi.Click += (_, _) => item.PreConditions.Remove(c);
            menu.Items.Add(mi);
        }
        menu.Items.Add(MakeAddConditionItem(menu, palette, val => item.PreConditions.Add(new ConditionEntry(val))));

        menu.Items.Add(new Separator());

        // ── Post-conditions ───────────────────────────────────────────────
        menu.Items.Add(MakeMenuLabel("事後条件", palette));
        foreach (var cond in item.PostConditions.ToList())
        {
            var c = cond;
            var mi = new MenuItem
            {
                Header = c.Value,
                Icon = new TextBlock { Text = "×", Foreground = palette.Danger, VerticalAlignment = VerticalAlignment.Center },
            };
            mi.Click += (_, _) => item.PostConditions.Remove(c);
            menu.Items.Add(mi);
        }
        menu.Items.Add(MakeAddConditionItem(menu, palette, val => item.PostConditions.Add(new ConditionEntry(val))));

        return menu;
    }

    private static MenuItem MakeMenuLabel(string text, ThemePalette palette) => new()
    {
        Header = text,
        IsEnabled = false,
        FontSize = 10,
        Foreground = palette.TextSecondary,
    };

    private static MenuItem MakeAddConditionItem(ContextMenu menu, ThemePalette palette, Action<string> onAdd)
    {
        var tb = new TextBox
        {
            Width = 160,
            Height = 22,
            FontSize = 11,
            Background = palette.Surface,
            Foreground = palette.TextPrimary,
            BorderBrush = palette.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        tb.PreviewKeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Return)
            {
                var val = tb.Text.Trim();
                if (!string.IsNullOrEmpty(val)) onAdd(val);
                menu.IsOpen = false;
                ke.Handled = true;
            }
            else if (ke.Key != Key.Tab)
            {
                ke.Handled = true;
            }
        };
        tb.PreviewMouseDown += (_, me) => me.Handled = true;

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = "＋ ",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = palette.TextSecondary,
        });
        sp.Children.Add(tb);

        var mi = new MenuItem { Header = sp, StaysOpenOnClick = true };
        mi.Click += (_, _) => tb.Focus();
        return mi;
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

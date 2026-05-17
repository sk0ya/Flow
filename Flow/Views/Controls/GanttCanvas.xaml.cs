using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
    public double PixelsPerUnit
    { get => (double)GetValue(PixelsPerUnitProperty); set => SetValue(PixelsPerUnitProperty, value); }

    // ── Layout constants ──────────────────────────────────────────────────
    private const double LaneHeaderW  = 150;
    private const double LaneH        = 36;
    private const double BarH         = 28;
    private const double TimeHeaderH  = 30;
    private const double ResizeW      = 8;
    private const double MinBarW      = 4;

    // ── Colors ────────────────────────────────────────────────────────────
    private static readonly Brush BgWhite     = Brushes.White;
    private static readonly Brush LaneHdrBg   = new SolidColorBrush(Color.FromRgb(248, 249, 250));
    private static readonly Brush TimeHdrBg   = new SolidColorBrush(Color.FromRgb(242, 244, 248));
    private static readonly Brush EvenRow     = new SolidColorBrush(Color.FromArgb(15, 66, 133, 244));
    private static readonly Brush GridMinor   = new SolidColorBrush(Color.FromRgb(235, 237, 242));
    private static readonly Brush GridMajor   = new SolidColorBrush(Color.FromRgb(210, 215, 225));
    private static readonly Brush Divider     = new SolidColorBrush(Color.FromRgb(200, 204, 212));
    private static readonly Brush NormalBar   = new SolidColorBrush(Color.FromRgb(66, 133, 244));
    private static readonly Brush ErrorBarFg  = new SolidColorBrush(Color.FromRgb(229, 115, 115));
    private static readonly Brush ArrowBr     = new SolidColorBrush(Color.FromArgb(140, 90, 90, 90));
    private static readonly Brush DarkText    = new SolidColorBrush(Color.FromRgb(32, 33, 36));
    private static readonly Brush MutedText   = new SolidColorBrush(Color.FromRgb(95, 99, 104));
    private static readonly Brush DropLine    = new SolidColorBrush(Color.FromRgb(66, 133, 244));

    // ── Drag state ────────────────────────────────────────────────────────
    private ItemViewModel? _dragItem;
    private enum DragMode { None, Move, Resize }
    private DragMode _drag = DragMode.None;
    private double _dragStartMouseX, _dragStartMouseY;
    private double _dragOriginStart;   // item's StartTime at drag begin
    private double _dragOriginDuration;
    private double _dragMouseOffsetX;  // mouse X offset within the bar (for Move)
    private int    _dragLaneIdx;

    // ── Cached rects ─────────────────────────────────────────────────────
    private readonly Dictionary<Guid, Rect> _barRects = new();

    public GanttCanvas()
    {
        InitializeComponent();
        RootCanvas.PreviewMouseLeftButtonDown += OnMouseDown;
        RootCanvas.MouseMove           += OnMouseMove;
        RootCanvas.MouseLeftButtonUp   += OnMouseUp;
        RootCanvas.MouseLeave          += (_, _) => CommitDrag();
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

    // ── Render ────────────────────────────────────────────────────────────

    public void Render()
    {
        RootCanvas.Children.Clear();
        _barRects.Clear();

        var items = ItemsSource?.ToList() ?? new();
        var lanes = Lanes?.ToList() ?? new();
        if (lanes.Count == 0) lanes.Add(new LaneViewModel("レーン 1"));

        double ppu   = PixelsPerUnit;
        double total = Math.Max(TotalDuration, 1);
        int    nLanes = lanes.Count;
        double totalW = LaneHeaderW + total * ppu + 20;
        double totalH = TimeHeaderH + nLanes * LaneH + 20;

        // 1. White background
        Add(Rect(totalW, totalH, BgWhite), 0, 0);

        // 2. Time header background
        Add(Rect(totalW, TimeHeaderH, TimeHdrBg), 0, 0);

        // 3. Lane header background
        Add(Rect(LaneHeaderW, totalH, LaneHdrBg), 0, 0);

        // 4. Alternating lane backgrounds
        for (int i = 0; i < nLanes; i++)
            if (i % 2 == 0)
                Add(Rect(totalW - LaneHeaderW, LaneH, EvenRow), LaneHeaderW, TimeHeaderH + i * LaneH);

        // 5. Minor grid lines at 0.5 unit intervals
        for (double t = 0.5; t < total; t += 0.5)
        {
            if (Math.Abs(t % 1) < 0.01) continue; // skip major (drawn separately)
            double x = LaneHeaderW + t * ppu;
            Add(VLine(x, TimeHeaderH, totalH, GridMinor, 0.5), 0, 0);
        }

        // 6. Major vertical grid lines at each integer unit
        for (int t = 0; t <= (int)total; t++)
        {
            double x = LaneHeaderW + t * ppu;
            Add(VLine(x, 0, totalH, t == 0 ? Divider : GridMajor, t == 0 ? 1 : 0.8), 0, 0);

            // Time label
            Add(new TextBlock
            {
                Text = t.ToString(), FontSize = 11,
                Foreground = MutedText, Width = 40, TextAlignment = TextAlignment.Center,
            }, x - 20, (TimeHeaderH - 15) / 2);
        }

        // Unit label at top right of header
        Add(new TextBlock
        {
            Text = $"（{TimeUnit}）", FontSize = 10, Foreground = MutedText,
        }, LaneHeaderW + 4, (TimeHeaderH - 14) / 2);

        // 7. Horizontal lane separators
        for (int i = 0; i <= nLanes; i++)
            Add(HLine(0, totalW, TimeHeaderH + i * LaneH, GridMajor, i == 0 ? 1 : 0.6), 0, 0);

        // 8. Header bottom divider
        Add(HLine(0, totalW, TimeHeaderH, Divider, 1), 0, 0);

        // 9. Lane header column divider
        Add(VLine(LaneHeaderW, 0, totalH, Divider, 1), 0, 0);

        // 10. "タスク" label in header
        Add(new TextBlock
        {
            Text = "タスク", FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = MutedText, Width = LaneHeaderW, TextAlignment = TextAlignment.Center,
        }, 0, (TimeHeaderH - 15) / 2);

        // 11. Lane labels
        var laneIndexMap = new Dictionary<Guid, int>();
        for (int i = 0; i < nLanes; i++)
        {
            laneIndexMap[lanes[i].Id] = i;
            double rowY = TimeHeaderH + i * LaneH;

            // Selected highlight for lane of selected item
            bool isActiveLane = SelectedItem != null && SelectedItem.LaneId == lanes[i].Id;

            Add(new TextBlock
            {
                Text = lanes[i].Name, FontSize = 12,
                FontWeight = isActiveLane ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = DarkText,
                Width = LaneHeaderW - 16, TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }, 4, rowY + (LaneH - 15) / 2);
        }

        // 12. Compute bar rects
        var itemMap = items.ToDictionary(i => i.Id);
        foreach (var item in items)
        {
            int li = laneIndexMap.TryGetValue(item.LaneId, out var idx) ? idx : 0;
            double bx = LaneHeaderW + item.StartTime * ppu;
            double by = TimeHeaderH + li * LaneH + (LaneH - BarH) / 2.0;
            double bw = Math.Max(item.Duration * ppu, MinBarW);
            _barRects[item.Id] = new Rect(bx, by, bw, BarH);
        }

        // 13. Dependency arrows (drawn before bars)
        DrawArrows(itemMap);

        // 14. Task bars
        foreach (var item in items.OrderBy(i => i.StartTime))
        {
            bool isGhost = _drag != DragMode.None && _dragItem?.Id == item.Id;
            DrawBar(item, isGhost);
        }

        // 15. Drag ghost indicator
        if (_drag != DragMode.None && _dragItem != null)
            DrawDragGhost(_dragItem, laneIndexMap, ppu);

        RootCanvas.Width  = totalW;
        RootCanvas.Height = totalH;
    }

    // ── Bar rendering ─────────────────────────────────────────────────────

    private void DrawBar(ItemViewModel item, bool ghost)
    {
        if (!_barRects.TryGetValue(item.Id, out var r)) return;
        bool selected = SelectedItem?.Id == item.Id;

        Brush fill = item.HasErrors
            ? new SolidColorBrush(Color.FromArgb(200, 229, 115, 115))
            : NormalBar;

        var bar = new Border
        {
            Width  = r.Width, Height = r.Height,
            Background = fill, CornerRadius = new CornerRadius(5),
            Opacity = ghost ? 0.22 : 1.0,
            BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(30, 30, 30))
                : (item.HasErrors ? ErrorBarFg : Brushes.Transparent),
            BorderThickness = selected ? new Thickness(2) : new Thickness(item.HasErrors ? 1.5 : 0),
            Cursor = Cursors.SizeAll,
            ToolTip = BuildTooltip(item),
            ClipToBounds = true,
        };

        // Name label inside bar
        if (r.Width > 24)
        {
            bar.Child = new TextBlock
            {
                Text = item.Name, FontSize = 11, Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(7, 0, ResizeW + 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }

        Add(bar, r.Left, r.Top);

        // Resize handle (right edge)
        if (!ghost && r.Width > ResizeW * 2)
        {
            var handle = new Border
            {
                Width = ResizeW, Height = r.Height,
                Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                CornerRadius = new CornerRadius(0, 5, 5, 0),
                Cursor = Cursors.SizeWE,
            };
            Add(handle, r.Right - ResizeW, r.Top);
        }
    }

    private void DrawDragGhost(ItemViewModel item, Dictionary<Guid, int> laneMap, double ppu)
    {
        if (!_barRects.TryGetValue(item.Id, out var r)) return;

        // Current drag position bar
        double ghostX = LaneHeaderW + item.StartTime * ppu;
        double ghostW = Math.Max(item.Duration * ppu, MinBarW);
        double ghostY = TimeHeaderH + _dragLaneIdx * LaneH + (LaneH - BarH) / 2.0;

        Add(new Border
        {
            Width = ghostW, Height = BarH,
            Background = new SolidColorBrush(Color.FromArgb(100, 66, 133, 244)),
            CornerRadius = new CornerRadius(5),
            BorderBrush = DropLine, BorderThickness = new Thickness(2),
        }, ghostX, ghostY);
    }

    // ── Arrows ────────────────────────────────────────────────────────────

    private void DrawArrows(Dictionary<Guid, ItemViewModel> itemMap)
    {
        var edges = Edges?.ToList() ?? new();
        foreach (var edge in edges)
        {
            if (!_barRects.TryGetValue(edge.FromId, out var src)) continue;
            if (!_barRects.TryGetValue(edge.ToId,   out var dst)) continue;

            bool timeOk = itemMap.TryGetValue(edge.FromId, out var fi) &&
                          itemMap.TryGetValue(edge.ToId,   out var ti) &&
                          (fi!.StartTime + fi.Duration) <= ti!.StartTime + 1e-9;

            var stroke = timeOk
                ? new SolidColorBrush(Color.FromArgb(140, 60, 120, 200))
                : ErrorBarFg;
            double sw = timeOk ? 1.5 : 2;

            double x1 = src.Right, y1 = src.Top + src.Height / 2;
            double x2 = dst.Left,  y2 = dst.Top  + dst.Height / 2;

            var fig = new PathFigure { StartPoint = new(x1, y1) };
            if (Math.Abs(y1 - y2) < 2)
            {
                fig.Segments.Add(new LineSegment(new(x2 - 8, y2), true));
            }
            else
            {
                double cx = Math.Min(40, Math.Abs(x2 - x1) * 0.5);
                fig.Segments.Add(new BezierSegment(new(x1 + cx, y1), new(x2 - cx, y2), new(x2 - 8, y2), true));
            }

            Add(new Path
            {
                Data = new PathGeometry(new[] { fig }),
                Stroke = stroke, StrokeThickness = sw,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeDashArray = timeOk ? null : new DoubleCollection { 4, 2 },
            }, 0, 0);

            Add(new Polygon
            {
                Fill = stroke,
                Points = new PointCollection { new(x2, y2), new(x2-8, y2-4), new(x2-8, y2+4) },
            }, 0, 0);

            // Condition label
            if (!string.IsNullOrEmpty(edge.Condition))
            {
                var lbl = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                    BorderBrush = GridMajor, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 1, 3, 1),
                    Child = new TextBlock
                    {
                        Text = edge.Condition.Length > 16 ? edge.Condition[..13] + "…" : edge.Condition,
                        FontSize = 9, Foreground = timeOk ? MutedText : ErrorBarFg,
                    },
                };
                Add(lbl, (x1 + x2) / 2 - 28, (y1 + y2) / 2 - 11);
            }
        }
    }

    // ── Collision avoidance ───────────────────────────────────────────────

    // Returns a StartTime that fits the item in targetLaneId without overlapping other items,
    // while staying as close as possible to proposedStart.
    private double FindValidStart(ItemViewModel item, double proposedStart, Guid targetLaneId)
    {
        double dur    = item.Duration;
        var    others = ItemsSource?
            .Where(i => i.Id != item.Id && i.LaneId == targetLaneId)
            .OrderBy(i => i.StartTime)
            .ToList() ?? new();

        // Build free gaps: list of (gapStart, gapEnd)
        var gaps = new List<(double s, double e)>();
        double cursor = 0;
        foreach (var o in others)
        {
            if (o.StartTime > cursor + 1e-9)
                gaps.Add((cursor, o.StartTime));
            cursor = Math.Max(cursor, o.StartTime + o.Duration);
        }
        gaps.Add((cursor, double.MaxValue));

        // Keep only gaps wide enough for the item
        var validGaps = gaps.Where(g => g.e - g.s >= dur - 1e-9).ToList();
        if (validGaps.Count == 0) return Math.Max(0, proposedStart);

        // Pick the gap that minimises |clampedStart - proposedStart|
        double best = validGaps[0].s;
        double bestDist = double.MaxValue;
        foreach (var g in validGaps)
        {
            double clamped = Math.Clamp(proposedStart, g.s, g.e - dur);
            double dist    = Math.Abs(proposedStart - clamped);
            if (dist < bestDist) { bestDist = dist; best = clamped; }
        }
        return Math.Max(0, best);
    }

    // Returns a Duration that doesn't cause the item to overlap the next item in its lane.
    private double FindValidDuration(ItemViewModel item, double proposedDuration)
    {
        var next = ItemsSource?
            .Where(i => i.Id != item.Id && i.LaneId == item.LaneId
                                        && i.StartTime >= item.StartTime - 1e-9)
            .OrderBy(i => i.StartTime)
            .FirstOrDefault();

        double maxDur = next != null ? next.StartTime - item.StartTime : double.MaxValue;
        return Math.Clamp(proposedDuration, 0.5, Math.Max(0.5, maxDur));
    }

    // ── Drag & Drop ───────────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(RootCanvas);
        var item = HitTestItem(pos);
        if (item == null) { SelectedItem = null; return; }

        SelectedItem = item;
        _dragItem = item;
        _dragStartMouseX = pos.X;
        _dragStartMouseY = pos.Y;
        _dragOriginStart    = item.StartTime;
        _dragOriginDuration = item.Duration;
        _dragLaneIdx = GetLaneIndex(pos.Y);

        if (_barRects.TryGetValue(item.Id, out var r) && pos.X >= r.Right - ResizeW)
            _drag = DragMode.Resize;
        else
        {
            _drag = DragMode.Move;
            _dragMouseOffsetX = _barRects.TryGetValue(item.Id, out var br)
                ? (pos.X - br.Left) / PixelsPerUnit : 0;
        }

        RootCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(RootCanvas);

        // Update cursor
        UpdateCursor(pos);

        if (_drag == DragMode.None || _dragItem == null) return;

        double ppu = PixelsPerUnit;

        if (_drag == DragMode.Move)
        {
            var lanes = Lanes?.ToList() ?? new();
            if (lanes.Count == 0) return;

            int li = Math.Clamp(GetLaneIndex(pos.Y), 0, lanes.Count - 1);
            _dragLaneIdx = li;
            var targetLaneId = lanes[li].Id;

            double rawStart = (pos.X - LaneHeaderW) / ppu - _dragMouseOffsetX;
            rawStart = Snap(rawStart);
            double validStart = FindValidStart(_dragItem, rawStart, targetLaneId);

            _dragItem.StartTime = validStart;
            _dragItem.LaneId    = targetLaneId;
        }
        else if (_drag == DragMode.Resize)
        {
            double rawEnd = Snap((pos.X - LaneHeaderW) / ppu);
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

    private void CommitDrag()
    {
        _drag     = DragMode.None;
        _dragItem = null;
        RootCanvas.ReleaseMouseCapture();
        Render();
    }

    private void UpdateCursor(Point pos)
    {
        if (_drag != DragMode.None) return;
        var item = HitTestItem(pos);
        if (item == null) { RootCanvas.Cursor = Cursors.Arrow; return; }
        if (_barRects.TryGetValue(item.Id, out var r) && pos.X >= r.Right - ResizeW)
            RootCanvas.Cursor = Cursors.SizeWE;
        else
            RootCanvas.Cursor = Cursors.SizeAll;
    }

    // ── Hit testing ───────────────────────────────────────────────────────

    private ItemViewModel? HitTestItem(Point pos)
    {
        var items = ItemsSource?.ToList() ?? new();
        // Reverse order so top-most (last rendered) is checked first
        foreach (var item in items.OrderByDescending(i => i.StartTime))
            if (_barRects.TryGetValue(item.Id, out var r) && r.Contains(pos))
                return item;
        return null;
    }

    private int GetLaneIndex(double y) => (int)((y - TimeHeaderH) / LaneH);

    private static double Snap(double value, double snap = 0.5) =>
        Math.Round(value / snap) * snap;

    // ── Tooltip ───────────────────────────────────────────────────────────

    private ToolTip BuildTooltip(ItemViewModel item)
    {
        var sp = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
        sp.Children.Add(new TextBlock { Text = item.Name, FontWeight = FontWeights.Bold, FontSize = 13 });
        sp.Children.Add(new TextBlock
        {
            Text = $"開始: {item.StartTime:F1} {TimeUnit}  ／  所要: {item.Duration:F1} {TimeUnit}  ／  終了: {item.StartTime + item.Duration:F1} {TimeUnit}",
            FontSize = 10, Foreground = MutedText, Margin = new Thickness(0, 2, 0, 0),
        });
        if (!string.IsNullOrWhiteSpace(item.Description))
            sp.Children.Add(new TextBlock
            {
                Text = item.Description, FontSize = 11, Foreground = MutedText, Margin = new Thickness(0, 3, 0, 0),
            });
        if (item.HasErrors)
            sp.Children.Add(new TextBlock
            {
                Text = "⚠ " + item.ErrorMessage, Foreground = ErrorBarFg,
                FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 280,
            });
        return new ToolTip { Content = sp };
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void Add(UIElement el, double x, double y)
    { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); RootCanvas.Children.Add(el); }

    private static Rectangle Rect(double w, double h, Brush fill) =>
        new() { Width = w, Height = h, Fill = fill };

    private static Line VLine(double x, double y1, double y2, Brush stroke, double sw) =>
        new() { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = stroke, StrokeThickness = sw };

    private static Line HLine(double x1, double x2, double y, Brush stroke, double sw) =>
        new() { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = stroke, StrokeThickness = sw };
}

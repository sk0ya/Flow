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
    private const double AddLaneZoneH = 32;

    // ── Colors ────────────────────────────────────────────────────────────
    private static readonly Brush BgWhite    = Brushes.White;
    private static readonly Brush LaneHdrBg  = new SolidColorBrush(Color.FromRgb(248, 249, 250));
    private static readonly Brush TimeHdrBg  = new SolidColorBrush(Color.FromRgb(242, 244, 248));
    private static readonly Brush EvenRow    = new SolidColorBrush(Color.FromArgb(15, 66, 133, 244));
    private static readonly Brush GridMinor  = new SolidColorBrush(Color.FromRgb(235, 237, 242));
    private static readonly Brush GridMajor  = new SolidColorBrush(Color.FromRgb(210, 215, 225));
    private static readonly Brush Divider    = new SolidColorBrush(Color.FromRgb(200, 204, 212));
    private static readonly Brush NormalBar  = new SolidColorBrush(Color.FromRgb(66, 133, 244));
    private static readonly Brush ErrorBarFg = new SolidColorBrush(Color.FromRgb(229, 115, 115));
    private static readonly Brush DarkText   = new SolidColorBrush(Color.FromRgb(32, 33, 36));
    private static readonly Brush MutedText  = new SolidColorBrush(Color.FromRgb(95, 99, 104));
    private static readonly Brush DropLine   = new SolidColorBrush(Color.FromRgb(66, 133, 244));

    // ── Drag state ────────────────────────────────────────────────────────
    private ItemViewModel? _dragItem;
    private enum DragMode { None, Move, Resize, LaneReorder }
    private DragMode _drag = DragMode.None;
    private double _dragStartMouseY;
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

    // Callbacks set in code-behind
    public Func<Guid>?         AddLaneFunc           { get; set; }
    public Action<int, int>?   ReorderLanesCallback  { get; set; }

    // ── Cached rects ─────────────────────────────────────────────────────
    private readonly Dictionary<Guid, Rect> _barRects = new();

    public GanttCanvas()
    {
        InitializeComponent();
        RootCanvas.PreviewMouseLeftButtonDown += OnMouseDown;
        RootCanvas.MouseMove                  += OnMouseMove;
        RootCanvas.MouseLeftButtonUp          += OnMouseUp;
        RootCanvas.MouseLeave                 += (_, _) => CommitDrag();
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
        if (_renamingLane != null) return; // preserve TextBox overlay while renaming

        RootCanvas.Children.Clear();
        _barRects.Clear();

        var items = ItemsSource?.ToList() ?? new();
        var lanes = Lanes?.ToList() ?? new();
        if (lanes.Count == 0) lanes.Add(new LaneViewModel("レーン 1"));

        double ppu    = PixelsPerUnit;
        double total  = Math.Max(TotalDuration, 1);
        int    nLanes = lanes.Count;
        double totalW = LaneHeaderW + total * ppu + 20;
        double totalH = TimeHeaderH + nLanes * LaneH + AddLaneZoneH + 20;

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

        // 5. Minor grid lines
        for (double t = 0.5; t < total; t += 0.5)
        {
            if (Math.Abs(t % 1) < 0.01) continue;
            double x = LaneHeaderW + t * ppu;
            Add(VLine(x, TimeHeaderH, totalH, GridMinor, 0.5), 0, 0);
        }

        // 6. Major vertical grid lines and time labels
        for (int t = 0; t <= (int)total; t++)
        {
            double x = LaneHeaderW + t * ppu;
            Add(VLine(x, 0, totalH, t == 0 ? Divider : GridMajor, t == 0 ? 1 : 0.8), 0, 0);
            Add(new TextBlock
            {
                Text = t.ToString(), FontSize = 11,
                Foreground = MutedText, Width = 40, TextAlignment = TextAlignment.Center,
            }, x - 20, (TimeHeaderH - 15) / 2);
        }

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

        // 10. "タスク" column header
        Add(new TextBlock
        {
            Text = "タスク", FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = MutedText, Width = LaneHeaderW, TextAlignment = TextAlignment.Center,
        }, 0, (TimeHeaderH - 15) / 2);

        // 11. Lane labels with drag-grip hint
        var laneIndexMap = new Dictionary<Guid, int>();
        for (int i = 0; i < nLanes; i++)
        {
            laneIndexMap[lanes[i].Id] = i;
            double rowY = TimeHeaderH + i * LaneH;
            bool isActiveLane    = SelectedItem != null && SelectedItem.LaneId == lanes[i].Id;
            bool isReorderSource = _drag == DragMode.LaneReorder && _reorderSourceLane == i;

            // Grip icon
            Add(new TextBlock
            {
                Text = "⠿", FontSize = 10, Cursor = Cursors.SizeNS,
                Foreground = new SolidColorBrush(Color.FromArgb(100, 95, 99, 104)),
            }, 5, rowY + (LaneH - 14) / 2);

            // Lane name
            Add(new TextBlock
            {
                Text = lanes[i].Name, FontSize = 12,
                FontWeight = isActiveLane ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = DarkText,
                Opacity = isReorderSource ? 0.3 : 1.0,
                Width = LaneHeaderW - 20, TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }, 18, rowY + (LaneH - 15) / 2);

            // Dim overlay on source row during reorder
            if (isReorderSource)
                Add(new Rectangle
                {
                    Width = totalW, Height = LaneH,
                    Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                }, 0, rowY);
        }

        // 11b. Lane reorder drop indicator
        if (_drag == DragMode.LaneReorder && _reorderDropLane >= 0)
        {
            double lineY = TimeHeaderH + _reorderDropLane * LaneH;
            Add(new Ellipse { Width = 10, Height = 10, Fill = DropLine }, 1, lineY - 5);
            Add(new Rectangle { Width = totalW - 2, Height = 2, Fill = DropLine }, 11, lineY - 1);
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

        // 13. Dependency arrows (before bars)
        DrawArrows(itemMap);

        // 14. Task bars
        foreach (var item in items.OrderBy(i => i.StartTime))
        {
            bool isGhost = _drag == DragMode.Move && _dragItem?.Id == item.Id;
            DrawBar(item, isGhost);
        }

        // 15. Drag ghost indicator
        if (_drag == DragMode.Move && _dragItem != null)
            DrawDragGhost(_dragItem, ppu);

        // 16. "Add lane" zone at the bottom
        DrawAddLaneZone(totalW);

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

    private void DrawDragGhost(ItemViewModel item, double ppu)
    {
        if (!_barRects.ContainsKey(item.Id)) return;

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

    private void DrawAddLaneZone(double totalW)
    {
        var lanes = Lanes?.ToList() ?? new();
        double zoneY  = TimeHeaderH + lanes.Count * LaneH;
        bool   active = _dragToNewLane;

        Brush hdrBg  = active ? new SolidColorBrush(Color.FromArgb(55, 66, 133, 244)) : new SolidColorBrush(Color.FromRgb(245, 247, 250));
        Brush zoneBg = active ? new SolidColorBrush(Color.FromArgb(40, 66, 133, 244)) : new SolidColorBrush(Color.FromArgb(10, 66, 133, 244));
        Brush border = active ? DropLine : new SolidColorBrush(Color.FromRgb(220, 225, 235));
        Brush txtFg  = active ? DropLine : new SolidColorBrush(Color.FromRgb(148, 158, 178));

        var hdr = new Border
        {
            Width = LaneHeaderW, Height = AddLaneZoneH,
            Background = hdrBg, BorderBrush = border,
            BorderThickness = new Thickness(0, 1, 1, 0),
            Cursor = AddLaneFunc != null ? Cursors.Hand : Cursors.Arrow,
            Child = new TextBlock
            {
                Text = "+ 新しいレーン", FontSize = 10, Foreground = txtFg,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            }
        };
        var zone = new Border
        {
            Width = totalW - LaneHeaderW, Height = AddLaneZoneH,
            Background = zoneBg, BorderBrush = border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Cursor = AddLaneFunc != null ? Cursors.Hand : Cursors.Arrow,
        };

        if (AddLaneFunc != null)
        {
            hdr.MouseLeftButtonDown  += OnAddLaneZoneClick;
            zone.MouseLeftButtonDown += OnAddLaneZoneClick;
        }

        Add(hdr,  0,           zoneY);
        Add(zone, LaneHeaderW, zoneY);
    }

    private void OnAddLaneZoneClick(object sender, MouseButtonEventArgs e)
    {
        if (_drag != DragMode.None) return;
        AddLaneFunc?.Invoke();
        e.Handled = true;
    }

    // ── Lane rename ───────────────────────────────────────────────────────

    private void StartLaneRename(LaneViewModel lane, int laneIndex)
    {
        _renamingLane = lane;

        double boxY = TimeHeaderH + laneIndex * LaneH + (LaneH - 24) / 2.0;

        _renameBox = new TextBox
        {
            Text = lane.Name,
            Width = LaneHeaderW - 10, Height = 24,
            FontSize = 11,
            Padding = new Thickness(5, 2, 5, 2),
            BorderBrush = DropLine, BorderThickness = new Thickness(1.5),
            Background = Brushes.White,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _renameBox.KeyDown   += OnRenameKeyDown;
        _renameBox.LostFocus += OnRenameLostFocus;

        Canvas.SetLeft(_renameBox, 4);
        Canvas.SetTop(_renameBox, boxY);
        RootCanvas.Children.Add(_renameBox);
        _renameBox.Focus();
        _renameBox.SelectAll();
    }

    private void CommitRename(bool cancel = false)
    {
        if (_renamingLane == null || _renameBox == null) return;
        if (!cancel)
        {
            var name = _renameBox.Text.Trim();
            if (!string.IsNullOrEmpty(name)) _renamingLane.Name = name;
        }
        RootCanvas.Children.Remove(_renameBox);
        _renameBox    = null;
        _renamingLane = null;
        Render();
    }

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if      (e.Key == Key.Return) { CommitRename();              e.Handled = true; }
        else if (e.Key == Key.Escape) { CommitRename(cancel: true);  e.Handled = true; }
    }

    private void OnRenameLostFocus(object sender, RoutedEventArgs e) => CommitRename();

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

    private double FindValidStart(ItemViewModel item, double proposedStart, Guid targetLaneId)
    {
        double dur    = item.Duration;
        var    others = ItemsSource?
            .Where(i => i.Id != item.Id && i.LaneId == targetLaneId)
            .OrderBy(i => i.StartTime)
            .ToList() ?? new();

        var gaps   = new List<(double s, double e)>();
        double cur = 0;
        foreach (var o in others)
        {
            if (o.StartTime > cur + 1e-9) gaps.Add((cur, o.StartTime));
            cur = Math.Max(cur, o.StartTime + o.Duration);
        }
        gaps.Add((cur, double.MaxValue));

        var validGaps = gaps.Where(g => g.e - g.s >= dur - 1e-9).ToList();
        if (validGaps.Count == 0) return Math.Max(0, proposedStart);

        double best = validGaps[0].s, bestDist = double.MaxValue;
        foreach (var g in validGaps)
        {
            double clamped = Math.Clamp(proposedStart, g.s, g.e - dur);
            double dist    = Math.Abs(proposedStart - clamped);
            if (dist < bestDist) { bestDist = dist; best = clamped; }
        }
        return Math.Max(0, best);
    }

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
        if (_renamingLane != null) return;

        var pos = e.GetPosition(RootCanvas);

        // ── Lane header area (left strip, below time header) ──────────────
        if (pos.X < LaneHeaderW && pos.Y >= TimeHeaderH)
        {
            int laneIdx = GetLaneIndex(pos.Y);
            var lanes   = Lanes?.ToList() ?? new();

            if (laneIdx >= 0 && laneIdx < lanes.Count)
            {
                if (e.ClickCount == 2)
                {
                    StartLaneRename(lanes[laneIdx], laneIdx);
                    e.Handled = true;
                    return;
                }
                _reorderSourceLane = laneIdx;
                _reorderDropLane   = laneIdx;
                _dragStartMouseY   = pos.Y;
                _drag = DragMode.LaneReorder;
                RootCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }
            return; // click in time header or add-lane zone header
        }

        // ── Item hit test ─────────────────────────────────────────────────
        var item = HitTestItem(pos);
        if (item == null) { SelectedItem = null; return; }

        SelectedItem        = item;
        _dragItem           = item;
        _dragStartMouseY    = pos.Y;
        _dragOriginStart    = item.StartTime;
        _dragOriginDuration = item.Duration;
        _dragLaneIdx        = GetLaneIndex(pos.Y);

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
        UpdateCursor(pos);

        if (_drag == DragMode.None) return;

        if (_drag == DragMode.LaneReorder)
        {
            var lanes  = Lanes?.ToList() ?? new();
            int rawLi  = GetLaneIndex(pos.Y);
            double localY = (pos.Y - TimeHeaderH) - rawLi * LaneH;
            int dropPos   = localY < LaneH / 2.0 ? rawLi : rawLi + 1;
            _reorderDropLane = Math.Clamp(dropPos, 0, lanes.Count);
            Render();
            return;
        }

        if (_dragItem == null) return;

        double ppu = PixelsPerUnit;

        if (_drag == DragMode.Move)
        {
            var lanes = Lanes?.ToList() ?? new();
            if (lanes.Count == 0) return;

            double rawStart = (pos.X - LaneHeaderW) / ppu - _dragMouseOffsetX;
            rawStart = Snap(rawStart);

            int rawLi = GetLaneIndex(pos.Y);
            if (rawLi >= lanes.Count && AddLaneFunc != null)
            {
                _dragToNewLane      = true;
                _dragLaneIdx        = lanes.Count;
                _dragItem.StartTime = Math.Max(0, rawStart);
            }
            else
            {
                _dragToNewLane = false;
                int li = Math.Clamp(rawLi, 0, lanes.Count - 1);
                _dragLaneIdx = li;
                var targetLaneId = lanes[li].Id;
                _dragItem.StartTime = FindValidStart(_dragItem, rawStart, targetLaneId);
                _dragItem.LaneId    = targetLaneId;
            }
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
        if (_drag == DragMode.LaneReorder)
        {
            var lanes = Lanes?.ToList() ?? new();
            int from  = _reorderSourceLane;
            int to    = _reorderDropLane <= from ? _reorderDropLane : _reorderDropLane - 1;
            if (from != to && from >= 0 && from < lanes.Count && to >= 0 && to < lanes.Count)
                ReorderLanesCallback?.Invoke(from, to);
            _reorderSourceLane = -1;
            _reorderDropLane   = -1;
        }
        else if (_dragToNewLane && _dragItem != null && AddLaneFunc != null)
        {
            var newLaneId = AddLaneFunc();
            _dragItem.LaneId = newLaneId;
        }

        _dragToNewLane = false;
        _drag          = DragMode.None;
        _dragItem      = null;
        RootCanvas.ReleaseMouseCapture();
        Render();
    }

    private void UpdateCursor(Point pos)
    {
        if (_drag != DragMode.None) return;

        // Lane header area → reorder cursor
        if (pos.X < LaneHeaderW && pos.Y >= TimeHeaderH)
        {
            int laneIdx = GetLaneIndex(pos.Y);
            var lanes   = Lanes?.ToList() ?? new();
            RootCanvas.Cursor = (laneIdx >= 0 && laneIdx < lanes.Count) ? Cursors.SizeNS : Cursors.Arrow;
            return;
        }

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

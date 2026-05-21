using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Flow.ViewModels;

namespace Flow;

public partial class MainWindow : Window
{
    private TextBox? _activeCondBox;
    private bool     _isPre;
    private bool     _suppressPopup;
    private double   _savedSidebarWidth = 270.0;

    // ── Vim state ─────────────────────────────────────────────────────────
    private readonly VimEngine    _vim          = new();
    private readonly VimClipboard _vimClipboard = new();

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startupProjectPath)
    {
        InitializeComponent();
        var vm = new MainViewModel(startupProjectPath);
        DataContext = vm;
        GanttView.AddLaneFunc          = vm.AddNewLane;
        GanttView.AddItemAtFunc        = vm.AddNewItemAt;
        GanttView.DiscardItemFunc      = vm.DiscardNewItem;
        GanttView.ReorderLanesCallback = vm.ReorderLane;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSidebarOpen))
                UpdateSidebarColumns(vm);
        };
        vm.ProjectLoaded += (_, _) => GanttView.RequestAutoFitLaneHeader();

        InitVim();
    }

    private void UpdateSidebarColumns(MainViewModel vm)
    {
        var sidebarCol  = MainContentGrid.ColumnDefinitions[1];
        var splitterCol = MainContentGrid.ColumnDefinitions[2];

        if (vm.IsSidebarOpen)
        {
            // Restore MinWidth before setting Width to avoid Min > Max conflict
            sidebarCol.MinWidth = 160;
            sidebarCol.Width    = new GridLength(Math.Clamp(_savedSidebarWidth, 160, 400));
            splitterCol.Width   = new GridLength(5);
        }
        else
        {
            // Capture current pixel width; Width.Value is reliable after layout
            double w = sidebarCol.Width.IsAbsolute
                ? sidebarCol.Width.Value
                : sidebarCol.ActualWidth;
            if (w >= 160)
                _savedSidebarWidth = w;

            sidebarCol.MinWidth = 0;
            sidebarCol.Width    = new GridLength(0);
            splitterCol.Width   = new GridLength(0);
        }
    }

    private void OnSidebarSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        // Keep _savedSidebarWidth in sync whenever the user resizes the sidebar
        var col = MainContentGrid.ColumnDefinitions[1];
        if (col.ActualWidth >= 160)
            _savedSidebarWidth = col.ActualWidth;
    }

    private void OnActivityBarClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not RadioButton rb) return;

        var panel = rb.Tag?.ToString() switch
        {
            "ProjectList"     => SidebarPanel.ProjectList,
            "ProjectSettings" => SidebarPanel.ProjectSettings,
            "TaskEditor"      => SidebarPanel.TaskEditor,
            "AppSettings"     => SidebarPanel.AppSettings,
            _                 => SidebarPanel.ProjectList,
        };

        vm.ToggleOrActivatePanel(panel);
    }

    private void OnMinimizeWindow(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void OnToggleMaximizeWindow(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
            return;
        }

        SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    // ── Autocomplete popup ────────────────────────────────────────────────

    private void OnPreCondInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressPopup && sender is TextBox tb)
            ShowSuggestions(tb, isPre: true);
    }

    private void OnPostCondInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressPopup && sender is TextBox tb)
            ShowSuggestions(tb, isPre: false);
    }

    private void ShowSuggestions(TextBox tb, bool isPre)
    {
        if (DataContext is not MainViewModel vm) return;
        if (tb.DataContext is not ItemViewModel itemVm) return;

        _activeCondBox = tb;
        _isPre = isPre;

        var text = tb.Text;
        List<SuggestionItem> suggestions = isPre
            ? vm.GetPreSuggestions(itemVm.Id, text)
            : vm.GetPostSuggestions(itemVm.Id, text);

        if (suggestions.Count > 0)
        {
            SuggestionsListBox.ItemsSource = suggestions;
            SuggestionHeader.Text = isPre ? "他のItemの事後条件から選ぶ" : "他のItemの事前条件から選ぶ";
            SuggestionsPopup.PlacementTarget = tb;
            SuggestionsPopup.MinWidth = Math.Max(tb.ActualWidth + 4, 200);
            SuggestionsPopup.IsOpen = true;
        }
        else
        {
            SuggestionsPopup.IsOpen = false;
        }
    }

    private void OnCondInputLostFocus(object sender, RoutedEventArgs e)
    {
        // Delay closing so a click on the popup list can register
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (!SuggestionsListBox.IsMouseOver)
                SuggestionsPopup.IsOpen = false;
        });
    }

    private void OnSuggestionClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is SuggestionItem suggestion)
        {
            AcceptSuggestion(suggestion.Value);
            e.Handled = true;
        }
        else if (e.OriginalSource is FrameworkElement fe)
        {
            var item = fe.DataContext as SuggestionItem;
            if (item == null) return;
            AcceptSuggestion(item.Value);
            e.Handled = true;
        }
    }

    private void AcceptSuggestion(string value)
    {
        if (_activeCondBox == null) return;

        _suppressPopup = true;

        if (_activeCondBox.DataContext is ItemViewModel itemVm)
        {
            if (_isPre)
            {
                itemVm.NewPreCondition = value;
                itemVm.AddPreConditionCommand.Execute(null);
            }
            else
            {
                itemVm.NewPostCondition = value;
                itemVm.AddPostConditionCommand.Execute(null);
            }
        }

        SuggestionsPopup.IsOpen = false;
        _activeCondBox.Focus();
        _suppressPopup = false;
    }

    // Keyboard navigation inside the suggestion popup
    private void OnCondInputKeyDown(object sender, KeyEventArgs e)
    {
        if (!SuggestionsPopup.IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                SuggestionsListBox.Focus();
                if (SuggestionsListBox.Items.Count > 0)
                    SuggestionsListBox.SelectedIndex = 0;
                e.Handled = true;
                break;

            case Key.Escape:
                SuggestionsPopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // Confirm selected suggestion with Enter while ListBox is focused
        if (e.Key == Key.Return && SuggestionsListBox.IsKeyboardFocusWithin
                                && SuggestionsListBox.SelectedItem is SuggestionItem sel)
        {
            AcceptSuggestion(sel.Value);
            e.Handled = true;
            return;
        }

        // Return focus from ListBox back to input on arrow-up from first item
        if (e.Key == Key.Up && SuggestionsListBox.IsKeyboardFocusWithin
                             && SuggestionsListBox.SelectedIndex == 0)
        {
            _activeCondBox?.Focus();
            e.Handled = true;
            return;
        }

        // Global shortcut: Escape deselects
        if (e.Key == Key.Escape)
        {
            if (DataContext is MainViewModel vm)
                vm.SelectedItem = null;
        }

        // Vim keybindings (skip when a text input has focus or inline editor is open)
        if (!e.Handled && !IsTextInputFocused() && !GanttView.IsEditing)
            HandleVimKey(e);
    }

    private static bool IsTextInputFocused() =>
        Keyboard.FocusedElement is TextBox or PasswordBox;

    private void InitVim()
    {
        _vim.Init();

        _vim.Register("h",   ctx => VimNavigate(ctx,  0, -1));
        _vim.Register("l",   ctx => VimNavigate(ctx,  0, +1));
        _vim.Register("k",   ctx => VimNavigate(ctx, -1,  0));
        _vim.Register("j",   ctx => VimNavigate(ctx, +1,  0));

        _vim.Register("i",   ctx => { if (ctx.ViewModel.SelectedItem != null) ctx.GanttView.StartRenameSelectedItem(); });
        _vim.Register("I",   ctx => VimAddTask(ctx, VimAddMode.Start));
        _vim.Register("a",   ctx => VimAddTask(ctx, VimAddMode.After));
        _vim.Register("o",   ctx => VimAddTask(ctx, VimAddMode.LaneBelow));
        _vim.Register("O",   ctx => VimAddTask(ctx, VimAddMode.LaneAbove));

        _vim.Register("p",   ctx => VimPaste(ctx));
        _vim.Register("yiw", ctx => VimYank(ctx, isLane: false));
        _vim.Register("yy",  ctx => VimYank(ctx, isLane: true));
        _vim.Register("diw", ctx => { if (ctx.ViewModel.SelectedItem != null) ctx.ViewModel.DeleteSelectedItemCommand.Execute(null); });
        _vim.Register("dd",  ctx => VimDeleteLane(ctx.ViewModel));
    }

    private void HandleVimKey(KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (_vim.HandleKey(e.Key, shift, new VimContext(vm, GanttView, _vimClipboard)))
            e.Handled = true;
    }

    // laneDir: -1 = up, +1 = down, 0 = same lane  /  itemDir: -1 = prev, +1 = next
    private static void VimNavigate(VimContext ctx, int laneDir, int itemDir)
    {
        var vm    = ctx.ViewModel;
        var items = vm.Items;
        var lanes = vm.Lanes;
        if (items.Count == 0) return;

        var selected = vm.SelectedItem;
        if (selected == null)
        {
            vm.SelectedItem = items.OrderBy(i => i.StartTime).FirstOrDefault();
            return;
        }

        if (laneDir != 0)
        {
            int laneIdx = FindLaneIndex(lanes, selected.LaneId);
            int step    = laneDir > 0 ? 1 : -1;
            for (int i = laneIdx + step; step > 0 ? i < lanes.Count : i >= 0; i += step)
            {
                var nearest = items
                    .Where(x => x.LaneId == lanes[i].Id)
                    .OrderBy(x => Math.Abs(x.StartTime - selected.StartTime))
                    .FirstOrDefault();
                if (nearest != null) { vm.SelectedItem = nearest; break; }
            }
        }
        else
        {
            var laneItems = items
                .Where(i => i.LaneId == selected.LaneId)
                .OrderBy(i => i.StartTime)
                .ToList();
            int idx  = laneItems.FindIndex(i => i.Id == selected.Id);
            int next = idx + itemDir;
            if (next >= 0 && next < laneItems.Count)
                vm.SelectedItem = laneItems[next];
        }
    }

    private enum VimAddMode { After, Start, LaneBelow, LaneAbove }

    private static void VimAddTask(VimContext ctx, VimAddMode mode)
    {
        var vm       = ctx.ViewModel;
        var selected = vm.SelectedItem;
        var lanes    = vm.Lanes;
        Guid   laneId;
        double startTime;

        switch (mode)
        {
            case VimAddMode.After:
                laneId    = selected?.LaneId ?? lanes.FirstOrDefault()?.Id ?? Guid.Empty;
                startTime = selected != null ? selected.StartTime + selected.Duration : 0;
                break;
            case VimAddMode.Start:
                laneId    = selected?.LaneId ?? lanes.FirstOrDefault()?.Id ?? Guid.Empty;
                startTime = 0;
                break;
            case VimAddMode.LaneBelow:
            {
                int idx = selected != null ? FindLaneIndex(lanes, selected.LaneId) : lanes.Count - 1;
                laneId    = idx + 1 < lanes.Count ? lanes[idx + 1].Id : vm.InsertLaneAfter(idx);
                startTime = selected?.StartTime ?? 0;
                break;
            }
            case VimAddMode.LaneAbove:
            {
                int idx = selected != null ? FindLaneIndex(lanes, selected.LaneId) : 0;
                if (idx <= 0) return;
                laneId    = lanes[idx - 1].Id;
                startTime = selected?.StartTime ?? 0;
                break;
            }
            default: return;
        }

        if (laneId == Guid.Empty) return;
        var newItem = vm.AddNewItemAt(laneId, startTime);
        if (newItem != null) ctx.GanttView.StartRenameSelectedItem(discardOnCancel: true);
    }

    private static void VimYank(VimContext ctx, bool isLane)
    {
        var vm = ctx.ViewModel;
        var cb = ctx.Clipboard;

        if (!isLane)
        {
            if (vm.SelectedItem == null) return;
            cb.YankTask(vm.SelectedItem.ToModel());
        }
        else
        {
            var laneId = vm.SelectedItem?.LaneId ?? vm.Lanes.FirstOrDefault()?.Id ?? Guid.Empty;
            var lane   = vm.Lanes.FirstOrDefault(l => l.Id == laneId);
            if (lane == null) return;
            cb.YankLane(
                lane.ToModel(),
                vm.Items.Where(i => i.LaneId == laneId).Select(i => i.ToModel()).ToList());
        }
    }

    private static void VimPaste(VimContext ctx)
    {
        var vm = ctx.ViewModel;
        var cb = ctx.Clipboard;

        if (cb.Kind == VimClipboard.ClipKind.Task && cb.Task != null)
        {
            var sel    = vm.SelectedItem;
            var laneId = sel?.LaneId ?? vm.Lanes.FirstOrDefault()?.Id ?? Guid.Empty;
            if (laneId == Guid.Empty) return;
            double start = sel != null ? sel.StartTime + sel.Duration : 0;
            vm.PasteItem(cb.Task, laneId, start);
        }
        else if (cb.Kind == VimClipboard.ClipKind.Lane && cb.Lane.HasValue)
        {
            int idx = vm.SelectedItem != null
                ? FindLaneIndex(vm.Lanes, vm.SelectedItem.LaneId)
                : vm.Lanes.Count - 1;
            if (idx < 0) idx = vm.Lanes.Count - 1;
            vm.PasteLane(cb.Lane.Value.lane, cb.Lane.Value.items, idx);
        }
    }

    private static void VimDeleteLane(MainViewModel vm)
    {
        if (vm.SelectedItem == null) return;
        var lane = vm.Lanes.FirstOrDefault(l => l.Id == vm.SelectedItem.LaneId);
        if (lane != null) vm.DeleteLaneWithItems(lane);
    }

    private static int FindLaneIndex(
        System.Collections.ObjectModel.ObservableCollection<LaneViewModel> lanes, Guid id)
    {
        for (int i = 0; i < lanes.Count; i++)
            if (lanes[i].Id == id) return i;
        return -1;
    }

    // ── SelectItemCommand relay (click in list = select) ─────────────────

    private void OnSelectItemCommand(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is ItemViewModel vm
            && DataContext is MainViewModel mainVm)
        {
            mainVm.SelectedItem = vm;
        }
    }
}

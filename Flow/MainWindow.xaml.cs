using System;
using System.Collections.Generic;
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
    private LaneViewModel?        _pendingNewLane;

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
        GanttView.ReorderLanesCallback = vm.ReorderLane;

        GanttView.DiscardItemFunc = item =>
        {
            vm.DiscardNewItem(item);
            if (_pendingNewLane != null)
            {
                vm.Lanes.Remove(_pendingNewLane);
                _pendingNewLane = null;
            }
        };

        GanttView.ItemCreatedCommittedFunc = item =>
        {
            if (_pendingNewLane != null)
            {
                int laneIdx = vm.Lanes.IndexOf(_pendingNewLane);
                vm.UndoRedo.Push(new CompositeCommand([
                    new AddLaneCommand(vm.Lanes, _pendingNewLane, laneIdx),
                    new AddItemCommand(vm.Items, item),
                ]));
                _pendingNewLane = null;
            }
            else
            {
                vm.UndoRedo.Push(new AddItemCommand(vm.Items, item));
            }
        };

        GanttView.ItemRenamedFunc = (item, oldName, newName) =>
            vm.UndoRedo.Push(new PropertyChangeCommand<string>(v => item.Name = v, oldName, newName));

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSidebarOpen))
                UpdateSidebarColumns(vm);
        };
        vm.ProjectLoaded        += (_, _) => GanttView.RequestAutoFitLaneHeader();
        vm.StartRenameRequested += (_, _) => GanttView.StartRenameSelectedItem(discardOnCancel: true);

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

        // Navigation
        _vim.Register("h",   VimCommands.Left);
        _vim.Register("l",   VimCommands.Right);
        _vim.Register("k",   VimCommands.Up);
        _vim.Register("j",   VimCommands.Down);
        _vim.Register("gg",  VimCommands.GoFirst);
        _vim.Register("G",   VimCommands.GoLast);
        _vim.Register("^",   VimCommands.GoFirstTask);
        _vim.Register("w",   VimCommands.WordForward);
        _vim.Register("b",   VimCommands.WordBackward);
        _vim.Register("e",   VimCommands.WordEnd);
        _vim.Register("0",   VimCommands.GoLineStart);
        _vim.Register("$",   VimCommands.GoLineEnd);

        // Duration / move
        _vim.Register("+",   VimCommands.DurationGrow);
        _vim.Register("-",   VimCommands.DurationShrink);
        _vim.Register(">",   VimCommands.MoveTaskRight);
        _vim.Register("<",   VimCommands.MoveTaskLeft);

        // View
        _vim.Register("zz",  ctx => ctx.GanttView.ScrollCursorIntoCenter());

        // Edit
        _vim.Register("i",   VimCommands.Rename);
        _vim.Register("a",   ctx => VimAddTask(ctx, VimAddMode.After));
        _vim.Register("I",   ctx => VimAddTask(ctx, VimAddMode.Start));
        _vim.Register("o",   ctx => VimAddTask(ctx, VimAddMode.LaneBelow));
        _vim.Register("O",   ctx => VimAddTask(ctx, VimAddMode.LaneAbove));

        // Undo
        _vim.Register("u",   ctx => ctx.ViewModel.Undo());

        // Delete / yank / paste
        _vim.Register("x",   VimCommands.DeleteTask);
        _vim.Register("diw", VimCommands.DeleteTask);
        _vim.Register("yiw", VimCommands.YankTask);
        _vim.Register("yy",  VimCommands.YankLane);
        _vim.Register("dd",  VimCommands.DeleteLane);
        _vim.Register("p",   VimCommands.PasteAfter);
        _vim.Register("P",   VimCommands.PasteBefore);
    }

    private void HandleVimKey(KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;
        Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

        if (ctrl && key == Key.R)
        {
            vm.Redo();
            e.Handled = true;
            return;
        }

        if (_vim.HandleKey(key, shift, new VimContext(vm, GanttView, _vimClipboard)))
            e.Handled = true;
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        // IMEがONのとき、テキスト入力外ならIME変換結果を破棄する
        if (!IsTextInputFocused() && !GanttView.IsEditing)
            e.Handled = true;
    }

    private enum VimAddMode { After, Start, LaneBelow, LaneAbove }

    private void VimAddTask(VimContext ctx, VimAddMode mode)
    {
        var vm = ctx.ViewModel;
        Guid   laneId;
        double startTime;

        switch (mode)
        {
            case VimAddMode.After:
            {
                laneId    = ctx.CursorLaneId();
                var cur   = ctx.TaskAtCursor();
                startTime = cur != null ? cur.StartTime + cur.Duration : vm.CursorTime + ctx.GridStep;
                break;
            }
            case VimAddMode.Start:
                laneId    = ctx.CursorLaneId();
                startTime = 0;
                break;
            case VimAddMode.LaneBelow:
            {
                int idx = vm.CursorLaneIndex;
                if (idx + 1 < vm.Lanes.Count)
                {
                    laneId = vm.Lanes[idx + 1].Id;
                }
                else
                {
                    var newLane = vm.InsertLaneAfter(idx);
                    laneId          = newLane.Id;
                    _pendingNewLane = newLane;
                }
                startTime = vm.CursorTime;
                break;
            }
            case VimAddMode.LaneAbove:
                if (vm.CursorLaneIndex <= 0) return;
                laneId    = vm.Lanes[vm.CursorLaneIndex - 1].Id;
                startTime = vm.CursorTime;
                break;
            default:
                return;
        }

        if (laneId == Guid.Empty) return;
        var newItem = vm.AddNewItemAt(laneId, startTime);
        if (newItem != null) ctx.GanttView.StartRenameSelectedItem(discardOnCancel: true);
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

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Flow.ViewModels;
using Flow.Views.Controls;

namespace Flow;

public partial class MainWindow : Window
{
    private TextBox? _activeCondBox;
    private bool     _isPre;
    private bool     _suppressPopup;
    private double   _savedSidebarWidth = 270.0;
    private (LaneViewModel lane, int index)? _pendingLaneCreatedDuringMove;

    // ── Vim state ─────────────────────────────────────────────────────────
    private readonly VimController _vim;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startupProjectPath)
    {
        InitializeComponent();
        var vm = new MainViewModel(startupProjectPath);
        _vim = new VimController(vm, GanttView);
        _vim.SearchRequested += BeginSearch;
        DataContext = vm;
        GanttView.AddLaneFunc          = vm.AddNewLane;
        GanttView.LaneCreatedFunc      = (lane, index) =>
            vm.UndoRedo.Push(new AddLaneCommand(vm.Lanes, lane, index));
        GanttView.LaneCreatedDuringMoveFunc = (lane, index) =>
            _pendingLaneCreatedDuringMove = (lane, index);
        GanttView.AddItemAtFunc        = vm.AddNewItemAt;
        GanttView.ReorderLanesCallback = (from, to) =>
        {
            vm.ReorderLane(from, to);
            vm.UndoRedo.Push(new ReorderLaneCommand(vm.Lanes, from, to));
        };

        GanttView.DiscardItemFunc = _vim.HandleDiscardedNewItem;
        GanttView.ItemTimelineChangedFunc = changes =>
        {
            var commands = BuildTimelineCommands(changes);
            if (_pendingLaneCreatedDuringMove is { } pendingLane)
            {
                commands.Insert(0, new AddLaneCommand(vm.Lanes, pendingLane.lane, pendingLane.index));
                _pendingLaneCreatedDuringMove = null;
            }
            if (commands.Count > 0)
                vm.UndoRedo.Push(new CompositeCommand(commands));
        };
        GanttView.LaneRenamedFunc = (lane, oldName, newName) =>
            vm.UndoRedo.Push(new PropertyChangeCommand<string>(value => lane.Name = value, oldName, newName));

        GanttView.ItemCreatedCommittedFunc = item =>
        {
            if (!_vim.TryCommitPendingNewItem(item))
                vm.UndoRedo.Push(new AddItemCommand(vm.Items, item));
        };

        GanttView.ItemRenamedFunc = (item, oldName, newName) =>
            vm.UndoRedo.Push(new PropertyChangeCommand<string>(v => item.Name = v, oldName, newName));

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSidebarOpen))
                UpdateSidebarColumns(vm);
        };
        vm.ExportPngRequested += (_, _) => ExportPng();
        vm.ProjectLoaded        += (_, _) => GanttView.RequestAutoFitLaneHeader();
        vm.StartRenameRequested += (_, _) => GanttView.StartRenameSelectedItem(discardOnCancel: true);
        Closing += (_, e) =>
        {
            if (!vm.CanCloseWindow())
                e.Cancel = true;
        };
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
            "CanvasTools"     => SidebarPanel.CanvasTools,
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

    private List<IUndoableCommand> BuildTimelineCommands(IReadOnlyList<TimelineEditChange> changes)
    {
        var commands = new List<IUndoableCommand>();
        foreach (var change in changes)
        {
            if (Math.Abs(change.OldStartTime - change.NewStartTime) > 1e-9)
            {
                commands.Add(new PropertyChangeCommand<double>(
                    value => change.Item.StartTime = value,
                    change.OldStartTime,
                    change.NewStartTime));
            }

            if (Math.Abs(change.OldDuration - change.NewDuration) > 1e-9)
            {
                commands.Add(new PropertyChangeCommand<double>(
                    value => change.Item.Duration = value,
                    change.OldDuration,
                    change.NewDuration));
            }

            if (change.OldLaneId != change.NewLaneId)
            {
                commands.Add(new PropertyChangeCommand<Guid>(
                    value => change.Item.LaneId = value,
                    change.OldLaneId,
                    change.NewLaneId));
            }
        }

        return commands;
    }

    private void ExportPng()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png",
            DefaultExt = "png",
            FileName = $"{(DataContext as MainViewModel)?.ProjectName ?? "Flow"}.png"
        };

        if (dlg.ShowDialog() != true)
            return;

        GanttView.ExportViewportPng(dlg.FileName);
    }

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

    private void BeginSearch()
    {
        if (DataContext is not MainViewModel vm)
            return;

        vm.ActivatePanel(SidebarPanel.CanvasTools);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            CanvasSearchBox.Focus();
            CanvasSearchBox.SelectAll();
        });
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

        if (e.Key == Key.Escape && _vim.TryCancelPendingInput())
        {
            e.Handled = true;
            return;
        }

        // Visual mode: Escape exits visual mode (without deselecting)
        if (e.Key == Key.Escape && _vim.TryExitMode())
        {
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

    private void HandleVimKey(KeyEventArgs e)
    {
        Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (_vim.HandleKey(key, Keyboard.Modifiers))
            e.Handled = true;
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        // IMEがONのとき、テキスト入力外ならIME変換結果を破棄する
        if (!IsTextInputFocused() && !GanttView.IsEditing)
            e.Handled = true;
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

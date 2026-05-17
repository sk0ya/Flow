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

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        GanttView.AddLaneFunc          = vm.AddNewLane;
        GanttView.ReorderLanesCallback = vm.ReorderLane;
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

using System.Windows;
using Flow.ViewModels;

namespace Flow;

public partial class ProjectSettingsWindow : Window
{
    public ProjectSettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

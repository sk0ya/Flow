using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Flow.ViewModels;

namespace Flow.Views.Controls;

/// <summary>
/// Embeddable Flow editor surface. It intentionally does not create a Window or
/// draw a title bar, so a host application can place it in its own layout.
/// </summary>
public partial class FlowWorkspaceControl : UserControl
{
    public static readonly DependencyProperty FlowFilePathProperty =
        DependencyProperty.Register(
            nameof(FlowFilePath),
            typeof(string),
            typeof(FlowWorkspaceControl),
            new PropertyMetadata(null, OnFlowFilePathChanged));

    private MainViewModel? _viewModel;

    public FlowWorkspaceControl()
    {
        InitializeComponent();
        SetViewModel(new MainViewModel());
    }

    public FlowWorkspaceControl(string flowFilePath)
    {
        InitializeComponent();
        SetViewModel(new MainViewModel(NormalizeFlowPath(flowFilePath)));
        FlowFilePath = ViewModel.CurrentFilePath;
    }

    /// <summary>Path of the .flow document to open when the control is created.</summary>
    public string? FlowFilePath
    {
        get => (string?)GetValue(FlowFilePathProperty);
        set => SetValue(FlowFilePathProperty, value);
    }

    /// <summary>The live view model, available to the embedding tool for integration.</summary>
    public MainViewModel ViewModel => _viewModel ?? throw new InvalidOperationException("The workspace is not initialized.");

    /// <summary>Loads a .flow file and refreshes the timeline and inspector.</summary>
    public void Open(string flowFilePath)
    {
        var normalizedPath = NormalizeFlowPath(flowFilePath);
        if (string.Equals(ViewModel.CurrentFilePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            FlowFilePath = normalizedPath;
            return;
        }

        FlowFilePath = normalizedPath;
    }

    /// <summary>Saves the current document to its current path.</summary>
    public bool Save() => ViewModel.TrySaveProjectFromVim();

    private static void OnFlowFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FlowWorkspaceControl control || e.NewValue is not string path || string.IsNullOrWhiteSpace(path))
            return;

        control.LoadPath(path);
    }

    private void LoadPath(string path)
    {
        var normalizedPath = NormalizeFlowPath(path);
        if (_viewModel != null && string.Equals(_viewModel.CurrentFilePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            return;

        SetViewModel(new MainViewModel(normalizedPath));
    }

    private static string NormalizeFlowPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(normalizedPath), ".flow", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Flow project files must use the .flow extension.", nameof(path));
        return normalizedPath;
    }

    private void SetViewModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        GanttView.AddLaneFunc = viewModel.AddNewLane;
        GanttView.AddItemAtFunc = (laneId, startTime) => viewModel.AddNewItemAt(laneId, startTime);
        GanttView.DiscardItemFunc = viewModel.DiscardNewItem;
        GanttView.ReorderLanesCallback = viewModel.ReorderLane;
        GanttView.LaneRenamedFunc = (_, _, _) => viewModel.Analyze();
        GanttView.ItemTimelineChangedFunc = _ => viewModel.Analyze();
        viewModel.ProjectLoaded += (_, _) => GanttView.RequestAutoFitLaneHeader();
    }
}

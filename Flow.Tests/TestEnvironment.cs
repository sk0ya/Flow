using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Flow.ViewModels;
using Flow.Views.Controls;

namespace Flow.Tests;

internal static class TestEnvironment
{
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(CreateDispatcher);

    public static void RunInWpfContext(Action action)
        => RunInWpfContext(() =>
        {
            action();
            return true;
        });

    public static T RunInWpfContext<T>(Func<T> action)
    {
        var backup = AppStateBackup.Capture();
        try
        {
            backup.ClearCurrentState();
            return SharedDispatcher.Value.Invoke(action);
        }
        finally
        {
            SharedDispatcher.Value.Invoke(backup.Restore);
        }
    }

    public static MainViewModel CreateMainViewModel()
    {
        var viewModel = new MainViewModel
        {
            TimeUnit = "秒",
            CellDuration = 1,
            TotalDuration = 30,
        };

        return viewModel;
    }

    public static GanttCanvas CreateCanvas()
    {
        var canvas = new GanttCanvas
        {
            Width = 960,
            Height = 540,
        };

        canvas.Measure(new Size(canvas.Width, canvas.Height));
        canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        canvas.UpdateLayout();
        FlushDispatcher();
        return canvas;
    }

    public static GanttCanvas CreateBoundCanvas(MainViewModel viewModel)
    {
        var canvas = CreateCanvas();

        Bind(canvas, GanttCanvas.ItemsSourceProperty, viewModel, nameof(MainViewModel.Items));
        Bind(canvas, GanttCanvas.LanesProperty, viewModel, nameof(MainViewModel.Lanes));
        Bind(canvas, GanttCanvas.CategoriesProperty, viewModel, nameof(MainViewModel.Categories));
        Bind(canvas, GanttCanvas.EdgesProperty, viewModel, nameof(MainViewModel.DependencyEdges));
        Bind(canvas, GanttCanvas.SelectedItemProperty, viewModel, nameof(MainViewModel.SelectedItem), BindingMode.TwoWay);
        Bind(canvas, GanttCanvas.CursorLaneIndexProperty, viewModel, nameof(MainViewModel.CursorLaneIndex), BindingMode.TwoWay);
        Bind(canvas, GanttCanvas.CursorTimeProperty, viewModel, nameof(MainViewModel.CursorTime), BindingMode.TwoWay);
        Bind(canvas, GanttCanvas.TimeUnitProperty, viewModel, nameof(MainViewModel.TimeUnit));
        Bind(canvas, GanttCanvas.TotalDurationProperty, viewModel, nameof(MainViewModel.TotalDuration));
        Bind(canvas, GanttCanvas.CellDurationProperty, viewModel, nameof(MainViewModel.CellDuration));
        Bind(canvas, GanttCanvas.IsVisualModeProperty, viewModel, nameof(MainViewModel.IsVisualMode));
        Bind(canvas, GanttCanvas.IsVisualLineModeProperty, viewModel, nameof(MainViewModel.IsVisualLineMode));
        Bind(canvas, GanttCanvas.VisualAnchorLaneProperty, viewModel, nameof(MainViewModel.VisualAnchorLane));
        Bind(canvas, GanttCanvas.VisualAnchorTimeProperty, viewModel, nameof(MainViewModel.VisualAnchorTime));

        FlushDispatcher();
        return canvas;
    }

    public static void FlushDispatcher()
    {
        SharedDispatcher.Value.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static Dispatcher CreateDispatcher()
    {
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        ready.Wait();
        return dispatcher!;
    }

    private static void Bind(
        DependencyObject target,
        DependencyProperty property,
        object source,
        string path,
        BindingMode mode = BindingMode.OneWay)
    {
        BindingOperations.SetBinding(target, property, new Binding(path)
        {
            Source = source,
            Mode = mode,
        });
    }

    private sealed class AppStateBackup
    {
        private readonly string _path;
        private readonly byte[]? _content;
        private readonly bool _existed;

        private AppStateBackup(string path, byte[]? content, bool existed)
        {
            _path = path;
            _content = content;
            _existed = existed;
        }

        public static AppStateBackup Capture()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Flow");
            string path = Path.Combine(directory, "app-state.json");

            return File.Exists(path)
                ? new AppStateBackup(path, File.ReadAllBytes(path), existed: true)
                : new AppStateBackup(path, null, existed: false);
        }

        public void Restore()
        {
            if (_existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllBytes(_path, _content!);
                return;
            }

            if (File.Exists(_path))
                File.Delete(_path);
        }

        public void ClearCurrentState()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }
}

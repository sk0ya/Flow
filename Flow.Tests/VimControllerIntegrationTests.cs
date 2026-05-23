using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using Flow.Models;
using Flow.ViewModels;
using Flow.Views.Controls;

namespace Flow.Tests;

public sealed class VimControllerIntegrationTests
{
    [Fact]
    public void HandleKey_WhenRenameCommandInvoked_StartsTaskRenameEditor()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            var canvas = TestEnvironment.CreateBoundCanvas(viewModel);
            Guid laneId = viewModel.Lanes[0].Id;

            var item = viewModel.PasteItem(new SequenceItem
            {
                Name = "名前変更対象",
                Duration = 2,
            }, laneId, startTime: 0);
            canvas.Render();
            TestEnvironment.FlushDispatcher();

            var controller = new VimController(viewModel, canvas);
            bool handled = controller.HandleKey(Key.I, ModifierKeys.None);

            var renameBox = GetPrivateField<TextBox>(canvas, "_taskRenameBox");

            Assert.True(handled);
            Assert.True(canvas.IsEditing);
            Assert.NotNull(renameBox);
            Assert.Equal(item.Name, renameBox!.Text);
        });
    }

    [Fact]
    public void HandleKey_WhenYankAndPasteTaskPerformed_DuplicatesTaskAfterCurrentTask()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            Guid laneId = viewModel.Lanes[0].Id;
            var source = viewModel.PasteItem(new SequenceItem
            {
                Name = "複製元",
                Description = "詳細",
                Duration = 2,
                PreConditions = ["pre"],
                PostConditions = ["post"],
            }, laneId, startTime: 0);

            viewModel.CursorLaneIndex = 0;
            viewModel.CursorTime = source.StartTime;
            var controller = new VimController(viewModel, TestEnvironment.CreateCanvas());

            controller.HandleKey(Key.Y, ModifierKeys.None);
            controller.HandleKey(Key.I, ModifierKeys.None);
            controller.HandleKey(Key.W, ModifierKeys.None);
            bool handled = controller.HandleKey(Key.P, ModifierKeys.None);

            Assert.True(handled);
            Assert.Equal(2, viewModel.Items.Count);

            var pasted = Assert.Single(viewModel.Items, item => item.Id != source.Id);
            Assert.Equal(source.Name, pasted.Name);
            Assert.Equal(source.Description, pasted.Description);
            Assert.Equal(source.Duration, pasted.Duration);
            Assert.Equal(source.StartTime + source.Duration, pasted.StartTime);
            Assert.Equal(["pre"], pasted.PreConditions.Select(entry => entry.Value));
            Assert.Equal(["post"], pasted.PostConditions.Select(entry => entry.Value));
        });
    }

    [Fact]
    public void HandleKey_WhenAddingTaskOnNewLane_CommitSupportsUndoAndRedo()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            viewModel.CursorLaneIndex = 0;
            viewModel.CursorTime = 4;

            var controller = new VimController(viewModel, TestEnvironment.CreateCanvas());
            bool handled = controller.HandleKey(Key.O, ModifierKeys.None);

            Assert.True(handled);
            Assert.Equal(2, viewModel.Lanes.Count);
            Assert.Single(viewModel.Items);
            Assert.False(viewModel.IsTaskEditorActive);
            Assert.Null(viewModel.SelectedItem);

            var created = Assert.Single(viewModel.Items);
            Assert.Equal(viewModel.Lanes[1].Id, created.LaneId);

            Assert.True(controller.TryCommitPendingNewItem(created));

            viewModel.Undo();
            Assert.Single(viewModel.Lanes);
            Assert.Empty(viewModel.Items);

            viewModel.Redo();
            Assert.Equal(2, viewModel.Lanes.Count);
            Assert.Single(viewModel.Items);
        });
    }

    [Fact]
    public void HandleKey_WhenVisualLineDeleteExecuted_RemovesLaneRangeAndExitsVisualMode()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            viewModel.Lanes.Add(new("レーン 2"));
            viewModel.Lanes.Add(new("レーン 3"));

            viewModel.PasteItem(new SequenceItem { Name = "A", Duration = 1 }, viewModel.Lanes[0].Id, startTime: 0);
            viewModel.PasteItem(new SequenceItem { Name = "B", Duration = 1 }, viewModel.Lanes[1].Id, startTime: 0);
            viewModel.PasteItem(new SequenceItem { Name = "C", Duration = 1 }, viewModel.Lanes[2].Id, startTime: 0);
            viewModel.CursorLaneIndex = 0;
            viewModel.CursorTime = 0;
            viewModel.SelectedItem = viewModel.Items.First(item => item.Name == "A");

            var controller = new VimController(viewModel, TestEnvironment.CreateCanvas());

            controller.HandleKey(Key.V, ModifierKeys.Shift);
            controller.HandleKey(Key.J, ModifierKeys.None);
            bool handled = controller.HandleKey(Key.D, ModifierKeys.None);

            Assert.True(handled);
            Assert.False(viewModel.IsVisualMode);
            Assert.False(viewModel.IsVisualLineMode);
            Assert.Equal(string.Empty, viewModel.VisualModeLabel);
            Assert.Single(viewModel.Lanes);
            Assert.Single(viewModel.Items);
            Assert.Equal("レーン 3", viewModel.Lanes[0].Name);
            Assert.Equal("C", viewModel.Items[0].Name);
            Assert.Equal(0, viewModel.CursorLaneIndex);
        });
    }

    [Fact]
    public void HandleTextInput_WhenSlashPressed_EntersSearchPrompt()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            var controller = new VimController(viewModel, TestEnvironment.CreateCanvas());

            bool handled = controller.HandleTextInput("/");

            Assert.True(handled);
            Assert.True(viewModel.IsVimPromptActive);
            Assert.Equal("SEARCH", viewModel.VimModeLabel);
            Assert.Equal("/", viewModel.VimPromptText);
        });
    }

    [Fact]
    public void HandleTextInput_WhenSearchCommitted_UpdatesSearchText()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            var controller = new VimController(viewModel, TestEnvironment.CreateCanvas());

            controller.HandleTextInput("/");
            controller.HandleTextInput("task");
            bool handled = controller.HandleKey(Key.Return, ModifierKeys.None);

            Assert.True(handled);
            Assert.False(viewModel.IsVimPromptActive);
            Assert.Equal("NORMAL", viewModel.VimModeLabel);
            Assert.Equal("task", viewModel.SearchText);
        });
    }

    [Fact]
    public void HandleTextInput_WhenColonPressed_EntersCommandPrompt()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            var controller = new VimController(viewModel, TestEnvironment.CreateCanvas());

            bool handled = controller.HandleTextInput(":");

            Assert.True(handled);
            Assert.True(viewModel.IsVimPromptActive);
            Assert.Equal("COMMAND", viewModel.VimModeLabel);
            Assert.Equal(":", viewModel.VimPromptText);
        });
    }

    [Fact]
    public void HandleKey_WhenDotRepeatsDeleteWithoutCursorMove_RemovesNextTask()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            var canvas = TestEnvironment.CreateBoundCanvas(viewModel);
            Guid laneId = viewModel.Lanes[0].Id;

            viewModel.PasteItem(new SequenceItem { Name = "A", Duration = 1 }, laneId, startTime: 0);
            viewModel.PasteItem(new SequenceItem { Name = "B", Duration = 1 }, laneId, startTime: 1);
            viewModel.PasteItem(new SequenceItem { Name = "C", Duration = 1 }, laneId, startTime: 2);

            var controller = new VimController(viewModel, canvas);
            viewModel.CursorLaneIndex = 0;
            viewModel.CursorTime = 0;

            Assert.True(controller.HandleKey(Key.X, ModifierKeys.None));
            Assert.Equal(0, viewModel.CursorTime);
            Assert.Null(viewModel.SelectedItem);
            Assert.True(controller.HandleKey(Key.OemPeriod, ModifierKeys.None));

            Assert.Single(viewModel.Items);
            Assert.Equal("C", viewModel.Items[0].Name);
        });
    }

    [Fact]
    public void PreviewKeyDown_WhenDotRepeatsDelete_UsesMainWindowInputPath()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            var window = new MainWindow(viewModel)
            {
                Width = 1280,
                Height = 740,
                ShowInTaskbar = false,
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                TestEnvironment.FlushDispatcher();

                Guid laneId = viewModel.Lanes[0].Id;
                viewModel.PasteItem(new SequenceItem { Name = "A", Duration = 1 }, laneId, startTime: 0);
                viewModel.PasteItem(new SequenceItem { Name = "B", Duration = 1 }, laneId, startTime: 1);
                viewModel.PasteItem(new SequenceItem { Name = "C", Duration = 1 }, laneId, startTime: 2);
                TestEnvironment.FlushDispatcher();

                viewModel.CursorLaneIndex = 0;
                viewModel.CursorTime = 0;

                Assert.True(window.DispatchPreviewKeyForTest(Key.X));
                Assert.Equal(0, viewModel.CursorTime);
                Assert.Null(viewModel.SelectedItem);
                Assert.True(window.DispatchPreviewKeyForTest(Key.OemPeriod));

                Assert.Single(viewModel.Items);
                Assert.Equal("C", viewModel.Items[0].Name);
            }
            finally
            {
                window.Close();
                TestEnvironment.FlushDispatcher();
            }
        });
    }

    [Fact]
    public void HandleKey_WhenDotRepeatsCountedMove_ReusesOriginalCount()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            var canvas = TestEnvironment.CreateBoundCanvas(viewModel);
            Guid laneId = viewModel.Lanes[0].Id;

            var first = viewModel.PasteItem(new SequenceItem { Name = "A", Duration = 1 }, laneId, startTime: 0);
            var second = viewModel.PasteItem(new SequenceItem { Name = "B", Duration = 1 }, laneId, startTime: 5);
            canvas.Render();
            TestEnvironment.FlushDispatcher();

            var controller = new VimController(viewModel, canvas);
            viewModel.CursorLaneIndex = 0;
            viewModel.CursorTime = first.StartTime;

            Assert.True(controller.HandleKey(Key.D2, ModifierKeys.None));
            Assert.True(controller.HandleKey(Key.OemPeriod, ModifierKeys.Shift));
            Assert.Equal(2, first.StartTime);

            viewModel.CursorTime = second.StartTime;
            Assert.True(controller.HandleKey(Key.OemPeriod, ModifierKeys.None));
            Assert.Equal(7, second.StartTime);
        });
    }

    [Fact]
    public void HandleKey_WhenDotRepeatsCommittedCreate_AddsSameNamedTask()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            var canvas = TestEnvironment.CreateBoundCanvas(viewModel);
            Guid laneId = viewModel.Lanes[0].Id;

            viewModel.PasteItem(new SequenceItem { Name = "Base", Duration = 1 }, laneId, startTime: 0);
            canvas.Render();
            TestEnvironment.FlushDispatcher();

            var controller = new VimController(viewModel, canvas);
            WireVimCallbacks(viewModel, canvas, controller);

            viewModel.CursorLaneIndex = 0;
            viewModel.CursorTime = 0;
            Assert.True(controller.HandleKey(Key.A, ModifierKeys.None));

            var renameBox = GetPrivateField<TextBox>(canvas, "_taskRenameBox");
            Assert.NotNull(renameBox);
            renameBox!.Text = "Repeat Me";
            InvokePrivateMethod(canvas, "CommitTaskRename");

            Assert.Equal(2, viewModel.Items.Count);
            Assert.Equal(1, viewModel.Items.Count(item => item.Name == "Repeat Me"));

            Assert.True(controller.HandleKey(Key.OemPeriod, ModifierKeys.None));

            Assert.Equal(3, viewModel.Items.Count);
            Assert.Equal(2, viewModel.Items.Count(item => item.Name == "Repeat Me"));
        });
    }

    [Fact]
    public void HandleKey_WhenDotRepeatsCommittedRename_RenamesCurrentTaskToSameValue()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            var canvas = TestEnvironment.CreateBoundCanvas(viewModel);
            Guid laneId = viewModel.Lanes[0].Id;

            var first = viewModel.PasteItem(new SequenceItem { Name = "Old A", Duration = 1 }, laneId, startTime: 0);
            var second = viewModel.PasteItem(new SequenceItem { Name = "Old B", Duration = 1 }, laneId, startTime: 2);
            canvas.Render();
            TestEnvironment.FlushDispatcher();

            var controller = new VimController(viewModel, canvas);
            WireVimCallbacks(viewModel, canvas, controller);

            viewModel.CursorLaneIndex = 0;
            viewModel.CursorTime = first.StartTime;
            Assert.True(controller.HandleKey(Key.I, ModifierKeys.None));

            var renameBox = GetPrivateField<TextBox>(canvas, "_taskRenameBox");
            Assert.NotNull(renameBox);
            renameBox!.Text = "Renamed";
            InvokePrivateMethod(canvas, "CommitTaskRename");
            Assert.Equal("Renamed", first.Name);

            viewModel.CursorTime = second.StartTime;
            Assert.True(controller.HandleKey(Key.OemPeriod, ModifierKeys.None));
            Assert.Equal("Renamed", second.Name);
        });
    }

    private static T? GetPrivateField<T>(object target, string fieldName)
        where T : class
    {
        return typeof(GanttCanvas)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(target) as T;
    }

    private static void InvokePrivateMethod(object target, string methodName)
    {
        typeof(GanttCanvas)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)?
            .Invoke(target, [false]);
    }

    private static void WireVimCallbacks(MainViewModel viewModel, GanttCanvas canvas, VimController controller)
    {
        canvas.ItemCreatedCommittedFunc = item =>
        {
            if (!controller.TryCommitPendingNewItem(item))
                viewModel.UndoRedo.Push(new AddItemCommand(viewModel.Items, item));
            controller.HandleCommittedNewItem(item);
        };

        canvas.ItemRenamedFunc = (item, oldName, newName) =>
        {
            viewModel.UndoRedo.Push(new PropertyChangeCommand<string>(value => item.Name = value, oldName, newName));
            controller.HandleItemRenamed(item, oldName, newName);
        };
    }
}

using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using Flow.Models;
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

            var created = viewModel.SelectedItem;
            Assert.NotNull(created);
            Assert.Equal(viewModel.Lanes[1].Id, created!.LaneId);

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

    private static T? GetPrivateField<T>(object target, string fieldName)
        where T : class
    {
        return typeof(GanttCanvas)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(target) as T;
    }
}

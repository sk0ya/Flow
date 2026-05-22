using System;
using System.Collections.Generic;
using System.Linq;
using Flow.Models;
using Flow.ViewModels;

namespace Flow.Tests;

public sealed class MainViewModelTaskTests
{
    [Fact]
    public void AddNewItemAt_WhenRequestedStartOverlaps_MovesTaskToNextAvailableSlot()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            Guid laneId = viewModel.Lanes[0].Id;

            var existing = viewModel.PasteItem(new SequenceItem
            {
                Name = "既存タスク",
                Duration = 3,
            }, laneId, startTime: 0);

            var added = viewModel.AddNewItemAt(laneId, proposedStartTime: 1);

            Assert.NotNull(added);
            Assert.Equal(existing.StartTime + existing.Duration, added!.StartTime);
            Assert.Equal(laneId, added.LaneId);
            Assert.Same(added, viewModel.SelectedItem);
            Assert.True(viewModel.IsTaskEditorActive);
        });
    }

    [Fact]
    public void PasteItem_CopiesTaskDataAndAvoidsOverlap()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            Guid laneId = viewModel.Lanes[0].Id;

            viewModel.PasteItem(new SequenceItem
            {
                Name = "先頭タスク",
                Duration = 2,
            }, laneId, startTime: 0);

            Guid categoryId = Guid.NewGuid();
            var template = new SequenceItem
            {
                Id = Guid.NewGuid(),
                Name = "コピー元",
                Description = "説明",
                CategoryId = categoryId,
                Duration = 2,
                PreConditions = new List<string> { "pre-a", "pre-b" },
                PostConditions = new List<string> { "post-a" },
            };

            var pasted = viewModel.PasteItem(template, laneId, startTime: 1);

            Assert.NotEqual(template.Id, pasted.Id);
            Assert.Equal("コピー元", pasted.Name);
            Assert.Equal("説明", pasted.Description);
            Assert.Equal(categoryId, pasted.CategoryId);
            Assert.Equal(2, pasted.Duration);
            Assert.Equal(2, pasted.StartTime);
            Assert.Equal(["pre-a", "pre-b"], pasted.PreConditions.Select(entry => entry.Value));
            Assert.Equal(["post-a"], pasted.PostConditions.Select(entry => entry.Value));
        });
    }

    [Fact]
    public void PasteLane_InsertsLaneAndClonesItems()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            viewModel.Lanes.Add(new LaneViewModel("既存レーン"));

            var laneTemplate = new Lane
            {
                Id = Guid.NewGuid(),
                Name = "複製レーン",
            };
            var itemTemplates = new List<SequenceItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "A",
                    StartTime = 0,
                    Duration = 2,
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "B",
                    StartTime = 3,
                    Duration = 1,
                },
            };

            var (lane, items) = viewModel.PasteLane(laneTemplate, itemTemplates, afterIndex: 0);

            Assert.Equal(3, viewModel.Lanes.Count);
            Assert.Same(lane, viewModel.Lanes[1]);
            Assert.Equal("複製レーン", lane.Name);
            Assert.Equal(2, items.Count);
            Assert.All(items, item => Assert.Equal(lane.Id, item.LaneId));
            Assert.Equal(["A", "B"], items.OrderBy(item => item.StartTime).Select(item => item.Name));
            Assert.All(items.Zip(itemTemplates), pair => Assert.NotEqual(pair.Second.Id, pair.First.Id));
        });
    }

    [Fact]
    public void DeleteLaneWithItems_WhenMultipleLanes_RemovesLaneAndItsTasks()
    {
        TestEnvironment.RunInWpfContext(() =>
        {
            var viewModel = TestEnvironment.CreateMainViewModel();
            viewModel.Lanes.Add(new LaneViewModel("削除対象外"));

            var removedLane = viewModel.Lanes[0];
            var keptLane = viewModel.Lanes[1];

            var removedItem = viewModel.PasteItem(new SequenceItem
            {
                Name = "消えるタスク",
                Duration = 1,
            }, removedLane.Id, startTime: 0);
            viewModel.PasteItem(new SequenceItem
            {
                Name = "残るタスク",
                Duration = 1,
            }, keptLane.Id, startTime: 0);
            viewModel.SelectedItem = removedItem;

            viewModel.DeleteLaneWithItems(removedLane);

            Assert.Single(viewModel.Lanes);
            Assert.Equal(keptLane.Id, viewModel.Lanes[0].Id);
            Assert.DoesNotContain(viewModel.Items, item => item.Id == removedItem.Id);
            Assert.Null(viewModel.SelectedItem);
        });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Flow.Models;
using Flow.ViewModels;
using Flow.Views.Controls;

namespace Flow;

internal static class VimCommands
{
    // ── Navigation ────────────────────────────────────────────────────────

    internal static void Left(VimContext ctx)
    {
        ctx.ViewModel.CursorTime = Math.Max(0, ctx.ViewModel.CursorTime - ctx.GridStep);
        ctx.SyncSelection();
    }

    internal static void Right(VimContext ctx)
    {
        ctx.ViewModel.CursorTime += ctx.GridStep;
        ctx.SyncSelection();
    }

    internal static void Up(VimContext ctx)
    {
        if (ctx.ViewModel.CursorLaneIndex > 0)
        {
            ctx.ViewModel.CursorLaneIndex--;
            ctx.SyncSelection();
        }
    }

    internal static void Down(VimContext ctx)
    {
        if (ctx.ViewModel.CursorLaneIndex < ctx.LaneCount - 1)
        {
            ctx.ViewModel.CursorLaneIndex++;
            ctx.SyncSelection();
        }
    }

    internal static void GoFirst(VimContext ctx)
    {
        ctx.ViewModel.CursorLaneIndex = 0;
        ctx.SyncSelection();
    }

    internal static void GoLast(VimContext ctx)
    {
        ctx.ViewModel.CursorLaneIndex = ctx.LaneCount - 1;
        ctx.SyncSelection();
    }

    internal static void GoFirstTask(VimContext ctx)
    {
        var first = ctx.ViewModel.Items
            .Where(i => i.LaneId == ctx.CursorLaneId())
            .OrderBy(i => i.StartTime)
            .FirstOrDefault();
        if (first == null) return;
        ctx.ViewModel.CursorTime = first.StartTime;
        ctx.ViewModel.SetSelectionFromVim(first);
    }

    internal static void WordForward(VimContext ctx)
    {
        var vm = ctx.ViewModel;
        for (int li = vm.CursorLaneIndex; li < vm.Lanes.Count; li++)
        {
            double minStart = li == vm.CursorLaneIndex ? vm.CursorTime + 1e-9 : -1e-9;
            var next = vm.Items
                .Where(i => i.LaneId == vm.Lanes[li].Id && i.StartTime > minStart)
                .OrderBy(i => i.StartTime)
                .FirstOrDefault();
            if (next == null) continue;
            vm.CursorLaneIndex = li;
            vm.CursorTime = next.StartTime;
            vm.SetSelectionFromVim(next);
            return;
        }
    }

    internal static void WordBackward(VimContext ctx)
    {
        var vm  = ctx.ViewModel;
        var cur = ctx.TaskAtCursor();
        if (cur != null && vm.CursorTime > cur.StartTime + 1e-9)
        {
            vm.CursorTime = cur.StartTime;
            vm.SetSelectionFromVim(cur);
            return;
        }
        for (int li = vm.CursorLaneIndex; li >= 0; li--)
        {
            double maxStart = li == vm.CursorLaneIndex ? vm.CursorTime - 1e-9 : double.MaxValue;
            var prev = vm.Items
                .Where(i => i.LaneId == vm.Lanes[li].Id && i.StartTime < maxStart)
                .OrderByDescending(i => i.StartTime)
                .FirstOrDefault();
            if (prev == null) continue;
            vm.CursorLaneIndex = li;
            vm.CursorTime = prev.StartTime;
            vm.SetSelectionFromVim(prev);
            return;
        }
    }

    internal static void WordEnd(VimContext ctx)
    {
        var vm     = ctx.ViewModel;
        var laneId = ctx.CursorLaneId();
        var task   = ctx.TaskAtCursor()
            ?? vm.Items.Where(i => i.LaneId == laneId && i.StartTime >= vm.CursorTime - 1e-9)
                       .OrderBy(i => i.StartTime)
                       .FirstOrDefault();
        if (task == null) return;
        double last = Math.Max(task.StartTime, task.StartTime + task.Duration - ctx.GridStep);
        vm.CursorTime = last;
        vm.SetSelectionFromVim(task);
    }

    internal static void GoLineStart(VimContext ctx)
    {
        ctx.ViewModel.CursorTime = 0;
        ctx.SyncSelection();
    }

    internal static void GoLineEnd(VimContext ctx)
    {
        var vm   = ctx.ViewModel;
        var last = vm.Items
            .Where(i => i.LaneId == ctx.CursorLaneId())
            .OrderByDescending(i => i.StartTime + i.Duration)
            .FirstOrDefault();
        if (last == null) return;
        double pos = Math.Max(last.StartTime, last.StartTime + last.Duration - ctx.GridStep);
        vm.CursorTime = pos;
        vm.SetSelectionFromVim(last);
    }

    // ── Duration / Move ───────────────────────────────────────────────────

    internal static void DurationGrow(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return;
        ApplyTimelineChanges(ctx, ctx.GanttView.ResizeTaskByKeyboard(task, ctx.GridStep));
    }

    internal static void DurationShrink(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return;
        ApplyTimelineChanges(ctx, ctx.GanttView.ResizeTaskByKeyboard(task, -ctx.GridStep));
    }

    internal static void MoveTaskRight(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return;
        ApplyTimelineChanges(ctx, ctx.GanttView.MoveTaskByKeyboard(task, ctx.GridStep));
        ctx.ViewModel.CursorTime = task.StartTime;
        ctx.ViewModel.SetSelectionFromVim(task);
        ctx.GanttView.ScrollCursorIntoView();
    }

    internal static void MoveTaskLeft(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return;
        ApplyTimelineChanges(ctx, ctx.GanttView.MoveTaskByKeyboard(task, -ctx.GridStep));
        ctx.ViewModel.CursorTime = task.StartTime;
        ctx.ViewModel.SetSelectionFromVim(task);
        ctx.GanttView.ScrollCursorIntoView();
    }

    // ── Edit ─────────────────────────────────────────────────────────────

    internal static void Rename(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task != null)
        {
            ctx.GanttView.StartRenameSelectedItem();
            return;
        }
        var laneId = ctx.CursorLaneId();
        if (laneId == Guid.Empty) return;
        var newItem = ctx.ViewModel.AddNewItemAt(laneId, ctx.ViewModel.CursorTime);
        if (newItem != null) ctx.GanttView.StartRenameSelectedItem(discardOnCancel: true);
    }

    // ── Delete / Yank ─────────────────────────────────────────────────────

    internal static void DeleteTask(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return;
        DeleteTasks(ctx, [task]);
    }

    internal static void DeleteLane(VimContext ctx)
    {
        var vm   = ctx.ViewModel;
        var lane = vm.Lanes.ElementAtOrDefault(vm.CursorLaneIndex);
        if (lane == null) return;
        DeleteLanes(ctx, [lane]);
    }

    internal static void DeleteVisualSelection(VimContext ctx)
    {
        var tasks = ctx.VisualSelectionTasks().ToList();
        if (tasks.Count == 0)
        {
            var current = ctx.TaskAtCursor();
            if (current == null)
                return;

            tasks.Add(current);
        }

        DeleteTasks(ctx, tasks);
    }

    internal static void DeleteVisualLineSelection(VimContext ctx)
    {
        var lanes = ctx.VisualLineSelectionLanes().ToList();
        if (lanes.Count == 0)
        {
            var lane = ctx.ViewModel.Lanes.ElementAtOrDefault(ctx.ViewModel.CursorLaneIndex);
            if (lane == null)
                return;

            lanes.Add(lane);
        }

        DeleteLanes(ctx, lanes);
    }

    internal static void YankTask(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return;
        ctx.Clipboard.YankTask(task.ToModel());
    }

    internal static void YankLane(VimContext ctx)
    {
        var vm   = ctx.ViewModel;
        var lane = vm.Lanes.ElementAtOrDefault(vm.CursorLaneIndex);
        if (lane == null) return;
        ctx.Clipboard.YankLane(
            lane.ToModel(),
            vm.Items.Where(i => i.LaneId == lane.Id).Select(i => i.ToModel()).ToList());
    }

    internal static void YankVisualSelection(VimContext ctx)
    {
        var tasks = ctx.VisualSelectionTasks().ToList();
        if (tasks.Count == 0)
        {
            var current = ctx.TaskAtCursor();
            if (current == null)
                return;

            tasks.Add(current);
        }

        YankTaskSelection(ctx, tasks);
    }

    internal static void YankVisualLineSelection(VimContext ctx)
    {
        var lanes = ctx.VisualLineSelectionLanes().ToList();
        if (lanes.Count == 0)
        {
            var current = ctx.ViewModel.Lanes.ElementAtOrDefault(ctx.ViewModel.CursorLaneIndex);
            if (current == null)
                return;

            lanes.Add(current);
        }

        YankLaneSelection(ctx, lanes);
    }

    // ── Paste ─────────────────────────────────────────────────────────────

    internal static void PasteAfter(VimContext ctx)  => Paste(ctx, before: false);
    internal static void PasteBefore(VimContext ctx) => Paste(ctx, before: true);

    private static void Paste(VimContext ctx, bool before)
    {
        var vm = ctx.ViewModel;
        var cb = ctx.Clipboard;

        if (cb.Kind == VimClipboard.ClipKind.Task && cb.Task != null)
        {
            var laneId = ctx.CursorLaneId();
            if (laneId == Guid.Empty) return;
            double start = before ? Math.Max(0, vm.CursorTime - cb.Task.Duration) : vm.CursorTime;
            var item = vm.PasteItem(cb.Task, laneId, start);
            vm.UndoRedo.Push(new AddItemCommand(vm.Items, item));
        }
        else if (cb.Kind == VimClipboard.ClipKind.TaskBlock && cb.TaskBlock != null)
        {
            PasteTaskBlock(ctx, cb.TaskBlock, before);
        }
        else if (cb.Kind == VimClipboard.ClipKind.LaneBlock && cb.LaneBlock != null)
        {
            PasteLaneBlock(ctx, cb.LaneBlock, before);
        }
    }

    private static void DeleteTasks(VimContext ctx, IReadOnlyList<ItemViewModel> tasks)
    {
        if (tasks.Count == 0)
            return;

        YankTaskSelection(ctx, tasks);

        var vm = ctx.ViewModel;
        var commands = new List<IUndoableCommand>();
        foreach (var task in tasks)
        {
            var removeCommand = new RemoveItemCommand(vm.Items, task);
            if (vm.SelectedItem?.Id == task.Id)
                vm.SelectedItem = null;

            vm.Items.Remove(task);
            commands.Add(removeCommand);
        }

        if (commands.Count > 0)
            vm.UndoRedo.Push(new CompositeCommand(commands));

        vm.SetSelectionFromVim(ctx.TaskAtCursor());
    }

    private static void DeleteLanes(VimContext ctx, IReadOnlyList<LaneViewModel> lanesToDelete)
    {
        if (lanesToDelete.Count == 0)
            return;

        YankLaneSelection(ctx, lanesToDelete);

        var vm = ctx.ViewModel;
        var cmds = new List<IUndoableCommand>();
        int firstLaneIndex = vm.Lanes.Count == 0
            ? 0
            : lanesToDelete.Select(lane => vm.Lanes.IndexOf(lane)).Where(index => index >= 0).DefaultIfEmpty(0).Min();

        foreach (var lane in lanesToDelete)
        {
            foreach (var item in vm.Items.Where(i => i.LaneId == lane.Id).ToList())
            {
                var removeCmd = new RemoveItemCommand(vm.Items, item);
                if (vm.SelectedItem?.Id == item.Id)
                    vm.SelectedItem = null;

                vm.Items.Remove(item);
                cmds.Add(removeCmd);
            }
        }

        foreach (var lane in lanesToDelete)
        {
            if (vm.Lanes.Count <= 1)
                break;

            int laneIdx = vm.Lanes.IndexOf(lane);
            if (laneIdx < 0)
                continue;

            vm.Lanes.Remove(lane);
            cmds.Add(new RemoveLaneCommand(vm.Lanes, lane, laneIdx));
        }

        vm.Analyze();
        if (cmds.Count > 0)
            vm.UndoRedo.Push(new CompositeCommand(cmds));

        vm.CursorLaneIndex = Math.Clamp(firstLaneIndex, 0, Math.Max(0, vm.Lanes.Count - 1));
        vm.SetSelectionFromVim(ctx.TaskAtCursor());
    }

    private static void YankTaskSelection(VimContext ctx, IReadOnlyList<ItemViewModel> tasks)
    {
        if (tasks.Count == 0)
            return;

        if (tasks.Count == 1)
        {
            ctx.Clipboard.YankTask(tasks[0].ToModel());
            return;
        }

        var laneIds = tasks
            .OrderBy(task => ctx.LaneIndex(task.LaneId))
            .Select(task => task.LaneId)
            .Distinct()
            .ToList();

        double startTime = tasks.Min(task => task.StartTime);
        double endTime = tasks.Max(task => task.StartTime + task.Duration);
        ctx.Clipboard.YankTaskBlock(
            laneIds,
            startTime,
            endTime,
            tasks.Select(task => task.ToModel()).ToList());
    }

    private static void YankLaneSelection(VimContext ctx, IReadOnlyList<LaneViewModel> lanes)
    {
        if (lanes.Count == 0)
            return;

        var laneIds = lanes.Select(lane => lane.Id).ToHashSet();
        var items = ctx.ViewModel.Items
            .Where(item => laneIds.Contains(item.LaneId))
            .Select(item => item.ToModel())
            .ToList();

        if (lanes.Count == 1)
        {
            ctx.Clipboard.YankLane(lanes[0].ToModel(), items);
            return;
        }

        ctx.Clipboard.YankLaneBlock(
            lanes.Select(lane => lane.ToModel()).ToList(),
            items);
    }

    private static void PasteTaskBlock(VimContext ctx, VimTaskBlockClip clip, bool before)
    {
        if (clip.Tasks.Count == 0 || clip.LaneIds.Count == 0)
            return;

        var vm = ctx.ViewModel;
        var commands = new List<IUndoableCommand>();
        var laneOffsets = clip.LaneIds
            .Select((laneId, index) => (laneId, index))
            .ToDictionary(entry => entry.laneId, entry => entry.index);
        int baseLaneIndex = Math.Clamp(vm.CursorLaneIndex, 0, Math.Max(0, vm.Lanes.Count - 1));
        int maxLaneIndex = baseLaneIndex + clip.LaneIds.Count - 1;
        while (vm.Lanes.Count <= maxLaneIndex)
        {
            int index = vm.Lanes.Count;
            var newLane = vm.InsertLaneAt(index);
            commands.Add(new AddLaneCommand(vm.Lanes, newLane, index));
        }

        double span = clip.EndTime - clip.StartTime;
        double baseStart = before ? Math.Max(0, vm.CursorTime - span) : vm.CursorTime;
        foreach (var template in clip.Tasks
                     .OrderBy(task => laneOffsets[task.LaneId])
                     .ThenBy(task => task.StartTime))
        {
            int laneOffset = laneOffsets[template.LaneId];
            Guid laneId = vm.Lanes[baseLaneIndex + laneOffset].Id;
            double startTime = Math.Max(0, baseStart + (template.StartTime - clip.StartTime));
            var item = vm.PasteItem(template, laneId, startTime);
            commands.Add(new AddItemCommand(vm.Items, item));
        }

        if (commands.Count > 0)
            vm.UndoRedo.Push(new CompositeCommand(commands));
    }

    private static void PasteLaneBlock(VimContext ctx, VimLaneBlockClip clip, bool before)
    {
        if (clip.Lanes.Count == 0)
            return;

        var vm = ctx.ViewModel;
        var commands = new List<IUndoableCommand>();
        int afterIndex = before ? vm.CursorLaneIndex - 1 : vm.CursorLaneIndex;
        foreach (var laneTemplate in clip.Lanes)
        {
            var laneItems = clip.Items
                .Where(item => item.LaneId == laneTemplate.Id)
                .OrderBy(item => item.StartTime)
                .ToList();
            var (lane, items) = vm.PasteLane(laneTemplate, laneItems, afterIndex);
            int laneIndex = vm.Lanes.IndexOf(lane);
            commands.Add(new AddLaneCommand(vm.Lanes, lane, laneIndex));
            commands.AddRange(items.Select(item => (IUndoableCommand)new AddItemCommand(vm.Items, item)));
            afterIndex = laneIndex;
        }

        if (commands.Count > 0)
            vm.UndoRedo.Push(new CompositeCommand(commands));
    }

    private static void ApplyTimelineChanges(VimContext ctx, IReadOnlyList<TimelineEditChange> changes)
    {
        if (changes.Count == 0)
            return;

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

        if (commands.Count == 0)
            return;

        ctx.ViewModel.UndoRedo.Push(new CompositeCommand(commands));
    }
}

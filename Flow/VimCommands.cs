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
        ctx.SyncSelection();
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
            ctx.SyncSelection();
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
            ctx.SyncSelection();
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
            ctx.SyncSelection();
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
        ctx.SyncSelection();
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
        ctx.SyncSelection();
    }

    // ── Duration / Move ───────────────────────────────────────────────────

    internal static bool DurationGrow(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return false;
        return ApplyTimelineChanges(ctx, ctx.GanttView.ResizeTaskByKeyboard(task, ctx.GridStep));
    }

    internal static bool DurationShrink(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return false;
        return ApplyTimelineChanges(ctx, ctx.GanttView.ResizeTaskByKeyboard(task, -ctx.GridStep));
    }

    internal static bool MoveTaskRight(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return false;
        if (!ApplyTimelineChanges(ctx, ctx.GanttView.MoveTaskByKeyboard(task, ctx.GridStep)))
            return false;
        ctx.ViewModel.CursorTime = task.StartTime;
        ctx.SyncSelection();
        return true;
    }

    internal static bool MoveTaskLeft(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return false;
        if (!ApplyTimelineChanges(ctx, ctx.GanttView.MoveTaskByKeyboard(task, -ctx.GridStep)))
            return false;
        ctx.ViewModel.CursorTime = task.StartTime;
        ctx.SyncSelection();
        return true;
    }

    // ── Edit ─────────────────────────────────────────────────────────────

    internal static bool Rename(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task != null)
        {
            ctx.GanttView.StartRenameItem(task);
            ctx.ViewModel.SetSelectionFromVim(null);
            return true;
        }
        var laneId = ctx.CursorLaneId();
        if (laneId == Guid.Empty) return false;
        var newItem = ctx.ViewModel.AddNewItemAt(
            laneId,
            ctx.ViewModel.CursorTime,
            activateTaskEditor: false,
            selectItem: false);
        if (newItem == null) return false;
        ctx.GanttView.StartRenameItem(newItem, discardOnCancel: true);
        ctx.ViewModel.SetSelectionFromVim(null);
        return true;
    }

    // ── Delete / Yank ─────────────────────────────────────────────────────

    internal static bool DeleteTask(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        return task != null && DeleteTasks(ctx, [task]);
    }

    internal static bool DeleteTaskAtOrAfterCursor(VimContext ctx)
    {
        Guid laneId = ctx.CursorLaneId();
        if (laneId == Guid.Empty)
            return false;

        double cursorTime = ctx.ViewModel.CursorTime;
        var task = ctx.ViewModel.Items
            .Where(item => item.LaneId == laneId)
            .OrderBy(item => item.StartTime)
            .FirstOrDefault(item =>
                (item.StartTime <= cursorTime + 1e-9 && item.StartTime + item.Duration > cursorTime + 1e-9)
                || item.StartTime >= cursorTime - 1e-9);

        return task != null && DeleteTasks(ctx, [task]);
    }

    internal static bool DeleteLane(VimContext ctx)
    {
        var vm   = ctx.ViewModel;
        var lane = vm.Lanes.ElementAtOrDefault(vm.CursorLaneIndex);
        return lane != null && DeleteLanes(ctx, [lane]);
    }

    internal static bool DeleteVisualSelection(VimContext ctx)
    {
        var tasks = ResolveVisualTaskSelection(ctx);
        return DeleteTasks(ctx, tasks);
    }

    internal static bool DeleteVisualLineSelection(VimContext ctx)
    {
        var lanes = ResolveVisualLineSelection(ctx);
        return DeleteLanes(ctx, lanes);
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
        var tasks = ResolveVisualTaskSelection(ctx);
        YankTaskSelection(ctx, tasks);
    }

    internal static void YankVisualLineSelection(VimContext ctx)
    {
        var lanes = ResolveVisualLineSelection(ctx);
        YankLaneSelection(ctx, lanes);
    }

    private static List<ItemViewModel> ResolveVisualTaskSelection(VimContext ctx)
    {
        var tasks = ctx.VisualSelectionTasks().ToList();
        if (tasks.Count > 0)
            return tasks;

        var current = ctx.TaskAtCursor();
        return current != null ? [current] : [];
    }

    private static List<LaneViewModel> ResolveVisualLineSelection(VimContext ctx)
    {
        var lanes = ctx.VisualLineSelectionLanes().ToList();
        if (lanes.Count > 0)
            return lanes;

        var current = ctx.ViewModel.Lanes.ElementAtOrDefault(ctx.ViewModel.CursorLaneIndex);
        return current != null ? [current] : [];
    }

    // ── Paste ─────────────────────────────────────────────────────────────

    internal static bool PasteAfter(VimContext ctx)  => Paste(ctx, before: false);
    internal static bool PasteBefore(VimContext ctx) => Paste(ctx, before: true);

    private static bool Paste(VimContext ctx, bool before)
    {
        var vm = ctx.ViewModel;
        var cb = ctx.Clipboard;

        if (cb.Kind == VimClipboard.ClipKind.Task && cb.Task != null)
        {
            var laneId = ctx.CursorLaneId();
            if (laneId == Guid.Empty) return false;
            double start = before ? Math.Max(0, vm.CursorTime - cb.Task.Duration) : vm.CursorTime;
            var item = vm.PasteItem(cb.Task, laneId, start);
            vm.UndoRedo.Push(new AddItemCommand(vm.Items, item));
            vm.SetSelectionFromVim(null);
            return true;
        }
        if (cb.Kind == VimClipboard.ClipKind.TaskBlock && cb.TaskBlock != null)
        {
            return PasteTaskBlock(ctx, cb.TaskBlock, before);
        }
        if (cb.Kind == VimClipboard.ClipKind.LaneBlock && cb.LaneBlock != null)
        {
            return PasteLaneBlock(ctx, cb.LaneBlock, before);
        }

        return false;
    }

    internal static bool DeleteTaskRangeAtCursor(VimContext ctx, int laneSpan, double timeSpan)
    {
        if (laneSpan <= 0 || timeSpan <= 0 || ctx.ViewModel.Lanes.Count == 0)
            return false;

        int startLaneIndex = Math.Clamp(ctx.ViewModel.CursorLaneIndex, 0, ctx.ViewModel.Lanes.Count - 1);
        int endLaneIndex = Math.Min(ctx.ViewModel.Lanes.Count - 1, startLaneIndex + laneSpan - 1);
        var laneIds = Enumerable.Range(startLaneIndex, endLaneIndex - startLaneIndex + 1)
            .Select(index => ctx.ViewModel.Lanes[index].Id)
            .ToHashSet();
        double startTime = ctx.ViewModel.CursorTime;
        double endTime = startTime + timeSpan;

        var tasks = ctx.ViewModel.Items
            .Where(task => laneIds.Contains(task.LaneId)
                        && task.StartTime < endTime - 1e-9
                        && task.StartTime + task.Duration > startTime + 1e-9)
            .OrderBy(task => ctx.LaneIndex(task.LaneId))
            .ThenBy(task => task.StartTime)
            .ToList();

        return DeleteTasks(ctx, tasks);
    }

    internal static bool DeleteLaneRangeAtCursor(VimContext ctx, int laneCount)
    {
        if (laneCount <= 0 || ctx.ViewModel.Lanes.Count == 0)
            return false;

        int startLaneIndex = Math.Clamp(ctx.ViewModel.CursorLaneIndex, 0, ctx.ViewModel.Lanes.Count - 1);
        var lanes = ctx.ViewModel.Lanes
            .Skip(startLaneIndex)
            .Take(laneCount)
            .ToList();

        return DeleteLanes(ctx, lanes);
    }

    internal static bool RenameCurrentTaskTo(VimContext ctx, string newName)
    {
        var task = ctx.TaskAtCursor();
        if (task == null || string.IsNullOrWhiteSpace(newName) || task.Name == newName)
            return false;

        string oldName = task.Name;
        task.Name = newName;
        ctx.ViewModel.UndoRedo.Push(new PropertyChangeCommand<string>(value => task.Name = value, oldName, newName));
        return true;
    }

    private static bool DeleteTasks(VimContext ctx, IReadOnlyList<ItemViewModel> tasks)
    {
        if (tasks.Count == 0)
            return false;

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

        vm.SetSelectionFromVim(null);
        return commands.Count > 0;
    }

    private static bool DeleteLanes(VimContext ctx, IReadOnlyList<LaneViewModel> lanesToDelete)
    {
        if (lanesToDelete.Count == 0)
            return false;

        YankLaneSelection(ctx, lanesToDelete);

        var vm = ctx.ViewModel;
        var cmds = new List<IUndoableCommand>();
        double cursorTime = vm.CursorTime;
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

        RestoreCursorAfterDeletion(ctx, firstLaneIndex, cursorTime);
        return cmds.Count > 0;
    }

    private static void RestoreCursorAfterDeletion(VimContext ctx, int preferredLaneIndex, double preferredTime)
    {
        var vm = ctx.ViewModel;
        if (vm.Lanes.Count == 0)
            return;

        vm.CursorLaneIndex = Math.Clamp(preferredLaneIndex, 0, vm.Lanes.Count - 1);
        vm.CursorTime = Math.Max(0, preferredTime);
        vm.SetSelectionFromVim(null);
        ctx.GanttView.ScrollCursorIntoView();
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

    private static bool PasteTaskBlock(VimContext ctx, VimTaskBlockClip clip, bool before)
    {
        if (clip.Tasks.Count == 0 || clip.LaneIds.Count == 0)
            return false;

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
        vm.SetSelectionFromVim(null);
        return commands.Count > 0;
    }

    private static bool PasteLaneBlock(VimContext ctx, VimLaneBlockClip clip, bool before)
    {
        if (clip.Lanes.Count == 0)
            return false;

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
        vm.SetSelectionFromVim(null);
        return commands.Count > 0;
    }

    private static bool ApplyTimelineChanges(VimContext ctx, IReadOnlyList<TimelineEditChange> changes)
    {
        if (changes.Count == 0)
            return false;

        var commands = TimelineEditCommandFactory.Create(changes);
        if (commands.Count == 0)
            return false;

        ctx.ViewModel.UndoRedo.Push(new CompositeCommand(commands));
        ctx.ViewModel.SetSelectionFromVim(null);
        return true;
    }
}

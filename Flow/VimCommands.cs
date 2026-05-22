using System;
using System.Collections.Generic;
using System.Linq;
using Flow.ViewModels;

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
        double old = task.Duration, neu = old + ctx.GridStep;
        task.Duration = neu;
        ctx.ViewModel.UndoRedo.Push(new PropertyChangeCommand<double>(v => task.Duration = v, old, neu));
    }

    internal static void DurationShrink(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return;
        double old = task.Duration, neu = Math.Max(ctx.GridStep, old - ctx.GridStep);
        task.Duration = neu;
        ctx.ViewModel.UndoRedo.Push(new PropertyChangeCommand<double>(v => task.Duration = v, old, neu));
    }

    internal static void MoveTaskRight(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return;
        double old = task.StartTime, neu = old + ctx.GridStep;
        task.StartTime           = neu;
        ctx.ViewModel.CursorTime = neu;
        ctx.ViewModel.SetSelectionFromVim(task);
        ctx.ViewModel.UndoRedo.Push(new PropertyChangeCommand<double>(v => task.StartTime = v, old, neu));
    }

    internal static void MoveTaskLeft(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task == null) return;
        double old = task.StartTime, neu = Math.Max(0, old - ctx.GridStep);
        task.StartTime           = neu;
        ctx.ViewModel.CursorTime = neu;
        ctx.ViewModel.SetSelectionFromVim(task);
        ctx.ViewModel.UndoRedo.Push(new PropertyChangeCommand<double>(v => task.StartTime = v, old, neu));
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
        var vm  = ctx.ViewModel;
        var cmd = new RemoveItemCommand(vm.Items, task);
        if (vm.SelectedItem?.Id == task.Id) vm.SelectedItem = null;
        vm.Items.Remove(task);
        vm.UndoRedo.Push(cmd);
    }

    internal static void DeleteLane(VimContext ctx)
    {
        var vm   = ctx.ViewModel;
        var lane = vm.Lanes.ElementAtOrDefault(vm.CursorLaneIndex);
        if (lane == null) return;
        var cmds = new List<IUndoableCommand>();
        foreach (var item in vm.Items.Where(i => i.LaneId == lane.Id).ToList())
        {
            var removeCmd = new RemoveItemCommand(vm.Items, item);
            if (vm.SelectedItem?.Id == item.Id) vm.SelectedItem = null;
            vm.Items.Remove(item);
            cmds.Add(removeCmd);
        }
        if (vm.Lanes.Count > 1)
        {
            int laneIdx = vm.Lanes.IndexOf(lane);
            vm.Lanes.Remove(lane);
            cmds.Add(new RemoveLaneCommand(vm.Lanes, lane, laneIdx));
        }
        vm.Analyze();
        vm.UndoRedo.Push(new CompositeCommand(cmds));
    }

    internal static void YankTask(VimContext ctx)
    {
        var task = ctx.TaskAtCursor();
        if (task != null) ctx.Clipboard.YankTask(task.ToModel());
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
        else if (cb.Kind == VimClipboard.ClipKind.Lane && cb.Lane.HasValue)
        {
            int afterIndex = before ? vm.CursorLaneIndex - 1 : vm.CursorLaneIndex;
            var (lane, items) = vm.PasteLane(cb.Lane.Value.lane, cb.Lane.Value.items, afterIndex);
            int laneIdx = vm.Lanes.IndexOf(lane);
            var cmds = new List<IUndoableCommand> { new AddLaneCommand(vm.Lanes, lane, laneIdx) };
            cmds.AddRange(items.Select(i => (IUndoableCommand)new AddItemCommand(vm.Items, i)));
            vm.UndoRedo.Push(new CompositeCommand(cmds));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows.Input;
using Flow.Models;
using Flow.ViewModels;
using Flow.Views.Controls;

namespace Flow;

public sealed class VimController
{
    private readonly MainViewModel _viewModel;
    private readonly GanttCanvas _ganttView;
    private readonly VimEngine _engine = new();
    private readonly VimClipboard _clipboard = new();
    private LaneViewModel? _pendingNewLane;
    private Func<VimContext, bool>? _lastRepeatableChange;
    private Func<ItemViewModel, Func<VimContext, bool>>? _pendingCommittedItemRepeatFactory;

    public event Action? SearchRequested;

    public VimController(MainViewModel viewModel, GanttCanvas ganttView)
    {
        _viewModel = viewModel;
        _ganttView = ganttView;

        _engine.ModeChanged += ApplyMode;
        RegisterCommands();
        ApplyMode(_engine.Mode);
    }

    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            if (key == Key.R)
            {
                _viewModel.Redo();
                return true;
            }

            return false;
        }

        if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
            return false;

        bool shift = (modifiers & ModifierKeys.Shift) != 0;
        return _engine.HandleKey(key, shift, CreateContext());
    }

    public bool TryExitMode()
        => _engine.TryExitToNormalMode();

    public bool TryCancelPendingInput()
        => _engine.TryCancelPendingInput();

    public void HandleDiscardedNewItem(ItemViewModel item)
    {
        _viewModel.DiscardNewItem(item);
        _pendingCommittedItemRepeatFactory = null;
        CancelPendingLane();
    }

    public void HandleCommittedNewItem(ItemViewModel item)
    {
        _lastRepeatableChange = (_pendingCommittedItemRepeatFactory?.Invoke(item)
            ?? BuildRepeatCreateTask(VimAddMode.AtCursor, item.ToModel()));
        _pendingCommittedItemRepeatFactory = null;
        _viewModel.SetSelectionFromVim(null);
    }

    public void HandleItemRenamed(ItemViewModel item, string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        _lastRepeatableChange = ctx => VimCommands.RenameCurrentTaskTo(ctx, newName);
        _viewModel.SetSelectionFromVim(null);
    }

    public bool TryCommitPendingNewItem(ItemViewModel item)
    {
        if (_pendingNewLane == null)
            return false;

        int laneIdx = _viewModel.Lanes.IndexOf(_pendingNewLane);
        if (laneIdx < 0)
        {
            _pendingNewLane = null;
            return false;
        }

        _viewModel.UndoRedo.Push(new CompositeCommand([
            new AddLaneCommand(_viewModel.Lanes, _pendingNewLane, laneIdx),
            new AddItemCommand(_viewModel.Items, item),
        ]));

        _pendingNewLane = null;
        return true;
    }

    private VimContext CreateContext()
        => new(_engine, _viewModel, _ganttView, _clipboard);

    private void RegisterCommands()
    {
        // Navigation
        _engine.Register("h", VimCommands.Left);
        _engine.Register("l", VimCommands.Right);
        _engine.Register("k", VimCommands.Up);
        _engine.Register("j", VimCommands.Down);
        _engine.Register("gg", VimCommands.GoFirst);
        _engine.Register("G", VimCommands.GoLast);
        _engine.Register("^", VimCommands.GoFirstTask);
        _engine.Register("w", VimCommands.WordForward);
        _engine.Register("b", VimCommands.WordBackward);
        _engine.Register("e", VimCommands.WordEnd);
        _engine.Register("0", VimCommands.GoLineStart);
        _engine.Register("$", VimCommands.GoLineEnd);
        _engine.Register("/", _ => SearchRequested?.Invoke());
        _engine.Register("n", ctx => ctx.ViewModel.SelectNextMatchCommand.Execute(null));
        _engine.Register("N", ctx => ctx.ViewModel.SelectPreviousMatchCommand.Execute(null));
        _engine.Register(".", RepeatLastChange);

        // Duration / move
        RegisterRepeatable("+", VimCommands.DurationGrow);
        RegisterRepeatable("-", VimCommands.DurationShrink);
        RegisterRepeatable(">", VimCommands.MoveTaskRight);
        RegisterRepeatable("<", VimCommands.MoveTaskLeft);

        // View
        _engine.Register("zz", ctx => ctx.GanttView.ScrollCursorIntoCenter());

        // Edit
        _engine.Register("i", BeginRename);
        _engine.Register("a", ctx => BeginAddTask(ctx, VimAddMode.After));
        _engine.Register("I", ctx => BeginAddTask(ctx, VimAddMode.Start));
        _engine.Register("o", ctx => BeginAddTask(ctx, VimAddMode.LaneBelow));
        _engine.Register("O", ctx => BeginAddTask(ctx, VimAddMode.LaneAbove));

        // Undo
        _engine.Register("u", ctx => ctx.ViewModel.Undo());

        // Visual mode
        _engine.Register("v", EnterVisualMode);
        _engine.Register("V", EnterVisualLineMode);
        RegisterRepeatable(
            VimMode.Visual,
            "d",
            ctx => ExecuteAndReturnToNormal(ctx, VimCommands.DeleteVisualSelection),
            BuildVisualDeleteRepeat);
        _engine.Register(VimMode.Visual, "y", ctx => ExecuteAndReturnToNormal(ctx, VimCommands.YankVisualSelection));
        RegisterRepeatable(
            VimMode.VisualLine,
            "d",
            ctx => ExecuteAndReturnToNormal(ctx, VimCommands.DeleteVisualLineSelection),
            BuildVisualLineDeleteRepeat);
        _engine.Register(VimMode.VisualLine, "y", ctx => ExecuteAndReturnToNormal(ctx, VimCommands.YankVisualLineSelection));
        _engine.Register(VimMode.VisualLine, "k", MoveVisualLineUp);
        _engine.Register(VimMode.VisualLine, "j", MoveVisualLineDown);
        _engine.Register(VimMode.VisualLine, "h", _ => { });
        _engine.Register(VimMode.VisualLine, "l", _ => { });

        // Delete / yank / paste
        RegisterRepeatable(VimMode.Normal, "x", ExecuteDeleteTaskCommand, _ => repeatCtx => VimCommands.DeleteTaskAtOrAfterCursor(repeatCtx));
        RegisterRepeatable(VimMode.Normal, "diw", ExecuteDeleteTaskCommand, _ => repeatCtx => VimCommands.DeleteTaskAtOrAfterCursor(repeatCtx));
        RegisterRepeatable(VimMode.Normal, "daw", VimCommands.DeleteTaskAtOrAfterCursor, _ => repeatCtx => VimCommands.DeleteTaskAtOrAfterCursor(repeatCtx));
        _engine.Register("yiw", VimCommands.YankTask);
        _engine.Register("yaw", VimCommands.YankTaskAtOrAfterCursor);
        _engine.Register("yy", VimCommands.YankLane);
        RegisterRepeatable("dd", VimCommands.DeleteLane);
        RegisterRepeatable("p", VimCommands.PasteAfter);
        RegisterRepeatable("P", VimCommands.PasteBefore);
    }

    private void ApplyMode(VimMode mode)
    {
        _viewModel.IsVisualMode = mode != VimMode.Normal;
        _viewModel.IsVisualLineMode = mode == VimMode.VisualLine;
        _viewModel.VisualModeLabel = mode switch
        {
            VimMode.Visual => "-- VISUAL --",
            VimMode.VisualLine => "-- VISUAL LINE --",
            _ => "",
        };

        if (mode == VimMode.Normal)
        {
            _viewModel.VisualAnchorLane = -1;
            _viewModel.VisualAnchorTime = double.NaN;
        }
    }

    private void EnterVisualMode(VimContext ctx)
    {
        _viewModel.VisualAnchorLane = _viewModel.CursorLaneIndex;
        _viewModel.VisualAnchorTime = _viewModel.CursorTime;
        _engine.SetMode(VimMode.Visual);
        ctx.SyncSelection();
    }

    private void EnterVisualLineMode(VimContext ctx)
    {
        _viewModel.VisualAnchorLane = _viewModel.CursorLaneIndex;
        _viewModel.VisualAnchorTime = double.NaN;
        _engine.SetMode(VimMode.VisualLine);
    }

    private bool ExecuteAndReturnToNormal(VimContext ctx, Func<VimContext, bool> action)
    {
        bool changed = action(ctx);
        _engine.SetMode(VimMode.Normal);
        return changed;
    }

    private void ExecuteAndReturnToNormal(VimContext ctx, Action<VimContext> action)
    {
        action(ctx);
        _engine.SetMode(VimMode.Normal);
    }

    private void MoveVisualLineUp(VimContext ctx)
    {
        if (_viewModel.CursorLaneIndex <= 0)
            return;

        _viewModel.CursorLaneIndex--;
        ctx.GanttView.ScrollCursorIntoView();
    }

    private void MoveVisualLineDown(VimContext ctx)
    {
        if (_viewModel.CursorLaneIndex >= ctx.LaneCount - 1)
            return;

        _viewModel.CursorLaneIndex++;
        ctx.GanttView.ScrollCursorIntoView();
    }

    private static bool ExecuteDeleteTaskCommand(VimContext ctx)
        => ctx.Engine.CurrentCommandIteration == 1
            ? VimCommands.DeleteTask(ctx)
            : VimCommands.DeleteTaskAtOrAfterCursor(ctx);

    private void BeginRename(VimContext ctx)
    {
        _pendingCommittedItemRepeatFactory = null;
        bool isCreate = ctx.TaskAtCursor() == null;
        if (isCreate)
            _pendingCommittedItemRepeatFactory = item => BuildRepeatCreateTask(VimAddMode.AtCursor, item.ToModel());

        if (!VimCommands.Rename(ctx))
            _pendingCommittedItemRepeatFactory = null;
    }

    private void BeginAddTask(VimContext ctx, VimAddMode mode)
    {
        _pendingCommittedItemRepeatFactory = null;
        if (!AddTask(ctx, mode))
            return;

        _pendingCommittedItemRepeatFactory = item => BuildRepeatCreateTask(mode, item.ToModel());
    }

    private bool AddTask(VimContext ctx, VimAddMode mode)
    {
        if (!TryResolveAddTarget(ctx, mode, out Guid laneId, out double startTime, out LaneViewModel? newLane))
            return false;

        var newItem = _viewModel.AddNewItemAt(
            laneId,
            startTime,
            activateTaskEditor: false,
            selectItem: false);
        if (newItem == null)
        {
            if (newLane != null)
                _viewModel.Lanes.Remove(newLane);

            return false;
        }

        if (newLane != null)
            _pendingNewLane = newLane;

        ctx.GanttView.StartRenameItem(newItem, discardOnCancel: true);
        _viewModel.SetSelectionFromVim(null);
        return true;
    }

    private bool TryResolveAddTarget(
        VimContext ctx,
        VimAddMode mode,
        out Guid laneId,
        out double startTime,
        out LaneViewModel? newLane)
    {
        laneId = Guid.Empty;
        startTime = 0;
        newLane = null;

        switch (mode)
        {
            case VimAddMode.After:
            {
                laneId = ctx.CursorLaneId();
                var current = ctx.TaskAtCursor();
                startTime = current != null
                    ? current.StartTime + current.Duration
                    : _viewModel.CursorTime + ctx.GridStep;
                return laneId != Guid.Empty;
            }
            case VimAddMode.Start:
                laneId = ctx.CursorLaneId();
                startTime = 0;
                return laneId != Guid.Empty;
            case VimAddMode.AtCursor:
                laneId = ctx.CursorLaneId();
                startTime = _viewModel.CursorTime;
                return laneId != Guid.Empty;
            case VimAddMode.LaneBelow:
            {
                int index = _viewModel.CursorLaneIndex;
                if (index + 1 < _viewModel.Lanes.Count)
                {
                    laneId = _viewModel.Lanes[index + 1].Id;
                }
                else
                {
                    newLane = _viewModel.InsertLaneAfter(index);
                    laneId = newLane.Id;
                }

                startTime = _viewModel.CursorTime;
                return laneId != Guid.Empty;
            }
            case VimAddMode.LaneAbove:
                if (_viewModel.CursorLaneIndex > 0)
                {
                    laneId = _viewModel.Lanes[_viewModel.CursorLaneIndex - 1].Id;
                }
                else
                {
                    newLane = _viewModel.InsertLaneAt(0);
                    laneId = newLane.Id;
                }

                startTime = _viewModel.CursorTime;
                return laneId != Guid.Empty;
            default:
                return false;
        }
    }

    private Func<VimContext, bool> BuildRepeatCreateTask(VimAddMode mode, SequenceItem template)
    {
        var snapshot = CloneSequenceItem(template);
        return ctx => RepeatCreateTask(ctx, mode, snapshot);
    }

    private bool RepeatCreateTask(VimContext ctx, VimAddMode mode, SequenceItem template)
    {
        if (!TryResolveAddTarget(ctx, mode, out Guid laneId, out double startTime, out LaneViewModel? newLane))
            return false;

        var item = _viewModel.PasteItem(CloneSequenceItem(template), laneId, startTime);
        var commands = new List<IUndoableCommand>();
        if (newLane != null)
        {
            int laneIndex = _viewModel.Lanes.IndexOf(newLane);
            commands.Add(new AddLaneCommand(_viewModel.Lanes, newLane, laneIndex));
        }

        commands.Add(new AddItemCommand(_viewModel.Items, item));
        _viewModel.UndoRedo.Push(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
        _viewModel.SetSelectionFromVim(null);
        return true;
    }

    private Func<VimContext, bool> BuildVisualDeleteRepeat(VimContext ctx)
    {
        int laneSpan = Math.Abs(ctx.ViewModel.CursorLaneIndex - ctx.ViewModel.VisualAnchorLane) + 1;
        double timeSpan = Math.Abs(ctx.ViewModel.CursorTime - ctx.ViewModel.VisualAnchorTime) + ctx.GridStep;
        return repeatCtx => VimCommands.DeleteTaskRangeAtCursor(repeatCtx, laneSpan, timeSpan);
    }

    private Func<VimContext, bool> BuildVisualLineDeleteRepeat(VimContext ctx)
    {
        int laneCount = ctx.VisualLineSelectionLanes().Count;
        return repeatCtx => VimCommands.DeleteLaneRangeAtCursor(repeatCtx, laneCount);
    }

    private void RepeatLastChange(VimContext ctx)
    {
        _lastRepeatableChange?.Invoke(ctx);
    }

    private void RegisterRepeatable(string sequence, Func<VimContext, bool> command)
        => RegisterRepeatable(VimMode.Normal, sequence, command, _ => command);

    private void RegisterRepeatable(
        VimMode mode,
        string sequence,
        Func<VimContext, bool> command,
        Func<VimContext, Func<VimContext, bool>> repeatFactory)
    {
        _engine.Register(mode, sequence, ctx =>
        {
            Func<VimContext, bool>? repeatChange = ctx.Engine.CurrentCommandIteration == 1
                ? BuildCountAwareRepeat(repeatFactory(ctx), ctx.Engine.CurrentCommandCount)
                : null;

            if (!command(ctx))
                return;

            if (repeatChange != null)
                _lastRepeatableChange = repeatChange;
        });
    }

    private static Func<VimContext, bool> BuildCountAwareRepeat(Func<VimContext, bool> command, int count)
        => ctx =>
        {
            bool changed = false;
            for (int index = 0; index < Math.Max(count, 1); index++)
                changed = command(ctx) || changed;

            return changed;
        };

    private static SequenceItem CloneSequenceItem(SequenceItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Description = item.Description,
        StartTime = item.StartTime,
        Duration = item.Duration,
        LaneId = item.LaneId,
        CategoryId = item.CategoryId,
        PreConditions = [.. item.PreConditions],
        PostConditions = [.. item.PostConditions],
    };

    private void CancelPendingLane()
    {
        if (_pendingNewLane == null)
            return;

        _viewModel.Lanes.Remove(_pendingNewLane);
        _pendingNewLane = null;
    }

    private enum VimAddMode
    {
        After,
        Start,
        AtCursor,
        LaneBelow,
        LaneAbove,
    }
}

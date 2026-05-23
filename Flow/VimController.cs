using System;
using System.Windows.Input;
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
        CancelPendingLane();
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

        // Duration / move
        _engine.Register("+", VimCommands.DurationGrow);
        _engine.Register("-", VimCommands.DurationShrink);
        _engine.Register(">", VimCommands.MoveTaskRight);
        _engine.Register("<", VimCommands.MoveTaskLeft);

        // View
        _engine.Register("zz", ctx => ctx.GanttView.ScrollCursorIntoCenter());

        // Edit
        _engine.Register("i", VimCommands.Rename);
        _engine.Register("a", ctx => AddTask(ctx, VimAddMode.After));
        _engine.Register("I", ctx => AddTask(ctx, VimAddMode.Start));
        _engine.Register("o", ctx => AddTask(ctx, VimAddMode.LaneBelow));
        _engine.Register("O", ctx => AddTask(ctx, VimAddMode.LaneAbove));

        // Undo
        _engine.Register("u", ctx => ctx.ViewModel.Undo());

        // Visual mode
        _engine.Register("v", EnterVisualMode);
        _engine.Register("V", EnterVisualLineMode);
        _engine.Register(VimMode.Visual, "d", ctx => ExecuteAndReturnToNormal(ctx, VimCommands.DeleteVisualSelection));
        _engine.Register(VimMode.Visual, "y", ctx => ExecuteAndReturnToNormal(ctx, VimCommands.YankVisualSelection));
        _engine.Register(VimMode.VisualLine, "d", ctx => ExecuteAndReturnToNormal(ctx, VimCommands.DeleteVisualLineSelection));
        _engine.Register(VimMode.VisualLine, "y", ctx => ExecuteAndReturnToNormal(ctx, VimCommands.YankVisualLineSelection));
        _engine.Register(VimMode.VisualLine, "k", MoveVisualLineUp);
        _engine.Register(VimMode.VisualLine, "j", MoveVisualLineDown);
        _engine.Register(VimMode.VisualLine, "h", _ => { });
        _engine.Register(VimMode.VisualLine, "l", _ => { });

        // Delete / yank / paste
        _engine.Register("x", VimCommands.DeleteTask);
        _engine.Register("diw", VimCommands.DeleteTask);
        _engine.Register("yiw", VimCommands.YankTask);
        _engine.Register("yy", VimCommands.YankLane);
        _engine.Register("dd", VimCommands.DeleteLane);
        _engine.Register("p", VimCommands.PasteAfter);
        _engine.Register("P", VimCommands.PasteBefore);
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

    private void AddTask(VimContext ctx, VimAddMode mode)
    {
        Guid laneId;
        double startTime;

        switch (mode)
        {
            case VimAddMode.After:
            {
                laneId = ctx.CursorLaneId();
                var current = ctx.TaskAtCursor();
                startTime = current != null
                    ? current.StartTime + current.Duration
                    : _viewModel.CursorTime + ctx.GridStep;
                break;
            }
            case VimAddMode.Start:
                laneId = ctx.CursorLaneId();
                startTime = 0;
                break;
            case VimAddMode.LaneBelow:
            {
                int index = _viewModel.CursorLaneIndex;
                if (index + 1 < _viewModel.Lanes.Count)
                {
                    laneId = _viewModel.Lanes[index + 1].Id;
                }
                else
                {
                    var newLane = _viewModel.InsertLaneAfter(index);
                    laneId = newLane.Id;
                    _pendingNewLane = newLane;
                }

                startTime = _viewModel.CursorTime;
                break;
            }
            case VimAddMode.LaneAbove:
                if (_viewModel.CursorLaneIndex > 0)
                {
                    laneId = _viewModel.Lanes[_viewModel.CursorLaneIndex - 1].Id;
                }
                else
                {
                    var newLane = _viewModel.InsertLaneAt(0);
                    laneId = newLane.Id;
                    _pendingNewLane = newLane;
                }

                startTime = _viewModel.CursorTime;
                break;
            default:
                return;
        }

        if (laneId == Guid.Empty)
            return;

        var newItem = _viewModel.AddNewItemAt(laneId, startTime);
        if (newItem != null)
            ctx.GanttView.StartRenameSelectedItem(discardOnCancel: true);
    }

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
        LaneBelow,
        LaneAbove,
    }
}

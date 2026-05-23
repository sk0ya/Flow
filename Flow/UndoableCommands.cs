using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Flow.ViewModels;
using Flow.Views.Controls;

namespace Flow;

public interface IUndoableCommand
{
    void Undo();
    void Redo();
}

public sealed class PropertyChangeCommand<T>(Action<T> setter, T before, T after) : IUndoableCommand
{
    public void Undo() => setter(before);
    public void Redo() => setter(after);
}

public static class TimelineEditCommandFactory
{
    public static List<IUndoableCommand> Create(IReadOnlyList<TimelineEditChange> changes)
    {
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

        return commands;
    }
}

public sealed class AddItemCommand(ObservableCollection<ItemViewModel> items, ItemViewModel item) : IUndoableCommand
{
    public void Undo() => items.Remove(item);
    public void Redo() => items.Add(item);
}

public sealed class RemoveItemCommand : IUndoableCommand
{
    private readonly ObservableCollection<ItemViewModel> _items;
    private readonly ItemViewModel _item;
    private int _index;

    public RemoveItemCommand(ObservableCollection<ItemViewModel> items, ItemViewModel item)
    {
        _items = items;
        _item  = item;
        _index = items.IndexOf(item);
    }

    public void Undo() => _items.Insert(Math.Min(_index, _items.Count), _item);

    public void Redo()
    {
        _index = _items.IndexOf(_item);
        _items.Remove(_item);
    }
}

public sealed class AddLaneCommand : IUndoableCommand
{
    private readonly ObservableCollection<LaneViewModel> _lanes;
    private readonly LaneViewModel _lane;
    private readonly int _index;

    public AddLaneCommand(ObservableCollection<LaneViewModel> lanes, LaneViewModel lane, int index)
    {
        _lanes = lanes;
        _lane  = lane;
        _index = index;
    }

    public void Undo() => _lanes.Remove(_lane);
    public void Redo() => _lanes.Insert(Math.Min(_index, _lanes.Count), _lane);
}

public sealed class RemoveLaneCommand : IUndoableCommand
{
    private readonly ObservableCollection<LaneViewModel> _lanes;
    private readonly LaneViewModel _lane;
    private int _index;

    public RemoveLaneCommand(ObservableCollection<LaneViewModel> lanes, LaneViewModel lane, int index)
    {
        _lanes = lanes;
        _lane  = lane;
        _index = index;
    }

    public void Undo() => _lanes.Insert(Math.Min(_index, _lanes.Count), _lane);

    public void Redo()
    {
        _index = _lanes.IndexOf(_lane);
        _lanes.Remove(_lane);
    }
}

public sealed class ReorderLaneCommand : IUndoableCommand
{
    private readonly ObservableCollection<LaneViewModel> _lanes;
    private readonly int _fromIndex;
    private readonly int _toIndex;

    public ReorderLaneCommand(ObservableCollection<LaneViewModel> lanes, int fromIndex, int toIndex)
    {
        _lanes = lanes;
        _fromIndex = fromIndex;
        _toIndex = toIndex;
    }

    public void Undo()
    {
        if (_toIndex < 0 || _toIndex >= _lanes.Count) return;
        _lanes.Move(_toIndex, _fromIndex);
    }

    public void Redo()
    {
        if (_fromIndex < 0 || _fromIndex >= _lanes.Count) return;
        _lanes.Move(_fromIndex, _toIndex);
    }
}

public sealed class CompositeCommand(IReadOnlyList<IUndoableCommand> commands) : IUndoableCommand
{
    public void Undo() { foreach (var cmd in commands.Reverse()) cmd.Undo(); }
    public void Redo() { foreach (var cmd in commands)           cmd.Redo(); }
}

public sealed class UndoRedoManager
{
    private const int MaxDepth = 50;
    private readonly Stack<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    public void Push(IUndoableCommand cmd)
    {
        _undo.Push(cmd);
        _redo.Clear();
        if (_undo.Count > MaxDepth)
        {
            var all = _undo.ToArray();
            _undo.Clear();
            foreach (var c in all.Take(MaxDepth).Reverse())
                _undo.Push(c);
        }
    }

    public void Undo() { if (_undo.TryPop(out var cmd)) { cmd.Undo(); _redo.Push(cmd); } }
    public void Redo() { if (_redo.TryPop(out var cmd)) { cmd.Redo(); _undo.Push(cmd); } }
}

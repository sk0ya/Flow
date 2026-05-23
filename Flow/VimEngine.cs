using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Flow.Models;
using Flow.ViewModels;
using Flow.Views.Controls;

namespace Flow;

public enum VimMode
{
    Normal,
    Visual,
    VisualLine,
}

public sealed record VimTaskBlockClip(
    List<Guid> LaneIds,
    double StartTime,
    double EndTime,
    List<SequenceItem> Tasks);

public sealed record VimLaneBlockClip(
    List<Lane> Lanes,
    List<SequenceItem> Items);

public sealed class VimClipboard
{
    public enum ClipKind { None, Task, TaskBlock, LaneBlock }

    public ClipKind          Kind      { get; private set; } = ClipKind.None;
    public SequenceItem?     Task      { get; private set; }
    public VimTaskBlockClip? TaskBlock { get; private set; }
    public VimLaneBlockClip? LaneBlock { get; private set; }

    public void YankTask(SequenceItem task)
    {
        Task = task;
        TaskBlock = null;
        LaneBlock = null;
        Kind = ClipKind.Task;
    }

    public void YankLane(Lane lane, List<SequenceItem> items)
    {
        YankLaneBlock([lane], items);
    }

    public void YankTaskBlock(List<Guid> laneIds, double startTime, double endTime, List<SequenceItem> tasks)
    {
        Task = null;
        TaskBlock = new VimTaskBlockClip(laneIds, startTime, endTime, tasks);
        LaneBlock = null;
        Kind = ClipKind.TaskBlock;
    }

    public void YankLaneBlock(List<Lane> lanes, List<SequenceItem> items)
    {
        Task = null;
        TaskBlock = null;
        LaneBlock = new VimLaneBlockClip(lanes, items);
        Kind = ClipKind.LaneBlock;
    }
}

public sealed class VimContext(VimEngine engine, MainViewModel viewModel, GanttCanvas ganttView, VimClipboard clipboard)
{
    public VimEngine     Engine    { get; } = engine;
    public MainViewModel ViewModel { get; } = viewModel;
    public GanttCanvas   GanttView { get; } = ganttView;
    public VimClipboard  Clipboard { get; } = clipboard;

    public double GridStep  => ViewModel.GridStepInSeconds;
    public int    LaneCount => ViewModel.Lanes.Count;

    public Guid CursorLaneId()
        => ViewModel.Lanes.ElementAtOrDefault(ViewModel.CursorLaneIndex)?.Id ?? Guid.Empty;

    public ItemViewModel? TaskAtCursor()
    {
        var laneId = CursorLaneId();
        if (laneId == Guid.Empty) return null;
        double t = ViewModel.CursorTime;
        return ViewModel.Items.FirstOrDefault(i =>
            i.LaneId == laneId &&
            i.StartTime     <= t + 1e-9 &&
            i.StartTime + i.Duration > t + 1e-9);
    }

    public void SyncSelection()
    {
        ViewModel.SetSelectionFromVim(null);
        GanttView.ScrollCursorIntoView();
    }

    public int LaneIndex(Guid laneId)
    {
        for (int index = 0; index < ViewModel.Lanes.Count; index++)
        {
            if (ViewModel.Lanes[index].Id == laneId)
                return index;
        }

        return -1;
    }

    public IReadOnlyList<ItemViewModel> VisualSelectionTasks()
    {
        if (ViewModel.VisualAnchorLane < 0 || double.IsNaN(ViewModel.VisualAnchorTime))
            return [];

        int maxLaneIndex = Math.Max(0, ViewModel.Lanes.Count - 1);
        int anchorLane = Math.Clamp(ViewModel.VisualAnchorLane, 0, maxLaneIndex);
        int cursorLane = Math.Clamp(ViewModel.CursorLaneIndex, 0, maxLaneIndex);
        int minLane = Math.Min(anchorLane, cursorLane);
        int maxLane = Math.Max(anchorLane, cursorLane);
        double minTime = Math.Min(ViewModel.VisualAnchorTime, ViewModel.CursorTime);
        double maxTime = Math.Max(ViewModel.VisualAnchorTime, ViewModel.CursorTime) + GridStep;

        return ViewModel.Items
            .Where(item =>
            {
                int laneIndex = LaneIndex(item.LaneId);
                if (laneIndex < minLane || laneIndex > maxLane)
                    return false;

                double itemEnd = item.StartTime + item.Duration;
                return item.StartTime < maxTime - 1e-9 && itemEnd > minTime + 1e-9;
            })
            .OrderBy(item => LaneIndex(item.LaneId))
            .ThenBy(item => item.StartTime)
            .ToList();
    }

    public IReadOnlyList<LaneViewModel> VisualLineSelectionLanes()
    {
        if (ViewModel.VisualAnchorLane < 0 || ViewModel.Lanes.Count == 0)
            return [];

        int anchorLane = Math.Clamp(ViewModel.VisualAnchorLane, 0, ViewModel.Lanes.Count - 1);
        int cursorLane = Math.Clamp(ViewModel.CursorLaneIndex, 0, ViewModel.Lanes.Count - 1);
        int start = Math.Min(anchorLane, cursorLane);
        int end = Math.Max(anchorLane, cursorLane);

        return Enumerable.Range(start, end - start + 1)
            .Select(index => ViewModel.Lanes[index])
            .ToList();
    }
}

internal sealed class VimCommandRegistry
{
    private readonly Dictionary<string, Action<VimContext>> _commands = new(StringComparer.Ordinal);
    private readonly HashSet<string> _prefixes = new(StringComparer.Ordinal);

    public void Register(string sequence, Action<VimContext> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequence);
        ArgumentNullException.ThrowIfNull(handler);

        _commands[sequence] = handler;
        for (int i = 1; i < sequence.Length; i++)
            _prefixes.Add(sequence[..i]);
    }

    public bool TryGetCommand(string sequence, [NotNullWhen(true)] out Action<VimContext>? handler)
        => _commands.TryGetValue(sequence, out handler);

    public bool HasPrefix(string sequence)
        => _prefixes.Contains(sequence);
}

internal static class VimKeyNotation
{
    public static string? FromKey(Key key, bool shift) => key switch
    {
        Key.H when !shift => "h",
        Key.L when !shift => "l",
        Key.K when !shift => "k",
        Key.J when !shift => "j",
        Key.I when !shift => "i",
        Key.I when  shift => "I",
        Key.A when !shift => "a",
        Key.O when !shift => "o",
        Key.O when  shift => "O",
        Key.P when !shift => "p",
        Key.P when  shift => "P",
        Key.Y when !shift => "y",
        Key.D when !shift => "d",
        Key.W when !shift => "w",
        Key.B when !shift => "b",
        Key.E when !shift => "e",
        Key.Oem2 when !shift => "/",
        Key.Divide => "/",
        Key.G when !shift => "g",
        Key.G when  shift => "G",
        Key.N when !shift => "n",
        Key.N when  shift => "N",
        Key.U when !shift => "u",
        Key.V when !shift => "v",
        Key.V when  shift => "V",
        Key.X when !shift => "x",
        Key.Z when !shift => "z",
        Key.D0 when !shift => "0",
        Key.NumPad0 => "0",
        Key.D1 when !shift => "1",
        Key.NumPad1 => "1",
        Key.D2 when !shift => "2",
        Key.NumPad2 => "2",
        Key.D3 when !shift => "3",
        Key.NumPad3 => "3",
        Key.D4 when !shift => "4",
        Key.NumPad4 => "4",
        Key.D5 when !shift => "5",
        Key.NumPad5 => "5",
        Key.D6 when !shift => "6",
        Key.NumPad6 => "6",
        Key.D7 when !shift => "7",
        Key.NumPad7 => "7",
        Key.D8 when !shift => "8",
        Key.NumPad8 => "8",
        Key.D9 when !shift => "9",
        Key.NumPad9 => "9",
        Key.D4 when  shift => "$",
        Key.D6 when  shift => "^",
        Key.OemPlus   when  shift => "+",
        Key.Add                   => "+",
        Key.OemMinus  when !shift => "-",
        Key.Subtract              => "-",
        Key.OemPeriod when !shift => ".",
        Key.Decimal              => ".",
        Key.OemPeriod when  shift => ">",
        Key.OemComma  when  shift => "<",
        _ => null,
    };
}

public sealed class VimEngine
{
    private readonly Dictionary<VimMode, VimCommandRegistry> _registries = new()
    {
        [VimMode.Normal] = new(),
        [VimMode.Visual] = new(),
        [VimMode.VisualLine] = new(),
    };

    private readonly DispatcherTimer _clearTimer;
    private string _buffer = "";
    private string _countBuffer = "";

    public VimEngine(TimeSpan? bufferTimeout = null)
    {
        _clearTimer = new DispatcherTimer
        {
            Interval = bufferTimeout ?? TimeSpan.FromMilliseconds(1000),
        };
        _clearTimer.Tick += (_, _) => ClearPendingInput();
    }

    public VimMode Mode { get; private set; } = VimMode.Normal;
    public int CurrentCommandCount { get; private set; } = 1;
    public int CurrentCommandIteration { get; private set; }

    public event Action<VimMode>? ModeChanged;

    public void Register(string sequence, Action<VimContext> handler)
        => Register(VimMode.Normal, sequence, handler);

    public void Register(VimMode mode, string sequence, Action<VimContext> handler)
        => _registries[mode].Register(sequence, handler);

    public bool HandleKey(Key key, bool shift, VimContext context)
    {
        string? token = VimKeyNotation.FromKey(key, shift);
        return token != null && TryDispatch(token, context);
    }

    public bool TryExitToNormalMode()
    {
        if (Mode == VimMode.Normal)
        {
            ClearPendingInput();
            return false;
        }

        SetMode(VimMode.Normal);
        return true;
    }

    public bool TryCancelPendingInput()
    {
        if (string.IsNullOrEmpty(_buffer) && string.IsNullOrEmpty(_countBuffer))
            return false;

        ClearPendingInput();
        return true;
    }

    public void SetMode(VimMode mode)
    {
        if (Mode == mode)
        {
            ClearPendingInput();
            return;
        }

        Mode = mode;
        ClearPendingInput();
        ModeChanged?.Invoke(mode);
    }

    public void ClearPendingInput()
    {
        _buffer = "";
        _countBuffer = "";
        _clearTimer.Stop();
    }

    private bool TryDispatch(string token, VimContext context)
    {
        if (TryAccumulateCount(token))
            return true;

        string candidate = _buffer + token;
        int count = ParsePendingCount();

        foreach (var registry in EnumerateActiveRegistries())
        {
            if (!registry.TryGetCommand(candidate, out var command)) continue;
            ClearPendingInput();
            CurrentCommandCount = count;
            try
            {
                for (int index = 0; index < count; index++)
                {
                    CurrentCommandIteration = index + 1;
                    command(context);
                }
            }
            finally
            {
                CurrentCommandCount = 1;
                CurrentCommandIteration = 0;
            }
            return true;
        }

        foreach (var registry in EnumerateActiveRegistries())
        {
            if (!registry.HasPrefix(candidate)) continue;
            _buffer = candidate;
            _clearTimer.Stop();
            _clearTimer.Start();
            return true;
        }

        ClearPendingInput();

        foreach (var registry in EnumerateActiveRegistries())
        {
            if (!registry.TryGetCommand(token, out var fallback)) continue;
            fallback(context);
            return true;
        }

        return false;
    }

    private bool TryAccumulateCount(string token)
    {
        if (_buffer.Length != 0 || token.Length != 1 || token[0] < '0' || token[0] > '9')
            return false;

        if (token == "0" && _countBuffer.Length == 0)
            return false;

        _countBuffer += token;
        _clearTimer.Stop();
        _clearTimer.Start();
        return true;
    }

    private int ParsePendingCount()
        => int.TryParse(_countBuffer, out int count) && count > 0 ? count : 1;

    private IEnumerable<VimCommandRegistry> EnumerateActiveRegistries()
    {
        if (Mode != VimMode.Normal)
            yield return _registries[Mode];

        yield return _registries[VimMode.Normal];
    }
}

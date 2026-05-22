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

public sealed class VimClipboard
{
    public enum ClipKind { None, Task, Lane }

    public ClipKind                               Kind { get; private set; } = ClipKind.None;
    public SequenceItem?                          Task { get; private set; }
    public (Lane lane, List<SequenceItem> items)? Lane { get; private set; }

    public void YankTask(SequenceItem task)
    {
        Task = task;
        Lane = null;
        Kind = ClipKind.Task;
    }

    public void YankLane(Lane lane, List<SequenceItem> items)
    {
        Lane = (lane, items);
        Task = null;
        Kind = ClipKind.Lane;
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
        ViewModel.SetSelectionFromVim(TaskAtCursor());
        GanttView.ScrollCursorIntoView();
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
        Key.G when !shift => "g",
        Key.G when  shift => "G",
        Key.U when !shift => "u",
        Key.V when !shift => "v",
        Key.V when  shift => "V",
        Key.X when !shift => "x",
        Key.Z when !shift => "z",
        Key.D0 when !shift => "0",
        Key.D4 when  shift => "$",
        Key.D6 when  shift => "^",
        Key.OemPlus   when  shift => "+",
        Key.Add                   => "+",
        Key.OemMinus  when !shift => "-",
        Key.Subtract              => "-",
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

    public VimEngine(TimeSpan? bufferTimeout = null)
    {
        _clearTimer = new DispatcherTimer
        {
            Interval = bufferTimeout ?? TimeSpan.FromMilliseconds(1000),
        };
        _clearTimer.Tick += (_, _) => ClearPendingInput();
    }

    public VimMode Mode { get; private set; } = VimMode.Normal;

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
        _clearTimer.Stop();
    }

    private bool TryDispatch(string token, VimContext context)
    {
        string candidate = _buffer + token;

        foreach (var registry in EnumerateActiveRegistries())
        {
            if (!registry.TryGetCommand(candidate, out var command)) continue;
            ClearPendingInput();
            command(context);
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

    private IEnumerable<VimCommandRegistry> EnumerateActiveRegistries()
    {
        if (Mode != VimMode.Normal)
            yield return _registries[Mode];

        yield return _registries[VimMode.Normal];
    }
}

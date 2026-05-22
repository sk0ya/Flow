using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Flow.Models;
using Flow.ViewModels;
using Flow.Views.Controls;

namespace Flow;

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

public sealed class VimContext(MainViewModel viewModel, GanttCanvas ganttView, VimClipboard clipboard)
{
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

/// Key-sequence Vim command engine.
/// Register commands via Register(), then route WPF key events through HandleKey().
public sealed class VimEngine
{
    private readonly Dictionary<string, Action<VimContext>> _commands =
        new(StringComparer.Ordinal);

    private string           _buffer     = "";
    private DispatcherTimer? _clearTimer;

    public void Init()
    {
        _clearTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _clearTimer.Tick += (_, _) => ClearBuffer();
    }

    public void Register(string sequence, Action<VimContext> handler)
        => _commands[sequence] = handler;

    public bool HandleKey(Key key, bool shift, VimContext ctx)
    {
        string? ch = KeyToChar(key, shift);
        if (ch == null) return false;

        string candidate = _buffer + ch;

        // Exact match → execute
        if (_commands.TryGetValue(candidate, out var cmd))
        {
            ClearBuffer();
            cmd(ctx);
            return true;
        }

        // Prefix of a registered sequence → buffer and wait
        if (_commands.Keys.Any(k =>
                k.StartsWith(candidate, StringComparison.Ordinal) && k.Length > candidate.Length))
        {
            _buffer = candidate;
            _clearTimer?.Stop();
            _clearTimer?.Start();
            return true;
        }

        // No continuation — clear buffer and retry as a single-char command
        ClearBuffer();
        if (_commands.TryGetValue(ch, out var fallback))
        {
            fallback(ctx);
            return true;
        }

        return false;
    }

    private void ClearBuffer()
    {
        _buffer = "";
        _clearTimer?.Stop();
    }

    private static string? KeyToChar(Key key, bool shift) => key switch
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

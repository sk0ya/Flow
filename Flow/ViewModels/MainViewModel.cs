using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flow.Models;
using Flow.Services;
using Microsoft.Win32;

namespace Flow.ViewModels;

public record SuggestionItem(string Value, string Source);

public partial class MainViewModel : ObservableObject
{
    private readonly DependencyService _dep  = new();
    private readonly StorageService    _stor = new();

    [ObservableProperty] private string  _projectName     = "新しいプロジェクト";
    [ObservableProperty] private string? _currentFilePath;
    [ObservableProperty] private ItemViewModel? _selectedItem;
    [ObservableProperty] private string  _newItemName      = "";
    [ObservableProperty] private string  _timeUnit         = "日";
    [ObservableProperty] private double  _totalDuration    = 10.0;
    [ObservableProperty] private double  _projectDuration;
    [ObservableProperty] private bool    _hasErrors;

    // Undo delete
    [ObservableProperty] private bool   _canUndoDelete;
    [ObservableProperty] private string _undoMessage = "";
    private ItemViewModel? _deletedItem;

    public ObservableCollection<LaneViewModel>   Lanes          { get; } = new();
    public ObservableCollection<ItemViewModel>   Items          { get; } = new();
    public ObservableCollection<ValidationError> Errors         { get; } = new();
    public ObservableCollection<DependencyEdge>  DependencyEdges{ get; } = new();

    public static readonly string[] TimeUnitOptions = { "分", "時間", "日", "週", "スプリント" };

    public string WindowTitle => CurrentFilePath != null
        ? $"Flow — {System.IO.Path.GetFileNameWithoutExtension(CurrentFilePath)}"
        : "Flow";

    public MainViewModel()
    {
        Lanes.Add(new LaneViewModel("レーン 1"));
        Items.CollectionChanged += (_, _) => Analyze();
    }

    // ── Suggestions ──────────────────────────────────────────────────────

    public System.Collections.Generic.List<SuggestionItem> GetPreSuggestions(Guid forId, string filter) =>
        Items.Where(i => i.Id != forId)
             .SelectMany(i => i.PostConditions.Select(e => new SuggestionItem(e.Value, i.Name)))
             .GroupBy(s => s.Value, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
             .Where(s => string.IsNullOrEmpty(filter) || s.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
             .OrderBy(s => s.Value).Take(8).ToList();

    public System.Collections.Generic.List<SuggestionItem> GetPostSuggestions(Guid forId, string filter) =>
        Items.Where(i => i.Id != forId)
             .SelectMany(i => i.PreConditions.Select(e => new SuggestionItem(e.Value, i.Name)))
             .GroupBy(s => s.Value, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
             .Where(s => string.IsNullOrEmpty(filter) || s.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
             .OrderBy(s => s.Value).Take(8).ToList();

    // ── Lane commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void AddLane()
    {
        Lanes.Add(new LaneViewModel($"レーン {Lanes.Count + 1}"));
    }

    [RelayCommand]
    private void DeleteLane(LaneViewModel? lane)
    {
        if (lane == null || Lanes.Count <= 1) return;
        var firstLaneId = Lanes.First(l => l.Id != lane.Id).Id;
        foreach (var item in Items.Where(i => i.LaneId == lane.Id))
            item.LaneId = firstLaneId;
        Lanes.Remove(lane);
        Analyze();
    }

    // ── Item commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void AddItem()
    {
        var name = NewItemName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var laneId = Lanes.FirstOrDefault()?.Id ?? Guid.Empty;

        // Place after the last item in the target lane (never overlap)
        double startTime = Items
            .Where(i => i.LaneId == laneId)
            .Select(i => i.StartTime + i.Duration)
            .DefaultIfEmpty(0)
            .Max();

        var vm = new ItemViewModel
        {
            Name      = name,
            LaneId    = laneId,
            StartTime = startTime,
            Duration  = 1.0,
        };
        Subscribe(vm);
        Items.Add(vm);
        SelectedItem = vm;
        NewItemName  = "";
    }

    [RelayCommand]
    private void SelectItem(ItemViewModel? vm) => SelectedItem = vm;

    [RelayCommand]
    private async Task DeleteItem(ItemViewModel? item)
    {
        if (item == null) return;
        _deletedItem = item;
        Items.Remove(item);
        if (SelectedItem?.Id == item.Id) SelectedItem = null;

        UndoMessage  = $"「{item.Name}」を削除しました";
        CanUndoDelete = true;
        await Task.Delay(4000);
        if (CanUndoDelete) { CanUndoDelete = false; _deletedItem = null; }
    }

    [RelayCommand]
    private void UndoDelete()
    {
        if (_deletedItem == null) return;
        Subscribe(_deletedItem);
        Items.Add(_deletedItem);
        SelectedItem  = _deletedItem;
        _deletedItem  = null;
        CanUndoDelete = false;
    }

    [RelayCommand]
    private void DeleteSelectedItem()
    {
        if (SelectedItem != null) _ = DeleteItemCommand.ExecuteAsync(SelectedItem);
    }

    // ── File commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void NewProject()
    {
        Items.Clear();
        Lanes.Clear();
        Lanes.Add(new LaneViewModel("レーン 1"));
        ProjectName      = "新しいプロジェクト";
        TimeUnit         = "日";
        TotalDuration    = 10.0;
        CurrentFilePath  = null;
        SelectedItem     = null;
        CanUndoDelete    = false;
        OnPropertyChanged(nameof(WindowTitle));
    }

    [RelayCommand]
    private void SaveProject()
    {
        if (CurrentFilePath == null) { SaveProjectAs(); return; }
        DoSave(CurrentFilePath);
    }

    [RelayCommand]
    private void SaveProjectAs()
    {
        var dlg = new SaveFileDialog { Filter = "Flow Project (*.flow)|*.flow", DefaultExt = "flow", FileName = ProjectName };
        if (dlg.ShowDialog() != true) return;
        CurrentFilePath = dlg.FileName;
        ProjectName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        DoSave(CurrentFilePath);
        OnPropertyChanged(nameof(WindowTitle));
    }

    [RelayCommand]
    private void OpenProject()
    {
        var dlg = new OpenFileDialog { Filter = "Flow Project (*.flow)|*.flow" };
        if (dlg.ShowDialog() != true) return;
        var p = _stor.Load(dlg.FileName);
        Items.Clear(); Lanes.Clear();
        ProjectName     = p.Name;
        TimeUnit        = p.TimeUnit;
        TotalDuration   = p.TotalDuration;
        CurrentFilePath = dlg.FileName;
        foreach (var l in p.Lanes) Lanes.Add(new LaneViewModel(l));
        if (Lanes.Count == 0) Lanes.Add(new LaneViewModel("レーン 1"));
        foreach (var m in p.Items) { var vm = new ItemViewModel(m); Subscribe(vm); Items.Add(vm); }
        SelectedItem = null; CanUndoDelete = false;
        OnPropertyChanged(nameof(WindowTitle));
        Analyze();
    }

    private void DoSave(string path)
    {
        _stor.Save(new SequenceProject
        {
            Name          = ProjectName,
            TimeUnit      = TimeUnit,
            TotalDuration = TotalDuration,
            Lanes         = Lanes.Select(l => l.ToModel()).ToList(),
            Items         = Items.Select(v => v.ToModel()).ToList(),
        }, path);
    }

    private bool _resolving;

    private void Subscribe(ItemViewModel vm)
    {
        vm.PreConditions.CollectionChanged  += (_, _) => Analyze();
        vm.PostConditions.CollectionChanged += (_, _) => Analyze();
        vm.PropertyChanged += (_, e) =>
        {
            if (_resolving) return;
            // When lane changes via dropdown, auto-place to avoid overlap
            if (e.PropertyName == nameof(ItemViewModel.LaneId))
            {
                _resolving = true;
                PlaceWithoutOverlap(vm, vm.LaneId);
                _resolving = false;
            }
            if (e.PropertyName is nameof(ItemViewModel.Name)
                               or nameof(ItemViewModel.StartTime)
                               or nameof(ItemViewModel.Duration)
                               or nameof(ItemViewModel.LaneId))
                Analyze();
        };
    }

    // Find the nearest valid StartTime in targetLaneId that fits vm without overlap.
    // Called when lane changes via the editor dropdown.
    private void PlaceWithoutOverlap(ItemViewModel vm, Guid targetLaneId)
    {
        var others = Items
            .Where(i => i.Id != vm.Id && i.LaneId == targetLaneId)
            .OrderBy(i => i.StartTime)
            .ToList();

        bool Overlaps(double start) => others.Any(o =>
            start < o.StartTime + o.Duration - 1e-9 &&
            start + vm.Duration > o.StartTime + 1e-9);

        if (!Overlaps(vm.StartTime)) return;

        // Find the first gap that fits
        double cursor = 0;
        foreach (var other in others)
        {
            if (other.StartTime > cursor + vm.Duration - 1e-9)
                break; // gap before this item is wide enough
            cursor = Math.Max(cursor, other.StartTime + other.Duration);
        }
        vm.StartTime = cursor;
    }

    // ── Analysis ─────────────────────────────────────────────────────────

    public void Analyze()
    {
        var models = Items.Select(v => v.ToModel()).ToList();
        var result = _dep.Analyze(models);

        // Build lookup maps for condition link info
        var producers = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var vm in Items)
            foreach (var e in vm.PostConditions)
            {
                var k = e.Value.Trim();
                if (!producers.TryGetValue(k, out var list)) producers[k] = list = new();
                list.Add(vm.Name);
            }

        var consumers = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var vm in Items)
            foreach (var e in vm.PreConditions)
            {
                var k = e.Value.Trim();
                if (!consumers.TryGetValue(k, out var list)) consumers[k] = list = new();
                list.Add(vm.Name);
            }

        foreach (var vm in Items)
        {
            var errs = result.Errors.Where(e => e.ItemId == vm.Id).ToList();
            vm.HasErrors    = errs.Count > 0;
            vm.ErrorMessage = string.Join("\n", errs.Select(e => e.Message));

            foreach (var entry in vm.PreConditions)
            {
                var k = entry.Value.Trim();
                if (producers.TryGetValue(k, out var p))
                { entry.IsLinked = true; entry.LinkDetail = "← " + string.Join(", ", p); }
                else
                { entry.IsLinked = false; entry.LinkDetail = "未解決 — この条件を提供するタスクがありません"; }
            }

            foreach (var entry in vm.PostConditions)
            {
                var k = entry.Value.Trim();
                if (consumers.TryGetValue(k, out var c))
                {
                    var others = c.Where(n => n != vm.Name).ToList();
                    entry.IsLinked   = others.Count > 0;
                    entry.LinkDetail = others.Count > 0 ? "→ " + string.Join(", ", others) : "この条件を必要とするタスクがありません";
                }
                else
                { entry.IsLinked = false; entry.LinkDetail = "この条件を必要とするタスクがありません"; }
            }
        }

        Errors.Clear();
        foreach (var e in result.Errors) Errors.Add(e);
        HasErrors      = Errors.Count > 0;
        ProjectDuration = result.ProjectDuration;

        DependencyEdges.Clear();
        foreach (var edge in result.Edges) DependencyEdges.Add(edge);
    }

    // ── Partial property change handlers ─────────────────────────────────

    partial void OnTimeUnitChanged(string value) => Analyze();
    partial void OnTotalDurationChanged(double value) => Analyze();
}

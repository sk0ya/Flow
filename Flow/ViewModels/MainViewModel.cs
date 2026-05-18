using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flow.Models;
using Flow.Services;
using Microsoft.Win32;

namespace Flow.ViewModels;

public record SuggestionItem(string Value, string Source);

public enum SidebarPanel { ProjectList, ProjectSettings, TaskEditor, AppSettings }

public partial class MainViewModel : ObservableObject
{
    private readonly DependencyService _dep             = new();
    private readonly StorageService    _stor            = new();
    private readonly AppStateService   _appStateService = new();
    private readonly AppState          _appState;
    private readonly DispatcherTimer   _autoSaveTimer;
    private readonly DispatcherTimer   _cellDurationTimer;
    private string _cellDurationText = "";
    private bool _suspendAutoSave;
    private bool _initializingAppearance;

    private static readonly ThemeOption[] AvailableThemeOptions =
    {
        new(ThemeService.LightThemeKey, "ライト", "明るいベースで見やすく表示します"),
        new(ThemeService.DarkThemeKey, "ダーク", "作業面を落ち着いた配色に切り替えます"),
    };

    private static readonly AccentColorOption[] AvailableAccentOptions =
    {
        new("ブルー", "#4285F4"),
        new("ティール", "#0EA5A4"),
        new("グリーン", "#2EAD67"),
        new("オレンジ", "#F59E0B"),
        new("ローズ", "#EC4899"),
        new("バイオレット", "#8B5CF6"),
    };

    [ObservableProperty] private string         _projectName = "新しいプロジェクト";
    [ObservableProperty] private string?        _currentFilePath;
    [ObservableProperty] private RecentProjectEntry? _selectedRecentProject;
    [ObservableProperty] private ItemViewModel? _selectedItem;
    [ObservableProperty] private string         _newItemName   = "";
    [ObservableProperty] private string         _timeUnit      = "日";
    [ObservableProperty] private double         _cellDuration  = 1.0;
    [ObservableProperty] private double         _totalDuration = 10.0;
    [ObservableProperty] private double         _projectDuration;
    [ObservableProperty] private bool           _hasErrors;
    [ObservableProperty] private ThemeOption?   _selectedThemeOption;
    [ObservableProperty] private AccentColorOption? _selectedAccentColorOption;

    [ObservableProperty] private string _newCategoryName = "";

    // Undo delete
    [ObservableProperty] private bool   _canUndoDelete;
    [ObservableProperty] private string _undoMessage = "";
    private ItemViewModel? _deletedItem;

    [ObservableProperty] private string? _cellDurationError;

    public ObservableCollection<CategoryViewModel>  Categories      { get; } = new();
    public ObservableCollection<LaneViewModel>      Lanes           { get; } = new();
    public ObservableCollection<ItemViewModel>      Items           { get; } = new();
    public ObservableCollection<ValidationError>    Errors          { get; } = new();
    public ObservableCollection<DependencyEdge>     DependencyEdges { get; } = new();
    public ObservableCollection<RecentProjectEntry> RecentProjects  { get; } = new();
    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new();
    public ObservableCollection<AccentColorOption> AccentColorOptions { get; } = new();

    public IEnumerable<CategoryViewModel> CategoriesForPicker =>
        Enumerable.Repeat(CategoryViewModel.None, 1).Concat(Categories);

    public static readonly string[] TimeUnitOptions = { "秒", "分", "時間", "日", "週", "スプリント" };

    public bool HasRecentProjects => RecentProjects.Count > 0;

    public double CellDurationValue
    {
        get => ConvertCellDurationToDisplay(CellDuration);
        set => CellDuration = ConvertDisplayToCellDuration(value);
    }

    public string CellDurationText
    {
        get => _cellDurationText;
        set
        {
            if (_cellDurationText == value) return;
            _cellDurationText = value;
            OnPropertyChanged();
            CellDurationError = null;
            _cellDurationTimer.Stop();
            _cellDurationTimer.Start();
        }
    }

    public bool HasCellDurationError => CellDurationError != null;

    public string CellDurationUnitLabel => GetCellDurationUnitLabel();

    public string CellDurationSummary => $"1マス = {FormatNumber(CellDurationValue)} {CellDurationUnitLabel}";

    // ── Sidebar panel ─────────────────────────────────────────────────────

    private SidebarPanel _activeSidebarPanel = SidebarPanel.ProjectList;

    public bool IsProjectListActive
    {
        get => _activeSidebarPanel == SidebarPanel.ProjectList;
        set { if (value) SetActivePanel(SidebarPanel.ProjectList); }
    }

    public bool IsProjectSettingsActive
    {
        get => _activeSidebarPanel == SidebarPanel.ProjectSettings;
        set { if (value) SetActivePanel(SidebarPanel.ProjectSettings); }
    }

    public bool IsTaskEditorActive
    {
        get => _activeSidebarPanel == SidebarPanel.TaskEditor;
        set { if (value) SetActivePanel(SidebarPanel.TaskEditor); }
    }

    public bool IsAppSettingsActive
    {
        get => _activeSidebarPanel == SidebarPanel.AppSettings;
        set { if (value) SetActivePanel(SidebarPanel.AppSettings); }
    }

    private void SetActivePanel(SidebarPanel panel)
    {
        _activeSidebarPanel = panel;
        if (panel == SidebarPanel.ProjectList)
            SyncSelectedRecentProject();
        OnPropertyChanged(nameof(IsProjectListActive));
        OnPropertyChanged(nameof(IsProjectSettingsActive));
        OnPropertyChanged(nameof(IsTaskEditorActive));
        OnPropertyChanged(nameof(IsAppSettingsActive));
    }

    public MainViewModel(string? startupProjectPath = null)
    {
        _appState = _appStateService.Load();
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _autoSaveTimer.Tick += (_, _) => FlushAutoSave();
        _cellDurationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _cellDurationTimer.Tick += OnCellDurationTimerTick;
        _cellDurationText = FormatNumber(ConvertCellDurationToDisplay(_cellDuration));

        InitializeAppearance();

        Categories.CollectionChanged += OnCategoriesCollectionChanged;
        Lanes.CollectionChanged += OnLanesCollectionChanged;
        Items.CollectionChanged += OnItemsCollectionChanged;
        Lanes.Add(new LaneViewModel("レーン 1"));

        if (PruneInvalidProjectState())
            PersistAppState();

        RefreshRecentProjects();
        TryRestoreStartupProject(startupProjectPath);
    }

    private void InitializeAppearance()
    {
        foreach (var option in AvailableThemeOptions)
            ThemeOptions.Add(option);

        foreach (var option in AvailableAccentOptions)
            AccentColorOptions.Add(option);

        string themeKey = ThemeService.NormalizeThemeKey(_appState.ThemeKey);
        string accentColor = ThemeService.NormalizeAccentColor(_appState.AccentColor);

        bool stateChanged = !string.Equals(_appState.ThemeKey, themeKey, StringComparison.Ordinal)
                         || !string.Equals(_appState.AccentColor, accentColor, StringComparison.OrdinalIgnoreCase);

        _initializingAppearance = true;
        SelectedThemeOption = ThemeOptions.First(option => option.Key == themeKey);
        SelectedAccentColorOption = AccentColorOptions.FirstOrDefault(option =>
            string.Equals(option.ColorHex, accentColor, StringComparison.OrdinalIgnoreCase))
            ?? AccentColorOptions.First();
        _initializingAppearance = false;

        ApplyAppearance(persistState: stateChanged);
    }

    // ── Suggestions ──────────────────────────────────────────────────────

    public List<SuggestionItem> GetPreSuggestions(Guid forId, string filter) =>
        Items.Where(i => i.Id != forId)
             .SelectMany(i => i.PostConditions.Select(e => new SuggestionItem(e.Value, i.Name)))
             .GroupBy(s => s.Value, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
             .Where(s => string.IsNullOrEmpty(filter) || s.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
             .OrderBy(s => s.Value).Take(8).ToList();

    public List<SuggestionItem> GetPostSuggestions(Guid forId, string filter) =>
        Items.Where(i => i.Id != forId)
             .SelectMany(i => i.PreConditions.Select(e => new SuggestionItem(e.Value, i.Name)))
             .GroupBy(s => s.Value, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
             .Where(s => string.IsNullOrEmpty(filter) || s.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
             .OrderBy(s => s.Value).Take(8).ToList();

    // ── Category color palette ────────────────────────────────────────────

    private static readonly string[] CategoryColorPalette =
    {
        "#60A5FA", "#34D399", "#FBBF24", "#A78BFA",
        "#F472B6", "#38BDF8", "#FB923C", "#F87171",
    };

    private string NextCategoryColor() =>
        CategoryColorPalette[Categories.Count % CategoryColorPalette.Length];

    // ── Category commands ─────────────────────────────────────────────────

    [RelayCommand]
    private void AddCategory()
    {
        var name = NewCategoryName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var cat = new CategoryViewModel(Guid.NewGuid(), name, NextCategoryColor());
        Subscribe(cat);
        Categories.Add(cat);
        NewCategoryName = "";
        OnPropertyChanged(nameof(CategoriesForPicker));
    }

    [RelayCommand]
    private void DeleteCategory(CategoryViewModel? cat)
    {
        if (cat == null) return;
        foreach (var item in Items.Where(i => i.CategoryId == cat.Id))
            item.CategoryId = Guid.Empty;
        Categories.Remove(cat);
        OnPropertyChanged(nameof(CategoriesForPicker));
        Analyze();
        RequestAutoSave();
    }

    // ── Lane commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void AddLane() => Lanes.Add(new LaneViewModel(NextLaneName()));

    public Guid AddNewLane()
    {
        var lane = new LaneViewModel(NextLaneName());
        Lanes.Add(lane);
        return lane.Id;
    }

    public void ReorderLane(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Lanes.Count) return;
        if (toIndex   < 0 || toIndex   >= Lanes.Count) return;
        Lanes.Move(fromIndex, toIndex);
    }

    private string NextLaneName()
    {
        const string prefix = "レーン ";
        int max = Lanes
            .Select(l => l.Name)
            .Where(n => n.StartsWith(prefix) && int.TryParse(n[prefix.Length..], out _))
            .Select(n => int.Parse(n[prefix.Length..]))
            .DefaultIfEmpty(0).Max();
        return $"{prefix}{max + 1}";
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

        var laneId = SelectedItem?.LaneId ?? Lanes.FirstOrDefault()?.Id ?? Guid.Empty;
        if (laneId == Guid.Empty) return;

        double startTime = SelectedItem != null
            ? SelectedItem.StartTime + SelectedItem.Duration
            : Items.Where(i => i.LaneId == laneId)
                   .Select(i => i.StartTime + i.Duration)
                   .DefaultIfEmpty(0)
                   .Max();

        CreateItem(name, laneId, startTime);
        NewItemName  = "";
    }

    public ItemViewModel? AddNewItemAt(Guid laneId, double proposedStartTime)
    {
        var targetLaneId = Lanes.Any(l => l.Id == laneId)
            ? laneId
            : Lanes.FirstOrDefault()?.Id ?? Guid.Empty;
        if (targetLaneId == Guid.Empty) return null;

        var item = CreateItem("新しいタスク", targetLaneId, proposedStartTime);
        SetActivePanel(SidebarPanel.TaskEditor);
        return item;
    }

    public void DiscardNewItem(ItemViewModel? item)
    {
        if (item == null) return;
        if (!Items.Remove(item)) return;
        if (SelectedItem?.Id == item.Id) SelectedItem = null;
    }

    [RelayCommand]
    private void SelectItem(ItemViewModel? vm)
    {
        SelectedItem = vm;
        if (vm != null) SetActivePanel(SidebarPanel.TaskEditor);
    }

    [RelayCommand]
    private async Task DeleteItem(ItemViewModel? item)
    {
        if (item == null) return;
        _deletedItem = item;
        Items.Remove(item);
        if (SelectedItem?.Id == item.Id) SelectedItem = null;

        UndoMessage   = $"「{item.Name}」を削除しました";
        CanUndoDelete = true;
        await Task.Delay(4000);
        if (CanUndoDelete) { CanUndoDelete = false; _deletedItem = null; }
    }

    [RelayCommand]
    private void UndoDelete()
    {
        if (_deletedItem == null) return;
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
        RunWithoutAutoSave(() =>
        {
            Items.Clear();
            Lanes.Clear();
            Categories.Clear();
            Lanes.Add(new LaneViewModel("レーン 1"));
            ProjectName     = "新しいプロジェクト";
            TimeUnit        = "日";
            CellDuration    = 1.0;
            TotalDuration   = 10.0;
            CurrentFilePath = null;
            SelectedItem    = null;
            CanUndoDelete   = false;
            OnPropertyChanged(nameof(CategoriesForPicker));
            SetActivePanel(SidebarPanel.ProjectSettings);
        });
    }

    [RelayCommand]
    private void SaveProject()
    {
        if (CurrentFilePath == null) { SaveProjectAs(); return; }
        TrySaveProject(CurrentFilePath);
    }

    [RelayCommand]
    private void SaveProjectAs()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Flow Project (*.flow)|*.flow",
            DefaultExt = "flow",
            FileName = ProjectName
        };

        if (dlg.ShowDialog() != true) return;

        var originalProjectName = ProjectName;
        bool saved = false;
        RunWithoutAutoSave(() =>
        {
            ProjectName = Path.GetFileNameWithoutExtension(dlg.FileName);
            saved = TrySaveProject(dlg.FileName);
            if (!saved)
                ProjectName = originalProjectName;
        });
    }

    [RelayCommand]
    private void OpenProject()
    {
        var dlg = new OpenFileDialog { Filter = "Flow Project (*.flow)|*.flow" };
        if (dlg.ShowDialog() != true) return;
        TryLoadProject(dlg.FileName, showErrorMessage: true);
    }

    [RelayCommand]
    private void OpenRecentProject(RecentProjectEntry? recentProject)
    {
        if (recentProject == null) return;
        TryLoadProject(recentProject.FilePath, showErrorMessage: true);
    }

    private bool TryLoadProject(string filePath, bool showErrorMessage)
    {
        var normalizedPath = NormalizeProjectPath(filePath);
        if (normalizedPath == null)
        {
            RemoveRecentProject(filePath);
            if (showErrorMessage)
            {
                MessageBox.Show("指定されたプロジェクト パスが不正です。", "Flow",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }

        if (!File.Exists(normalizedPath))
        {
            RemoveRecentProject(normalizedPath);
            if (showErrorMessage)
            {
                MessageBox.Show($"プロジェクト ファイルが見つかりません。\n{normalizedPath}", "Flow",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        try
        {
            var project = _stor.Load(normalizedPath);
            ApplyProject(project, normalizedPath);
            RememberProject(normalizedPath);
            return true;
        }
        catch (Exception ex)
        {
            if (showErrorMessage)
            {
                MessageBox.Show($"プロジェクトを開けませんでした。\n{ex.Message}", "Flow",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }
    }

    private bool TrySaveProject(string filePath, bool showErrorMessage = true)
    {
        _autoSaveTimer.Stop();

        var normalizedPath = NormalizeProjectPath(filePath);
        if (normalizedPath == null)
        {
            if (showErrorMessage)
            {
                MessageBox.Show("保存先パスが不正です。", "Flow",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }

        try
        {
            _stor.Save(new SequenceProject
            {
                Name          = ProjectName,
                TimeUnit      = TimeUnit,
                CellDuration  = CellDuration,
                TotalDuration = TotalDuration,
                Categories    = Categories.Select(c => c.ToModel()).ToList(),
                Lanes         = Lanes.Select(l => l.ToModel()).ToList(),
                Items         = Items.Select(v => v.ToModel()).ToList(),
            }, normalizedPath);

            CurrentFilePath = normalizedPath;
            RememberProject(normalizedPath);
            return true;
        }
        catch (Exception ex)
        {
            if (showErrorMessage)
            {
                MessageBox.Show($"プロジェクトを保存できませんでした。\n{ex.Message}", "Flow",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }
    }

    private void ApplyProject(SequenceProject project, string filePath)
    {
        RunWithoutAutoSave(() =>
        {
            Items.Clear();
            Lanes.Clear();
            Categories.Clear();

            ProjectName = string.IsNullOrWhiteSpace(project.Name)
                ? Path.GetFileNameWithoutExtension(filePath)
                : project.Name;
            TimeUnit = project.TimeUnit;
            CellDuration = project.CellDuration > 0
                ? project.CellDuration
                : project.GridDivisions is > 0 ? 1.0 / project.GridDivisions.Value : 1.0;
            SyncCellDurationText();
            TotalDuration = project.TotalDuration;
            CurrentFilePath = filePath;

            foreach (var cat in project.Categories)
            {
                var vm = new CategoryViewModel(cat);
                Subscribe(vm);
                Categories.Add(vm);
            }

            OnPropertyChanged(nameof(CategoriesForPicker));

            foreach (var lane in project.Lanes)
                Lanes.Add(new LaneViewModel(lane));

            if (Lanes.Count == 0)
                Lanes.Add(new LaneViewModel("レーン 1"));

            foreach (var model in project.Items)
            {
                var vm = new ItemViewModel(model);
                Subscribe(vm);
                Items.Add(vm);
            }

            SelectedItem  = null;
            CanUndoDelete = false;
            SetActivePanel(SidebarPanel.ProjectList);
            Analyze();
        });
    }

    private void TryRestoreStartupProject(string? startupProjectPath)
    {
        if (!string.IsNullOrWhiteSpace(startupProjectPath))
        {
            TryLoadProject(startupProjectPath, showErrorMessage: true);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_appState.LastProjectPath))
            TryLoadProject(_appState.LastProjectPath, showErrorMessage: false);
    }

    private void RememberProject(string filePath)
    {
        bool isLastProjectSame = string.Equals(_appState.LastProjectPath, filePath, StringComparison.OrdinalIgnoreCase);
        bool isTopRecentSame = _appState.RecentProjectPaths.Count > 0 &&
                               string.Equals(_appState.RecentProjectPaths[0], filePath, StringComparison.OrdinalIgnoreCase);
        if (isLastProjectSame && isTopRecentSame)
            return;

        _appState.LastProjectPath = filePath;
        _appState.RecentProjectPaths.RemoveAll(path =>
            string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
        _appState.RecentProjectPaths.Insert(0, filePath);

        if (_appState.RecentProjectPaths.Count > 10)
        {
            _appState.RecentProjectPaths.RemoveRange(
                10,
                _appState.RecentProjectPaths.Count - 10);
        }

        PersistAppState();
        RefreshRecentProjects();
    }

    private void RemoveRecentProject(string filePath)
    {
        var normalizedPath = NormalizeProjectPath(filePath) ?? filePath;
        bool changed = _appState.RecentProjectPaths.RemoveAll(path =>
            string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase)) > 0;

        if (string.Equals(_appState.LastProjectPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            _appState.LastProjectPath = null;
            changed = true;
        }

        if (!changed) return;

        PersistAppState();
        RefreshRecentProjects();
    }

    private bool PruneInvalidProjectState()
    {
        var validPaths = new List<string>();
        foreach (var path in _appState.RecentProjectPaths)
        {
            var normalizedPath = NormalizeProjectPath(path);
            if (normalizedPath == null || !File.Exists(normalizedPath)) continue;
            if (validPaths.Any(existing =>
                    string.Equals(existing, normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            validPaths.Add(normalizedPath);
        }

        bool changed = !validPaths.SequenceEqual(
            _appState.RecentProjectPaths,
            StringComparer.OrdinalIgnoreCase);

        if (changed)
            _appState.RecentProjectPaths = validPaths;

        var normalizedLastProjectPath = NormalizeProjectPath(_appState.LastProjectPath);
        if (normalizedLastProjectPath == null || !File.Exists(normalizedLastProjectPath))
        {
            if (!string.IsNullOrWhiteSpace(_appState.LastProjectPath))
            {
                _appState.LastProjectPath = null;
                changed = true;
            }
        }
        else if (!string.Equals(_appState.LastProjectPath, normalizedLastProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            _appState.LastProjectPath = normalizedLastProjectPath;
            changed = true;
        }

        return changed;
    }

    private void RefreshRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var filePath in _appState.RecentProjectPaths)
            RecentProjects.Add(new RecentProjectEntry(filePath));

        OnPropertyChanged(nameof(HasRecentProjects));
        SyncSelectedRecentProject();
    }

    private void PersistAppState()
    {
        try
        {
            _appStateService.Save(_appState);
        }
        catch
        {
            // Ignore state persistence errors. The project file itself is the primary data.
        }
    }

    private static string? NormalizeProjectPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            return Path.GetFullPath(filePath);
        }
        catch
        {
            return null;
        }
    }

    private bool _resolving;

    private void OnCategoriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CategoriesForPicker));
        Analyze();
        RequestAutoSave();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Analyze();
        RequestAutoSave();
    }

    private void OnLanesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace)
        {
            if (e.NewItems != null)
            {
                foreach (LaneViewModel lane in e.NewItems)
                    Subscribe(lane);
            }
        }

        RequestAutoSave();
    }

    private void Subscribe(CategoryViewModel vm)
    {
        vm.PropertyChanged += (_, _) =>
        {
            Analyze();
            RequestAutoSave();
        };
    }

    private void Subscribe(LaneViewModel vm)
    {
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LaneViewModel.Name))
                RequestAutoSave();
        };
    }

    private void Subscribe(ItemViewModel vm)
    {
        vm.PreConditions.CollectionChanged  += (_, _) => { Analyze(); RequestAutoSave(); };
        vm.PostConditions.CollectionChanged += (_, _) => { Analyze(); RequestAutoSave(); };
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
                               or nameof(ItemViewModel.LaneId)
                               or nameof(ItemViewModel.CategoryId))
                Analyze();

            if (e.PropertyName is nameof(ItemViewModel.Name)
                               or nameof(ItemViewModel.Description)
                               or nameof(ItemViewModel.StartTime)
                               or nameof(ItemViewModel.Duration)
                               or nameof(ItemViewModel.LaneId)
                               or nameof(ItemViewModel.CategoryId))
            {
                RequestAutoSave();
            }
        };
    }

    private ItemViewModel CreateItem(string name, Guid laneId, double proposedStartTime)
    {
        double defaultDuration = GetDefaultItemDuration();
        var vm = new ItemViewModel
        {
            Name      = name,
            LaneId    = laneId,
            StartTime = FindAvailableStartAtOrAfter(laneId, proposedStartTime, defaultDuration),
            Duration  = defaultDuration,
        };

        Subscribe(vm);
        Items.Add(vm);
        SelectedItem = vm;
        return vm;
    }

    private double FindAvailableStartAtOrAfter(Guid laneId, double proposedStartTime, double duration)
    {
        double start = Math.Max(0, proposedStartTime);
        var others = Items
            .Where(i => i.LaneId == laneId)
            .OrderBy(i => i.StartTime)
            .ToList();

        foreach (var other in others)
        {
            double otherEnd = other.StartTime + other.Duration;
            if (start + duration <= other.StartTime + 1e-9)
                break;

            if (start < otherEnd - 1e-9)
                start = otherEnd;
        }

        return Math.Round(start, 10, MidpointRounding.AwayFromZero);
    }

    private double GetDefaultItemDuration() => NormalizeCellDuration(CellDuration) * GetSecondsPerUnit();

    // Find the nearest valid StartTime in targetLaneId that fits vm without overlap.
    // Called when lane changes via the editor dropdown.
    private void PlaceWithoutOverlap(ItemViewModel vm, Guid targetLaneId)
    {
        var others = Items
            .Where(i => i.Id != vm.Id && i.LaneId == targetLaneId)
            .OrderBy(i => i.StartTime)
            .ToList();

        bool overlaps(double start) => others.Any(o =>
            start < o.StartTime + o.Duration - 1e-9 &&
            start + vm.Duration > o.StartTime + 1e-9);

        if (!overlaps(vm.StartTime)) return;

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
        var producers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var vm in Items)
            foreach (var e in vm.PostConditions)
            {
                var k = e.Value.Trim();
                if (!producers.TryGetValue(k, out var list)) producers[k] = list = new();
                list.Add(vm.Name);
            }

        var consumers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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
        HasErrors       = Errors.Count > 0;
        ProjectDuration = result.ProjectDuration;

        DependencyEdges.Clear();
        foreach (var edge in result.Edges) DependencyEdges.Add(edge);
    }

    // ── Partial property change handlers ─────────────────────────────────

    partial void OnTimeUnitChanged(string value)
    {
        CellDuration = NormalizeCellDuration(CellDuration);
        SyncCellDurationText();
        OnPropertyChanged(nameof(CellDurationValue));
        OnPropertyChanged(nameof(CellDurationUnitLabel));
        OnPropertyChanged(nameof(CellDurationSummary));
        Analyze();
        RequestAutoSave();
    }

    partial void OnCellDurationChanged(double value)
    {
        double normalized = NormalizeCellDuration(value);
        if (Math.Abs(normalized - value) > 1e-9)
        {
            CellDuration = normalized;
            return;
        }

        SyncCellDurationText();
        OnPropertyChanged(nameof(CellDurationValue));
        OnPropertyChanged(nameof(CellDurationUnitLabel));
        OnPropertyChanged(nameof(CellDurationSummary));
        RequestAutoSave();
    }

    partial void OnCellDurationErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasCellDurationError));
    }

    private void OnCellDurationTimerTick(object? sender, EventArgs e)
    {
        _cellDurationTimer.Stop();

        if (!double.TryParse(_cellDurationText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out var displayValue) &&
            !double.TryParse(_cellDurationText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out displayValue))
        {
            CellDurationError = "数値を入力してください";
            return;
        }

        if (displayValue <= 0)
        {
            CellDurationError = "0より大きい値を入力してください";
            return;
        }

        double converted = ConvertDisplayToCellDuration(displayValue);
        if (converted < 0.0001)
        {
            CellDurationError = $"最小値は {FormatNumber(ConvertCellDurationToDisplay(0.0001))} {CellDurationUnitLabel} です";
            return;
        }

        CellDurationError = null;
        CellDuration = converted;
    }

    private void SyncCellDurationText()
    {
        string newText = FormatNumber(ConvertCellDurationToDisplay(CellDuration));
        if (_cellDurationText == newText) return;
        _cellDurationTimer.Stop();
        _cellDurationText = newText;
        OnPropertyChanged(nameof(CellDurationText));
        CellDurationError = null;
    }

    partial void OnProjectNameChanged(string value) => RequestAutoSave();

    partial void OnSelectedThemeOptionChanged(ThemeOption? value)
    {
        if (_initializingAppearance || value == null)
            return;

        ApplyAppearance();
    }

    partial void OnSelectedAccentColorOptionChanged(AccentColorOption? value)
    {
        if (_initializingAppearance || value == null)
            return;

        ApplyAppearance();
    }

    partial void OnCurrentFilePathChanged(string? value)
    {
        SyncSelectedRecentProject();
    }

    partial void OnTotalDurationChanged(double value)
    {
        Analyze();
        RequestAutoSave();
    }

    private void ApplyAppearance(bool persistState = true)
    {
        if (SelectedThemeOption == null || SelectedAccentColorOption == null)
            return;

        string themeKey = ThemeService.NormalizeThemeKey(SelectedThemeOption.Key);
        string accentColor = ThemeService.NormalizeAccentColor(SelectedAccentColorOption.ColorHex);

        ThemeService.ApplyTheme(themeKey, accentColor);

        bool stateChanged = !string.Equals(_appState.ThemeKey, themeKey, StringComparison.Ordinal)
                         || !string.Equals(_appState.AccentColor, accentColor, StringComparison.OrdinalIgnoreCase);

        _appState.ThemeKey = themeKey;
        _appState.AccentColor = accentColor;

        if (persistState && stateChanged)
            PersistAppState();
    }

    private void RequestAutoSave()
    {
        if (_suspendAutoSave || string.IsNullOrWhiteSpace(CurrentFilePath))
            return;

        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void FlushAutoSave()
    {
        _autoSaveTimer.Stop();

        if (_suspendAutoSave || string.IsNullOrWhiteSpace(CurrentFilePath))
            return;

        TrySaveProject(CurrentFilePath, showErrorMessage: false);
    }

    private void RunWithoutAutoSave(Action action)
    {
        bool wasSuspended = _suspendAutoSave;
        _suspendAutoSave = true;
        _autoSaveTimer.Stop();
        try
        {
            action();
        }
        finally
        {
            _suspendAutoSave = wasSuspended;
        }
    }

    private void SyncSelectedRecentProject()
    {
        var normalizedCurrentPath = NormalizeProjectPath(CurrentFilePath);
        SelectedRecentProject = normalizedCurrentPath == null
            ? null
            : RecentProjects.FirstOrDefault(project =>
                string.Equals(project.FilePath, normalizedCurrentPath, StringComparison.OrdinalIgnoreCase));
    }

    private double ConvertCellDurationToDisplay(double value) =>
        TryGetSmallerUnit(TimeUnit, out _, out var scale)
            ? value * scale
            : value;

    private double ConvertDisplayToCellDuration(double value) =>
        TryGetSmallerUnit(TimeUnit, out _, out var scale)
            ? value / scale
            : value;

    private string GetCellDurationUnitLabel() =>
        TryGetSmallerUnit(TimeUnit, out var smallerUnit, out _)
            ? smallerUnit
            : TimeUnit;

    private static double NormalizeCellDuration(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return 1.0;

        return Math.Max(value, 0.0001);
    }

    private double GetSecondsPerUnit() => TimeUnit switch
    {
        "秒" => 1,
        "分" => 60,
        "時間" => 3600,
        "日" => 86400,
        "週" => 604800,
        "スプリント" => 1209600,
        _ => 1,
    };

    private static bool TryGetSmallerUnit(string timeUnit, out string smallerUnit, out double scale)
    {
        switch (timeUnit)
        {
            case "分":
                smallerUnit = "秒";
                scale = 60;
                return true;
            case "時間":
                smallerUnit = "分";
                scale = 60;
                return true;
            case "日":
                smallerUnit = "時間";
                scale = 24;
                return true;
            case "週":
                smallerUnit = "日";
                scale = 7;
                return true;
            default:
                smallerUnit = "";
                scale = 0;
                return false;
        }
    }

    private static string FormatNumber(double value) => value.ToString("0.####");
}

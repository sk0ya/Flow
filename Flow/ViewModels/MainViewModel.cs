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

public enum SidebarPanel { ProjectList, ProjectSettings, CanvasTools, TaskEditor, AppSettings }

public partial class MainViewModel : ObservableObject
{
    private readonly DependencyService _dep;
    private readonly StorageService    _stor;
    private readonly AppStateService   _appStateService;
    private readonly ProjectDraftService _draftService;
    private readonly ProjectExportService _exportService;
    private readonly AppState          _appState;
    private readonly DispatcherTimer   _autoSaveTimer;
    private readonly DispatcherTimer   _cellDurationTimer;
    private string _cellDurationText = "";
    private bool _suspendAutoSave;
    private bool _initializingAppearance;
    private bool _isLoadingProject;

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

    [ObservableProperty] private int    _cursorLaneIndex = 0;
    [ObservableProperty] private double _cursorTime      = 0.0;
    [ObservableProperty] private bool   _isVisualMode      = false;
    [ObservableProperty] private bool   _isVisualLineMode  = false;
    [ObservableProperty] private int    _visualAnchorLane  = -1;
    [ObservableProperty] private double _visualAnchorTime  = double.NaN;
    [ObservableProperty] private string _visualModeLabel   = "";
    [ObservableProperty] private string _vimModeLabel      = "NORMAL";
    [ObservableProperty] private bool   _isVimPromptActive = false;
    [ObservableProperty] private string _vimPromptText     = "";

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
    [ObservableProperty] private bool           _isDirty;
    [ObservableProperty] private string         _searchText = "";
    [ObservableProperty] private string         _searchHighlightText = "";
    [ObservableProperty] private CategoryViewModel? _selectedFilterCategory = CategoryViewModel.None;
    [ObservableProperty] private bool           _showErrorsOnly;
    [ObservableProperty] private double         _zoomPercent = 100;
    [ObservableProperty] private string         _statusMessage = "";
    [ObservableProperty] private double         _criticalPathDuration;
    [ObservableProperty] private bool           _hasDraftRecovery;

    [ObservableProperty] private string _newCategoryName = "";

    // Undo delete
    [ObservableProperty] private bool   _canUndoDelete;
    [ObservableProperty] private string _undoMessage = "";
    private ItemViewModel? _deletedItem;
    private bool _suppressCursorSnap;

    public UndoRedoManager UndoRedo { get; } = new();

    [ObservableProperty] private string? _cellDurationError;

    public ObservableCollection<CategoryViewModel>  Categories      { get; } = new();
    public ObservableCollection<LaneViewModel>      Lanes           { get; } = new();
    public ObservableCollection<ItemViewModel>      Items           { get; } = new();
    public ObservableCollection<ValidationError>    Errors          { get; } = new();
    public ObservableCollection<DependencyEdge>     DependencyEdges { get; } = new();
    public ObservableCollection<ItemViewModel>      FilteredItems   { get; } = new();
    public ObservableCollection<DependencyEdge>     FilteredDependencyEdges { get; } = new();
    public ObservableCollection<Guid>               CriticalPathItemIds { get; } = new();
    public ObservableCollection<RecentProjectEntry> RecentProjects  { get; } = new();
    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new();
    public ObservableCollection<AccentColorOption> AccentColorOptions { get; } = new();

    public IEnumerable<CategoryViewModel> CategoriesForPicker =>
        Enumerable.Repeat(CategoryViewModel.None, 1).Concat(Categories);
    public IEnumerable<CategoryViewModel> CategoriesForFilter =>
        Enumerable.Repeat(CategoryViewModel.None, 1).Concat(Categories);

    public static readonly string[] TimeUnitOptions = { "秒", "分", "時間", "日", "週", "スプリント" };

    public bool HasRecentProjects => RecentProjects.Count > 0;
    public bool HasActiveFilters => ShowErrorsOnly
                                 || SelectedFilterCategory is { IsNone: false };
    public double ZoomScale => Math.Clamp(ZoomPercent / 100.0, 0.3, 4.0);
    public double PixelsPerUnit => 80 * ZoomScale;
    public bool CanZoomIn => ZoomPercent < 300;
    public bool CanZoomOut => ZoomPercent > 40;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public int CursorLaneNumber => CursorLaneIndex + 1;
    public string SaveStateLabel => CurrentFilePath == null
        ? (IsDirty ? "未保存のドラフトあり" : "未保存")
        : (IsDirty ? "変更あり" : "保存済み");
    public string FilterSummary =>
        FilteredItems.Count == Items.Count
            ? $"表示 {FilteredItems.Count} / {Items.Count}"
            : $"絞り込み {FilteredItems.Count} / {Items.Count}";

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
    private bool _isSidebarOpen = true;

    public bool IsSidebarOpen
    {
        get => _isSidebarOpen;
        private set { if (_isSidebarOpen == value) return; _isSidebarOpen = value; OnPropertyChanged(); }
    }

    public bool IsProjectListActive    => _activeSidebarPanel == SidebarPanel.ProjectList;
    public bool IsProjectSettingsActive => _activeSidebarPanel == SidebarPanel.ProjectSettings;
    public bool IsCanvasToolsActive    => _activeSidebarPanel == SidebarPanel.CanvasTools;
    public bool IsTaskEditorActive     => _activeSidebarPanel == SidebarPanel.TaskEditor;
    public bool IsAppSettingsActive    => _activeSidebarPanel == SidebarPanel.AppSettings;

    public void ToggleOrActivatePanel(SidebarPanel panel)
    {
        if (_activeSidebarPanel == panel)
        {
            IsSidebarOpen = !_isSidebarOpen;
        }
        else
        {
            _activeSidebarPanel = panel;
            IsSidebarOpen = true;
            if (panel == SidebarPanel.ProjectList) SyncSelectedRecentProject();
            NotifyPanelProperties();
        }
    }

    public void ActivatePanel(SidebarPanel panel) => SetActivePanel(panel);

    private void SetActivePanel(SidebarPanel panel)
    {
        _activeSidebarPanel = panel;
        IsSidebarOpen = true;
        if (panel == SidebarPanel.ProjectList)
            SyncSelectedRecentProject();
        NotifyPanelProperties();
    }

    private void NotifyPanelProperties()
    {
        NotifyProperties(
            nameof(IsProjectListActive),
            nameof(IsProjectSettingsActive),
            nameof(IsCanvasToolsActive),
            nameof(IsTaskEditorActive),
            nameof(IsAppSettingsActive));
    }

    private void NotifyProperties(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            OnPropertyChanged(propertyName);
    }

    public MainViewModel(
        string? startupProjectPath = null,
        DependencyService? dependencyService = null,
        StorageService? storageService = null,
        AppStateService? appStateService = null,
        ProjectDraftService? draftService = null,
        ProjectExportService? exportService = null)
    {
        _dep = dependencyService ?? new DependencyService();
        _stor = storageService ?? new StorageService();
        _appStateService = appStateService ?? new AppStateService();
        _draftService = draftService ?? new ProjectDraftService();
        _exportService = exportService ?? new ProjectExportService();
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
        RefreshFilteredView();
        UpdateStatusMessage();
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
        GetConditionSuggestions(forId, filter, item => item.PostConditions);

    public List<SuggestionItem> GetPostSuggestions(Guid forId, string filter) =>
        GetConditionSuggestions(forId, filter, item => item.PreConditions);

    private List<SuggestionItem> GetConditionSuggestions(
        Guid forId,
        string filter,
        Func<ItemViewModel, IEnumerable<ConditionEntry>> conditionsSelector) =>
        Items.Where(item => item.Id != forId)
             .SelectMany(item => conditionsSelector(item).Select(entry => new SuggestionItem(entry.Value, item.Name)))
             .GroupBy(suggestion => suggestion.Value, StringComparer.OrdinalIgnoreCase)
             .Select(group => group.First())
             .Where(suggestion => string.IsNullOrEmpty(filter) || suggestion.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
             .OrderBy(suggestion => suggestion.Value)
             .Take(8)
             .ToList();

    // ── Category color palette ────────────────────────────────────────────

    private static readonly string[] CategoryColorPalette =
    {
        "#60A5FA", "#34D399", "#FBBF24", "#A78BFA",
        "#F472B6", "#38BDF8", "#FB923C", "#F87171",
    };

    private string NextCategoryColor() =>
        CategoryColorPalette[Categories.Count % CategoryColorPalette.Length];

    private void NotifyCategoryPickerChanged() =>
        OnPropertyChanged(nameof(CategoriesForPicker));

    private void NotifyCategoryViewsChanged() =>
        NotifyProperties(nameof(CategoriesForPicker), nameof(CategoriesForFilter));

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
        NotifyCategoryPickerChanged();
    }

    [RelayCommand]
    private void DeleteCategory(CategoryViewModel? cat)
    {
        if (cat == null) return;
        foreach (var item in Items.Where(i => i.CategoryId == cat.Id))
            item.CategoryId = Guid.Empty;
        Categories.Remove(cat);
        NotifyCategoryPickerChanged();
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

    public LaneViewModel InsertLaneAfter(int index)
    {
        var lane = new LaneViewModel(NextLaneName());
        Lanes.Insert(Math.Clamp(index + 1, 0, Lanes.Count), lane);
        return lane;
    }

    public LaneViewModel InsertLaneAt(int index, string? name = null)
    {
        var lane = new LaneViewModel(string.IsNullOrWhiteSpace(name) ? NextLaneName() : name);
        Lanes.Insert(Math.Clamp(index, 0, Lanes.Count), lane);
        return lane;
    }

    public void DeleteLaneWithItems(LaneViewModel lane)
    {
        // Remove all tasks that belong to this lane
        foreach (var item in Items.Where(i => i.LaneId == lane.Id).ToList())
        {
            if (SelectedItem?.Id == item.Id) SelectedItem = null;
            Items.Remove(item);
        }

        if (Lanes.Count <= 1)
        {
            // Last lane: clear tasks but keep the lane itself
            Analyze();
            return;
        }

        Lanes.Remove(lane);
        Analyze();
    }

    // ── Paste helpers (called from Vim keybindings) ───────────────────────

    public ItemViewModel PasteItem(SequenceItem template, Guid laneId, double startTime)
    {
        var model = new SequenceItem
        {
            Id             = Guid.NewGuid(),
            Name           = template.Name,
            Description    = template.Description,
            CategoryId     = template.CategoryId,
            Duration       = template.Duration,
            LaneId         = laneId,
            StartTime      = FindAvailableStartAtOrAfter(laneId, startTime, template.Duration),
            PreConditions  = new List<string>(template.PreConditions),
            PostConditions = new List<string>(template.PostConditions),
        };
        var vm = new ItemViewModel(model);
        Subscribe(vm);
        Items.Add(vm);
        SelectedItem = vm;
        return vm;
    }

    public (LaneViewModel lane, List<ItemViewModel> items) PasteLane(
        Lane laneTemplate, List<SequenceItem> itemTemplates, int afterIndex)
    {
        var lane = new LaneViewModel(laneTemplate.Name);
        Lanes.Insert(Math.Clamp(afterIndex + 1, 0, Lanes.Count), lane);

        var added = new List<ItemViewModel>();
        foreach (var t in itemTemplates.OrderBy(i => i.StartTime))
        {
            var model = new SequenceItem
            {
                Id             = Guid.NewGuid(),
                Name           = t.Name,
                Description    = t.Description,
                CategoryId     = t.CategoryId,
                Duration       = t.Duration,
                LaneId         = lane.Id,
                StartTime      = t.StartTime,
                PreConditions  = new List<string>(t.PreConditions),
                PostConditions = new List<string>(t.PostConditions),
            };
            var vm = new ItemViewModel(model);
            Subscribe(vm);
            Items.Add(vm);
            added.Add(vm);
        }
        return (lane, added);
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
        var laneId = SelectedItem?.LaneId ?? Lanes.FirstOrDefault()?.Id ?? Guid.Empty;
        if (laneId == Guid.Empty) return;

        double startTime = SelectedItem != null
            ? SelectedItem.StartTime + SelectedItem.Duration
            : Items.Where(i => i.LaneId == laneId)
                   .Select(i => i.StartTime + i.Duration)
                   .DefaultIfEmpty(0)
                   .Max();

        var name = NewItemName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            AddNewItemAt(laneId, startTime);
            StartRenameRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        CreateItem(name, laneId, startTime);
        NewItemName = "";
    }

    public ItemViewModel? AddNewItemAt(
        Guid laneId,
        double proposedStartTime,
        bool activateTaskEditor = true,
        bool selectItem = true)
    {
        var targetLaneId = Lanes.Any(l => l.Id == laneId)
            ? laneId
            : Lanes.FirstOrDefault()?.Id ?? Guid.Empty;
        if (targetLaneId == Guid.Empty) return null;

        var item = CreateItem("新しいタスク", targetLaneId, proposedStartTime, selectItem);
        if (activateTaskEditor)
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

    public void Undo() => UndoRedo.Undo();
    public void Redo() => UndoRedo.Redo();

    [RelayCommand]
    private void UndoLastAction() => Undo();

    [RelayCommand]
    private void RedoLastAction() => Redo();

    [RelayCommand]
    private void ZoomIn()
    {
        if (!CanZoomIn) return;
        ZoomPercent = Math.Min(300, ZoomPercent + 25);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (!CanZoomOut) return;
        ZoomPercent = Math.Max(40, ZoomPercent - 25);
    }

    [RelayCommand]
    private void ResetZoom() => ZoomPercent = 100;

    [RelayCommand]
    private void ClearFilters()
    {
        SearchHighlightText = "";
        SelectedFilterCategory = CategoryViewModel.None;
        ShowErrorsOnly = false;
    }

    [RelayCommand]
    private void SelectNextMatch() => SelectSearchMatch(forward: true);

    [RelayCommand]
    private void SelectPreviousMatch() => SelectSearchMatch(forward: false);

    [RelayCommand]
    private void RestoreDraft()
    {
        var draft = _draftService.LoadDraft();
        if (draft == null)
            return;

        _draftService.DeleteDraft();
        ApplyProject(draft.Project, draft.SourceProjectPath, isDraftRestore: true);
        StatusMessage = $"ドラフトを復元しました（{draft.SavedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm} 保存）";
    }

    [RelayCommand]
    private void AutoArrangeByDependencies()
    {
        var orderedItems = TopologicallyOrderItems();
        if (orderedItems == null)
        {
            MessageBox.Show("循環依存があるため、自動整列を実行できません。", "Flow",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var commands = new List<IUndoableCommand>();
        var laneSchedules = Lanes.ToDictionary(
            lane => lane.Id,
            _ => new List<(double Start, double End, Guid ItemId)>());

        foreach (var item in orderedItems)
        {
            double originalStart = item.StartTime;
            double earliest = GetRequiredStartTime(item);
            var schedule = laneSchedules[item.LaneId];
            double arrangedStart = FindAvailableStartInSchedule(schedule, earliest, item.Duration, item.Id);
            schedule.Add((arrangedStart, arrangedStart + item.Duration, item.Id));
            schedule.Sort((left, right) => left.Start.CompareTo(right.Start));

            if (Math.Abs(arrangedStart - originalStart) < 1e-9)
                continue;

            item.StartTime = arrangedStart;
            commands.Add(new PropertyChangeCommand<double>(value => item.StartTime = value, originalStart, arrangedStart));
        }

        if (commands.Count == 0)
            return;

        UndoRedo.Push(new CompositeCommand(commands));
        Analyze();
        StatusMessage = "依存関係に沿って自動整列しました";
    }

    // ── File commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void NewProject()
    {
        if (!TryConfirmProjectSwitch("新しいプロジェクトを作成"))
            return;

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
            IsDirty         = false;
            NotifyCategoryPickerChanged();
            _draftService.DeleteDraft();
            SetActivePanel(SidebarPanel.ProjectSettings);
            RefreshFilteredView();
            UpdateStatusMessage();
        });
    }

    [RelayCommand]
    private void SaveProject()
    {
        SaveProjectInteractive();
    }

    [RelayCommand]
    private void SaveProjectAs()
    {
        SaveProjectAsInteractive();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = "csv",
            FileName = $"{ProjectName}.csv"
        };

        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, _exportService.ExportCsv(CaptureProject()));
        StatusMessage = $"CSVを書き出しました: {Path.GetFileName(dlg.FileName)}";
    }

    [RelayCommand]
    private void ExportMarkdown()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Markdown (*.md)|*.md",
            DefaultExt = "md",
            FileName = $"{ProjectName}.md"
        };

        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, _exportService.ExportMarkdown(CaptureProject()));
        StatusMessage = $"Markdownを書き出しました: {Path.GetFileName(dlg.FileName)}";
    }

    [RelayCommand]
    private void RequestExportPng()
    {
        ExportPngRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool SaveProjectInteractive()
    {
        if (CurrentFilePath == null)
            return SaveProjectAsInteractive();

        return TrySaveProject(CurrentFilePath);
    }

    public bool TrySaveProjectFromVim()
        => SaveProjectInteractive();

    private bool SaveProjectAsInteractive()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Flow Project (*.flow)|*.flow",
            DefaultExt = "flow",
            FileName = ProjectName
        };

        if (dlg.ShowDialog() != true) return false;

        var originalProjectName = ProjectName;
        bool saved = false;
        RunWithoutAutoSave(() =>
        {
            ProjectName = Path.GetFileNameWithoutExtension(dlg.FileName);
            saved = TrySaveProject(dlg.FileName);
            if (!saved)
                ProjectName = originalProjectName;
        });

        return saved;
    }

    [RelayCommand]
    private void OpenProject()
    {
        if (!TryConfirmProjectSwitch("別のプロジェクトを開く"))
            return;

        var dlg = new OpenFileDialog { Filter = "Flow Project (*.flow)|*.flow" };
        if (dlg.ShowDialog() != true) return;
        TryLoadProject(dlg.FileName, showErrorMessage: true);
    }

    [RelayCommand]
    private void OpenRecentProject(RecentProjectEntry? recentProject)
    {
        if (recentProject == null) return;
        if (!TryConfirmProjectSwitch("最近使ったプロジェクトを開く"))
            return;
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
            _stor.Save(CaptureProject(), normalizedPath);

            CurrentFilePath = normalizedPath;
            _draftService.DeleteDraft();
            IsDirty = false;
            RememberProject(normalizedPath);
            UpdateStatusMessage();
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

    public event EventHandler? ProjectLoaded;
    public event EventHandler? StartRenameRequested;
    public event EventHandler? ExportPngRequested;

    private void ApplyProject(SequenceProject project, string? filePath, bool isDraftRestore = false)
    {
        RunWithoutAutoSave(() =>
        {
            _isLoadingProject = true;
            try
            {
                Items.Clear();
                Lanes.Clear();
                Categories.Clear();

                ProjectName = string.IsNullOrWhiteSpace(project.Name)
                    ? (filePath != null ? Path.GetFileNameWithoutExtension(filePath) : "復元されたドラフト")
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

                NotifyCategoryViewsChanged();

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
                IsDirty = isDraftRestore;
                HasDraftRecovery = _draftService.HasDraft();
                SetActivePanel(SidebarPanel.ProjectList);
                Analyze();
                RefreshFilteredView();
                UpdateStatusMessage();
            }
            finally
            {
                _isLoadingProject = false;
            }
        });
        ProjectLoaded?.Invoke(this, EventArgs.Empty);
    }

    private void TryRestoreStartupProject(string? startupProjectPath)
    {
        if (!string.IsNullOrWhiteSpace(startupProjectPath))
        {
            TryLoadProject(startupProjectPath, showErrorMessage: true);
            return;
        }

        HasDraftRecovery = _draftService.HasDraft();
        if (HasDraftRecovery)
        {
            var draft = _draftService.LoadDraft();
            string draftDetail = draft == null
                ? ""
                : $"\n保存日時: {draft.SavedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm}";
            var restore = MessageBox.Show(
                $"前回の未保存ドラフトが見つかりました。復元しますか？{draftDetail}",
                "Flow",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (restore == MessageBoxResult.Yes)
            {
                RestoreDraft();
                return;
            }

            _draftService.DeleteDraft();
            HasDraftRecovery = false;
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
        NotifyCategoryViewsChanged();
        Analyze();
        RequestAutoSave();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Analyze();
        RequestAutoSave();
        RefreshFilteredView();
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
        RefreshFilteredView();
    }

    private void Subscribe(CategoryViewModel vm)
    {
        vm.PropertyChanged += (_, _) =>
        {
            Analyze();
            RequestAutoSave();
            RefreshFilteredView();
        };
    }

    private void Subscribe(LaneViewModel vm)
    {
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LaneViewModel.Name))
            {
                RequestAutoSave();
                RefreshFilteredView();
            }
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
                RefreshFilteredView();
            }
        };
    }

    private ItemViewModel CreateItem(string name, Guid laneId, double proposedStartTime, bool selectItem = true)
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
        if (selectItem)
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

    private double FindAvailableStartInSchedule(
        List<(double Start, double End, Guid ItemId)> schedule,
        double proposedStartTime,
        double duration,
        Guid itemId)
    {
        double start = Math.Max(0, proposedStartTime);
        foreach (var slot in schedule.Where(slot => slot.ItemId != itemId).OrderBy(slot => slot.Start))
        {
            if (start + duration <= slot.Start + 1e-9)
                break;

            if (start < slot.End - 1e-9)
                start = slot.End;
        }

        return NormalizeTimelineValue(start);
    }

    private List<ItemViewModel>? TopologicallyOrderItems()
    {
        var producers = new Dictionary<string, List<ItemViewModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            foreach (var condition in item.PostConditions)
            {
                var key = condition.Value.Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!producers.TryGetValue(key, out var list))
                    producers[key] = list = new List<ItemViewModel>();
                list.Add(item);
            }
        }

        var incoming = Items.ToDictionary(item => item.Id, _ => 0);
        var edges = new Dictionary<Guid, List<Guid>>();

        foreach (var item in Items)
        {
            foreach (var condition in item.PreConditions)
            {
                var key = condition.Value.Trim();
                if (string.IsNullOrWhiteSpace(key) || !producers.TryGetValue(key, out var providers))
                    continue;

                foreach (var provider in providers.Where(provider => provider.Id != item.Id))
                {
                    if (!edges.TryGetValue(provider.Id, out var list))
                        edges[provider.Id] = list = new List<Guid>();

                    if (list.Contains(item.Id))
                        continue;

                    list.Add(item.Id);
                    incoming[item.Id]++;
                }
            }
        }

        var queue = new Queue<ItemViewModel>(Items
            .Where(item => incoming[item.Id] == 0)
            .OrderBy(item => item.StartTime)
            .ThenBy(item => GetLaneIndex(item.LaneId)));

        var ordered = new List<ItemViewModel>();
        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            ordered.Add(item);

            if (!edges.TryGetValue(item.Id, out var nextIds))
                continue;

            foreach (var nextId in nextIds)
            {
                incoming[nextId]--;
                if (incoming[nextId] == 0)
                {
                    var next = Items.First(candidate => candidate.Id == nextId);
                    queue.Enqueue(next);
                }
            }
        }

        return ordered.Count == Items.Count ? ordered : null;
    }

    private double GetRequiredStartTime(ItemViewModel item)
    {
        var producerEnds = Items
            .Where(candidate => candidate.Id != item.Id)
            .SelectMany(candidate => candidate.PostConditions
                .Where(post => item.PreConditions.Any(pre =>
                    string.Equals(pre.Value.Trim(), post.Value.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Select(_ => candidate.StartTime + candidate.Duration))
            .DefaultIfEmpty(0);

        return NormalizeTimelineValue(producerEnds.Max());
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
        CriticalPathDuration = result.CriticalPathDuration;

        DependencyEdges.Clear();
        foreach (var edge in result.Edges) DependencyEdges.Add(edge);

        CriticalPathItemIds.Clear();
        foreach (var itemId in result.CriticalPathItemIds)
            CriticalPathItemIds.Add(itemId);

        RefreshFilteredView();
        UpdateStatusMessage();
    }

    // ── Partial property change handlers ─────────────────────────────────

    partial void OnCursorLaneIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CursorLaneNumber));
    }

    partial void OnTimeUnitChanged(string value)
    {
        CellDuration = NormalizeCellDuration(CellDuration);
        SyncCellDurationText();
        NotifyCellDurationDisplayProperties();
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
        NotifyCellDurationDisplayProperties();
        RequestAutoSave();
    }

    private void NotifyCellDurationDisplayProperties() =>
        NotifyProperties(
            nameof(CellDurationValue),
            nameof(CellDurationUnitLabel),
            nameof(CellDurationSummary));

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

    partial void OnSearchTextChanged(string value)
    {
        SearchHighlightText = value;
        UpdateStatusMessage();
    }

    partial void OnSelectedFilterCategoryChanged(CategoryViewModel? value)
    {
        RefreshFilteredView();
        UpdateStatusMessage();
    }

    partial void OnShowErrorsOnlyChanged(bool value)
    {
        RefreshFilteredView();
        UpdateStatusMessage();
    }

    partial void OnZoomPercentChanged(double value)
    {
        double normalized = Math.Clamp(Math.Round(value / 5.0) * 5.0, 40, 300);
        if (Math.Abs(normalized - value) > 0.0001)
        {
            ZoomPercent = normalized;
            return;
        }

        NotifyZoomProperties();
    }

    private void NotifyZoomProperties() =>
        NotifyProperties(
            nameof(ZoomScale),
            nameof(PixelsPerUnit),
            nameof(CanZoomIn),
            nameof(CanZoomOut));

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(SaveStateLabel));
        UpdateStatusMessage();
    }

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
        OnPropertyChanged(nameof(SaveStateLabel));
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
        if (_suspendAutoSave || _isLoadingProject)
            return;

        IsDirty = true;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void FlushAutoSave()
    {
        _autoSaveTimer.Stop();

        if (_suspendAutoSave || _isLoadingProject || !IsDirty)
            return;

        if (!string.IsNullOrWhiteSpace(CurrentFilePath))
        {
            TrySaveProject(CurrentFilePath, showErrorMessage: false);
            return;
        }

        _draftService.SaveDraft(CaptureProject(), CurrentFilePath);
        HasDraftRecovery = true;
        UpdateStatusMessage();
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

    private SequenceProject CaptureProject() => new()
    {
        Name = ProjectName,
        TimeUnit = TimeUnit,
        CellDuration = CellDuration,
        TotalDuration = TotalDuration,
        Categories = Categories.Select(c => c.ToModel()).ToList(),
        Lanes = Lanes.Select(l => l.ToModel()).ToList(),
        Items = Items.Select(v => v.ToModel()).ToList(),
    };

    private bool TryConfirmProjectSwitch(string actionLabel)
    {
        if (!IsDirty)
            return true;

        var result = MessageBox.Show(
            $"{actionLabel}前に現在の変更を保存しますか？",
            "Flow",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
            return false;

        if (result == MessageBoxResult.Yes)
            return SaveProjectInteractive();

        _draftService.DeleteDraft();
        HasDraftRecovery = false;
        return true;
    }

    public bool CanCloseWindow()
    {
        if (!TryConfirmProjectSwitch("アプリを終了する"))
            return false;

        return true;
    }

    private void RefreshFilteredView()
    {
        var visibleIds = new HashSet<Guid>();
        var filteredItems = Items.Where(MatchesFilters)
            .OrderBy(item => GetLaneIndex(item.LaneId))
            .ThenBy(item => item.StartTime)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FilteredItems.Clear();
        foreach (var item in filteredItems)
        {
            FilteredItems.Add(item);
            visibleIds.Add(item.Id);
        }

        FilteredDependencyEdges.Clear();
        foreach (var edge in DependencyEdges.Where(edge =>
                     visibleIds.Contains(edge.FromId) && visibleIds.Contains(edge.ToId)))
        {
            FilteredDependencyEdges.Add(edge);
        }

        OnPropertyChanged(nameof(FilterSummary));
        UpdateStatusMessage();
    }

    private bool MatchesFilters(ItemViewModel item)
    {
        if (ShowErrorsOnly && !item.HasErrors)
            return false;

        if (SelectedFilterCategory is { IsNone: false } filterCategory && item.CategoryId != filterCategory.Id)
            return false;

        return true;
    }

    private bool MatchesSearch(ItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return false;

        string search = SearchText.Trim();
        var laneName = Lanes.FirstOrDefault(lane => lane.Id == item.LaneId)?.Name ?? "";
        var categoryName = Categories.FirstOrDefault(category => category.Id == item.CategoryId)?.Name ?? "";

        return item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
            || laneName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || categoryName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.PreConditions.Any(condition => condition.Value.Contains(search, StringComparison.OrdinalIgnoreCase))
            || item.PostConditions.Any(condition => condition.Value.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectSearchMatch(bool forward)
    {
        var pool = FilteredItems
            .Where(MatchesSearch)
            .ToList();

        if (pool.Count == 0)
            return;

        int currentIndex = SelectedItem == null ? -1 : pool.FindIndex(item => item.Id == SelectedItem.Id);
        int nextIndex = forward
            ? (currentIndex + 1 + pool.Count) % pool.Count
            : (currentIndex - 1 + pool.Count) % pool.Count;

        SelectedItem = pool[nextIndex];
        SetActivePanel(SidebarPanel.TaskEditor);
    }

    private int GetLaneIndex(Guid laneId)
    {
        for (int index = 0; index < Lanes.Count; index++)
        {
            if (Lanes[index].Id == laneId)
                return index;
        }

        return int.MaxValue;
    }

    private void UpdateStatusMessage()
    {
        if (HasDraftRecovery && !IsDirty && CurrentFilePath == null)
        {
            StatusMessage = "前回のドラフトを復元できます";
            return;
        }

        if (HasActiveFilters)
        {
            StatusMessage = FilterSummary;
            return;
        }

        StatusMessage = SaveStateLabel;
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

    public double GridStepInSeconds => Math.Max(CellDuration, 0.0001) * GetSecondsPerUnit();

    public void SetSelectionFromVim(ItemViewModel? item)
    {
        _suppressCursorSnap = true;
        SelectedItem = item;
        _suppressCursorSnap = false;
    }

    partial void OnSelectedItemChanged(ItemViewModel? value)
    {
        if (_suppressCursorSnap || value == null) return;
        for (int i = 0; i < Lanes.Count; i++)
        {
            if (Lanes[i].Id != value.LaneId) continue;
            CursorLaneIndex = i;
            CursorTime      = value.StartTime;
            return;
        }
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

    private static double NormalizeTimelineValue(double value) =>
        Math.Round(value, 10, MidpointRounding.AwayFromZero);
}

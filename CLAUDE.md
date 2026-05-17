# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```powershell
# Build
dotnet build Flow/Flow.csproj

# Run
Start-Process "Flow/bin/Debug/net9.0-windows/Flow.exe"

# Build + Run in one step
dotnet build Flow/Flow.csproj && Start-Process "Flow/bin/Debug/net9.0-windows/Flow.exe"
```

No test project exists yet. The app must be verified manually.

## Architecture

**Stack:** .NET 9, WPF, C#, CommunityToolkit.Mvvm 8.3.2, `System.Text.Json` for storage.

**Pattern:** MVVM. `MainViewModel` is the single root VM; `DataContext` is set in `MainWindow.xaml.cs` constructor.

### Data flow

```
User action (drag / input)
  → ItemViewModel / MainViewModel property change
  → MainViewModel.Analyze()          ← always re-runs on any item change
      → DependencyService.Analyze()  ← pure, works on List<SequenceItem> (models)
      → updates HasErrors, ErrorMessage on each ItemViewModel
      → updates DependencyEdges, Errors collections
  → GanttCanvas.Render()             ← triggered by collection change on Edges/Items/Lanes
```

### Key files

| File | Role |
|---|---|
| `Services/DependencyService.cs` | Pure analysis: condition string matching → edges, time-violation errors. No WPF deps. |
| `ViewModels/MainViewModel.cs` | All commands, lane/item CRUD, `Analyze()`, collision resolution (`PlaceWithoutOverlap`), undo-delete. |
| `Views/Controls/GanttCanvas.xaml.cs` | Entire Gantt rendering + drag logic. All drawing uses WPF `UIElement`s added to a `Canvas`. |
| `MainWindow.xaml` | All styles/templates defined as `Window.Resources`. Autocomplete popup managed in `MainWindow.xaml.cs`. |

### Models vs ViewModels

- `Models/` — plain POCOs, serialized to `.flow` (JSON). `SequenceItem` holds `StartTime`, `Duration`, `LaneId`.
- `ViewModels/` — observable wrappers. `ItemViewModel.PreConditions` / `PostConditions` are `ObservableCollection<ConditionEntry>` (not `string`) so each chip can carry `IsLinked` / `LinkDetail` status set by `Analyze()`.

### GanttCanvas rendering

`Render()` clears and recreates all children on every call (retained-mode style but fully immediate). Order matters for Z-depth: backgrounds → grid lines → arrows → bars → drag ghost.

**Coordinate system:**
- `X = LaneHeaderW + item.StartTime * PixelsPerUnit`
- `Y = TimeHeaderH + laneIndex * LaneH + (LaneH - BarH) / 2`

**Drag uses `PreviewMouseLeftButtonDown`** (not `MouseLeftButtonDown`) so it fires before child element handlers that would otherwise swallow the event.

Collision avoidance runs on every `MouseMove` during drag via `FindValidStart()` / `FindValidDuration()`, which compute free gaps in the target lane and snap the proposed position to the nearest valid slot.

### Condition system

Pre-condition on item B matches post-condition on item A by **case-insensitive string equality** (trimmed). A match creates a `DependencyEdge` and is visualised as an arrow. If `A.StartTime + A.Duration > B.StartTime`, a `TimeViolation` error is raised on B. Condition chips show `IsLinked=true` (blue) or `false` (red) set during `Analyze()`.

### Storage

`StorageService` serializes `SequenceProject` (including `Lanes`) to indented JSON with `.flow` extension. `SequenceProject` is the root; it owns `List<Lane>` and `List<SequenceItem>`.

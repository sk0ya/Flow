# View Settings Redesign Plan

## Problem

Flow currently treats a project as one fixed lane board. Each task stores a single `LaneId`, so the lane structure is part of the project data itself.

That works for a simple Gantt board, but it breaks down when the user wants to inspect the same project from different angles. A task may need to be seen by assignee, team, phase, place, equipment, priority, or workflow state. Those are not different project types; they are different views over the same tasks.

The removed "lane meaning" setting was not useful because it only labeled the existing lanes. It did not change task data, grouping, editing, analysis, or export behavior.

## Product Direction

Treat lanes as a view concern, not as a project-level meaning.

The project should own tasks and their attributes. A view decides which task attribute becomes the lane grouping.

```text
Project
  Tasks
    Name
    StartTime
    Duration
    CategoryId
    Assignee
    Team
    Phase
    Location
    Status
    Dependencies

  Views
    Default lane board
    By assignee
    By team
    By phase
    By location/equipment
```

## Target Behavior

- Users can switch the board view without duplicating tasks.
- A task can appear under a different lane depending on the selected view.
- Existing `.flow` files continue to open with their current explicit lanes.
- The current board remains the default view until task attributes and view grouping are introduced.
- Project presets create useful starter tasks metadata and starter views, not a permanent lane meaning.

## Proposed Model

Add a view model to saved project data:

```csharp
public enum ProjectViewGroupBy
{
    ExplicitLane,
    Assignee,
    Team,
    Phase,
    Location,
    Status,
    Category,
}

public sealed class ProjectView
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public ProjectViewGroupBy GroupBy { get; set; } = ProjectViewGroupBy.ExplicitLane;
    public List<string> LaneOrder { get; set; } = new();
}
```

Extend `SequenceItem` gradually:

```csharp
public string Assignee { get; set; } = "";
public string Team { get; set; } = "";
public string Phase { get; set; } = "";
public string Location { get; set; } = "";
public string Status { get; set; } = "";
```

Keep `LaneId` during migration. It remains the backing field for the existing explicit lane board.

## Migration

1. Existing projects load as a single `ExplicitLane` view.
2. Existing `Lanes` remain unchanged.
3. Existing `SequenceItem.LaneId` continues to work.
4. New view-based grouping is opt-in.
5. Only after view grouping is stable should we consider reducing reliance on `LaneId`.

## Implementation Phases

### Phase 1: Remove Lane Meaning

Status: done in current branch.

- Remove project-level lane meaning from model and UI.
- Keep project presets as starter lane/category templates only.
- Avoid implying that lane meaning changes behavior.

### Phase 2: Add Saved Views Without Changing Board Behavior

- Add `ProjectView` and `ProjectViewGroupBy`.
- Add `Views` and `ActiveViewId` to `SequenceProject`.
- On load, create a default explicit-lane view when missing.
- Add tests for round-trip save/load and backward compatibility.

### Phase 3: View Selector UI

- Add a compact view selector near canvas controls.
- Start with only `ExplicitLane`.
- Allow renaming views and creating a duplicate view.
- Do not add grouping behavior yet.

### Phase 4: Add Task Attributes

- Add optional task fields: assignee, team, phase, location, status.
- Add task editor controls for these fields.
- Include the fields in CSV/Markdown export.
- Keep them empty for existing projects.

### Phase 5: Group Board By Attribute

- Generate display lanes from the active view's `GroupBy`.
- For `ExplicitLane`, keep current behavior.
- For attribute-based views, lane identity is the attribute value.
- Tasks with empty values go to an "Unassigned" lane.
- Dragging a task to another generated lane updates the corresponding task attribute.

### Phase 6: Presets Create Views

- Software preset can create views like "By team", "By phase", and "By status".
- Event preset can create views like "By place" and "By owner".
- Manufacturing preset can create views like "By equipment" and "By process status".
- Presets should not overwrite user-created views unless the project has no tasks and no custom views.

## Open Decisions

- Whether `Phase` should be a free-text task field or backed by a managed list.
- Whether generated lanes should be editable directly, or edited through task attributes only.
- Whether dependency arrows should filter differently per view.
- How to handle lane ordering for generated lanes.
- Whether "Category" should remain separate from `Status` and `Phase`.

## Non-Goals

- Do not add more project-level settings that only change labels.
- Do not add validation modes until there is a concrete workflow that needs them.
- Do not remove `LaneId` until explicit-lane projects and Vim operations are safely migrated.

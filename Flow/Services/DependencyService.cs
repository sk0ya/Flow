using System;
using System.Collections.Generic;
using System.Linq;
using Flow.Models;

namespace Flow.Services;

public enum ValidationErrorType
{
    UnresolvedPrecondition,
    TimeViolation,
    AmbiguousProducers,
    CircularDependency,
}

public record ValidationError(ValidationErrorType Type, Guid ItemId, string Condition, string Message);
public record DependencyEdge(Guid FromId, Guid ToId, string Condition);

public sealed record DependencyRequirement(Guid ConsumerId, string Condition, IReadOnlyList<Guid> ProducerIds)
{
    public bool IsResolved => ProducerIds.Count > 0;
    public bool IsAmbiguous => ProducerIds.Count > 1;
}

public sealed record DependencyCycle(IReadOnlyList<Guid> ItemIds, IReadOnlyList<DependencyEdge> Edges);

public sealed record CriticalPathActivity(
    Guid ItemId,
    double EarliestStart,
    double EarliestFinish,
    double LatestStart,
    double LatestFinish,
    double TotalFloat,
    bool IsCritical);

public sealed class CriticalPathSummary
{
    public bool IsAvailable { get; init; }
    public double Duration { get; init; }
    public List<Guid> ItemIds { get; init; } = new();
    public List<CriticalPathActivity> Activities { get; init; } = new();
    public string? UnavailableReason { get; init; }
}

public class DependencyResult
{
    public List<DependencyEdge> Edges { get; init; } = new();
    public List<ValidationError> Errors { get; init; } = new();
    public List<DependencyRequirement> Requirements { get; init; } = new();
    public List<DependencyCycle> Cycles { get; init; } = new();
    public CriticalPathSummary CriticalPath { get; init; } = new() { IsAvailable = true };
    public double ProjectDuration { get; init; }
    public double CriticalPathDuration => CriticalPath.Duration;
    public IReadOnlyList<Guid> CriticalPathItemIds => CriticalPath.ItemIds;
}

public class DependencyService
{
    private const double TimeTolerance = 1e-9;

    public DependencyResult Analyze(List<SequenceItem> items)
    {
        if (items.Count == 0)
        {
            return new DependencyResult
            {
                CriticalPath = new CriticalPathSummary
                {
                    IsAvailable = true,
                    Duration = 0,
                },
            };
        }

        var itemById = items.ToDictionary(item => item.Id);
        var itemOrder = items.Select((item, index) => (item.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index);
        var producers = BuildProducerLookup(items);

        var errors = new List<ValidationError>();
        var edges = new HashSet<DependencyEdge>();
        var requirements = new List<DependencyRequirement>();

        foreach (var item in items)
        {
            foreach (var condition in NormalizeConditions(item.PreConditions))
            {
                var producerIds = producers.TryGetValue(condition, out var providers)
                    ? providers.Where(provider => provider.Id != item.Id).Select(provider => provider.Id).ToList()
                    : new List<Guid>();

                requirements.Add(new DependencyRequirement(item.Id, condition, producerIds));

                if (producerIds.Count == 0)
                {
                    errors.Add(new ValidationError(
                        ValidationErrorType.UnresolvedPrecondition,
                        item.Id,
                        condition,
                        $"未解決: 「{condition}」を提供するタスクがありません（{item.Name}）"));
                    continue;
                }

                if (producerIds.Count > 1)
                {
                    string producerNames = string.Join(", ", producerIds.Select(id => itemById[id].Name));
                    errors.Add(new ValidationError(
                        ValidationErrorType.AmbiguousProducers,
                        item.Id,
                        condition,
                        $"曖昧な依存: 「{condition}」を提供するタスクが複数あります（{producerNames} → {item.Name}）"));
                }

                foreach (var producerId in producerIds)
                {
                    var producer = itemById[producerId];
                    edges.Add(new DependencyEdge(producer.Id, item.Id, condition));

                    double producerEnd = producer.StartTime + producer.Duration;
                    if (producerEnd > item.StartTime + TimeTolerance)
                    {
                        errors.Add(new ValidationError(
                            ValidationErrorType.TimeViolation,
                            item.Id,
                            condition,
                            $"依存順序エラー: 「{producer.Name}」（終了 {producerEnd:0.####}）より前に「{item.Name}」（開始 {item.StartTime:0.####}）が始まっています"));
                    }
                }
            }
        }

        var orderedEdges = edges
            .OrderBy(edge => itemOrder[edge.FromId])
            .ThenBy(edge => itemOrder[edge.ToId])
            .ThenBy(edge => edge.Condition, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cycles = DetectCycles(items, orderedEdges, itemOrder);
        foreach (var cycle in cycles)
        {
            string cycleNames = string.Join(" -> ", cycle.ItemIds.Select(id => itemById[id].Name));
            string conditions = string.Join(", ", cycle.Edges
                .Select(edge => edge.Condition)
                .Distinct(StringComparer.OrdinalIgnoreCase));

            foreach (var itemId in cycle.ItemIds)
            {
                errors.Add(new ValidationError(
                    ValidationErrorType.CircularDependency,
                    itemId,
                    conditions,
                    $"循環依存: {cycleNames} の間で依存ループが検出されました"));
            }
        }

        double projectDuration = items.Max(item => item.StartTime + item.Duration);
        var criticalPath = BuildCriticalPath(items, orderedEdges, itemOrder, cycles.Count > 0);

        return new DependencyResult
        {
            Edges = orderedEdges,
            Errors = errors,
            Requirements = requirements,
            Cycles = cycles,
            CriticalPath = criticalPath,
            ProjectDuration = projectDuration,
        };
    }

    private static Dictionary<string, List<SequenceItem>> BuildProducerLookup(IEnumerable<SequenceItem> items)
    {
        var producers = new Dictionary<string, List<SequenceItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            foreach (var condition in NormalizeConditions(item.PostConditions))
            {
                if (!producers.TryGetValue(condition, out var list))
                {
                    list = new List<SequenceItem>();
                    producers[condition] = list;
                }

                list.Add(item);
            }
        }

        return producers;
    }

    private static List<string> NormalizeConditions(IEnumerable<string> conditions)
    {
        return conditions
            .Select(condition => condition.Trim())
            .Where(condition => !string.IsNullOrWhiteSpace(condition))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<DependencyCycle> DetectCycles(
        IReadOnlyList<SequenceItem> items,
        IReadOnlyList<DependencyEdge> edges,
        IReadOnlyDictionary<Guid, int> itemOrder)
    {
        var adjacency = items.ToDictionary(item => item.Id, _ => new List<Guid>());

        foreach (var pair in edges
                     .Select(edge => (edge.FromId, edge.ToId))
                     .Distinct()
                     .OrderBy(pair => itemOrder[pair.FromId])
                     .ThenBy(pair => itemOrder[pair.ToId]))
        {
            adjacency[pair.FromId].Add(pair.ToId);
        }

        var indexById = new Dictionary<Guid, int>();
        var lowLinkById = new Dictionary<Guid, int>();
        var stack = new Stack<Guid>();
        var onStack = new HashSet<Guid>();
        var components = new List<List<Guid>>();
        int index = 0;

        foreach (var item in items)
        {
            if (!indexById.ContainsKey(item.Id))
            {
                StrongConnect(item.Id);
            }
        }

        return components
            .Select(component =>
            {
                var componentIds = component.OrderBy(id => itemOrder[id]).ToList();
                var componentSet = componentIds.ToHashSet();
                var componentEdges = edges
                    .Where(edge => componentSet.Contains(edge.FromId) && componentSet.Contains(edge.ToId))
                    .ToList();

                return new DependencyCycle(componentIds, componentEdges);
            })
            .Where(cycle => cycle.Edges.Count > 0)
            .ToList();

        void StrongConnect(Guid itemId)
        {
            indexById[itemId] = index;
            lowLinkById[itemId] = index;
            index++;
            stack.Push(itemId);
            onStack.Add(itemId);

            foreach (var nextId in adjacency[itemId].OrderBy(id => itemOrder[id]))
            {
                if (!indexById.ContainsKey(nextId))
                {
                    StrongConnect(nextId);
                    lowLinkById[itemId] = Math.Min(lowLinkById[itemId], lowLinkById[nextId]);
                }
                else if (onStack.Contains(nextId))
                {
                    lowLinkById[itemId] = Math.Min(lowLinkById[itemId], indexById[nextId]);
                }
            }

            if (lowLinkById[itemId] != indexById[itemId])
            {
                return;
            }

            var component = new List<Guid>();
            Guid currentId;
            do
            {
                currentId = stack.Pop();
                onStack.Remove(currentId);
                component.Add(currentId);
            } while (currentId != itemId);

            if (component.Count > 1)
            {
                components.Add(component);
            }
        }
    }

    private static CriticalPathSummary BuildCriticalPath(
        IReadOnlyList<SequenceItem> items,
        IReadOnlyList<DependencyEdge> edges,
        IReadOnlyDictionary<Guid, int> itemOrder,
        bool hasCycles)
    {
        if (hasCycles)
        {
            return new CriticalPathSummary
            {
                IsAvailable = false,
                UnavailableReason = "Critical path is unavailable while circular dependencies exist.",
            };
        }

        var itemsById = items.ToDictionary(item => item.Id);
        var nodeEdges = edges
            .Select(edge => (edge.FromId, edge.ToId))
            .Distinct()
            .ToList();

        var successors = items.ToDictionary(item => item.Id, _ => new List<Guid>());
        var predecessors = items.ToDictionary(item => item.Id, _ => new List<Guid>());
        var indegree = items.ToDictionary(item => item.Id, _ => 0);

        foreach (var (fromId, toId) in nodeEdges)
        {
            successors[fromId].Add(toId);
            predecessors[toId].Add(fromId);
            indegree[toId]++;
        }

        var ready = indegree
            .Where(entry => entry.Value == 0)
            .Select(entry => entry.Key)
            .OrderBy(id => itemOrder[id])
            .ToList();
        var topologicalOrder = new List<Guid>(items.Count);

        while (ready.Count > 0)
        {
            var currentId = ready[0];
            ready.RemoveAt(0);
            topologicalOrder.Add(currentId);

            foreach (var nextId in successors[currentId].OrderBy(id => itemOrder[id]))
            {
                indegree[nextId]--;
                if (indegree[nextId] == 0)
                {
                    ready.Add(nextId);
                }
            }

            ready.Sort((left, right) => itemOrder[left].CompareTo(itemOrder[right]));
        }

        if (topologicalOrder.Count != items.Count)
        {
            return new CriticalPathSummary
            {
                IsAvailable = false,
                UnavailableReason = "Critical path could not be calculated because the dependency graph is not acyclic.",
            };
        }

        var earliestStart = items.ToDictionary(item => item.Id, _ => 0d);
        var earliestFinish = items.ToDictionary(item => item.Id, _ => 0d);
        var longestPredecessor = items.ToDictionary(item => item.Id, _ => (Guid?)null);

        foreach (var itemId in topologicalOrder)
        {
            earliestFinish[itemId] = earliestStart[itemId] + itemsById[itemId].Duration;

            foreach (var nextId in successors[itemId].OrderBy(id => itemOrder[id]))
            {
                double candidateStart = earliestFinish[itemId];
                var currentPredecessor = longestPredecessor[nextId];

                if (candidateStart > earliestStart[nextId] + TimeTolerance ||
                    (Math.Abs(candidateStart - earliestStart[nextId]) <= TimeTolerance &&
                     (currentPredecessor == null || itemOrder[itemId] < itemOrder[currentPredecessor.Value])))
                {
                    earliestStart[nextId] = candidateStart;
                    longestPredecessor[nextId] = itemId;
                }
            }
        }

        double duration = earliestFinish.Values.Max();
        Guid endItemId = topologicalOrder
            .Where(itemId => Math.Abs(earliestFinish[itemId] - duration) <= TimeTolerance)
            .OrderBy(itemId => itemOrder[itemId])
            .First();

        var latestFinish = items.ToDictionary(item => item.Id, _ => duration);
        var latestStart = items.ToDictionary(item => item.Id, _ => 0d);

        foreach (var itemId in topologicalOrder.AsEnumerable().Reverse())
        {
            if (successors[itemId].Count > 0)
            {
                latestFinish[itemId] = successors[itemId]
                    .Select(nextId => latestStart[nextId])
                    .Min();
            }

            latestStart[itemId] = latestFinish[itemId] - itemsById[itemId].Duration;
        }

        var activities = items
            .OrderBy(item => itemOrder[item.Id])
            .Select(item =>
            {
                double totalFloat = latestStart[item.Id] - earliestStart[item.Id];
                return new CriticalPathActivity(
                    item.Id,
                    earliestStart[item.Id],
                    earliestFinish[item.Id],
                    latestStart[item.Id],
                    latestFinish[item.Id],
                    totalFloat,
                    Math.Abs(totalFloat) <= TimeTolerance);
            })
            .ToList();

        var path = new List<Guid>();
        for (Guid? currentId = endItemId; currentId != null; currentId = longestPredecessor[currentId.Value])
        {
            path.Add(currentId.Value);
        }

        path.Reverse();

        return new CriticalPathSummary
        {
            IsAvailable = true,
            Duration = duration,
            ItemIds = path,
            Activities = activities,
        };
    }
}

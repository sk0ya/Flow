using System;
using System.Collections.Generic;
using System.Linq;
using Flow.Models;

namespace Flow.Services;

public enum ValidationErrorType
{
    UnresolvedPrecondition,
    TimeViolation,
}

public record ValidationError(ValidationErrorType Type, Guid ItemId, string Condition, string Message);
public record DependencyEdge(Guid FromId, Guid ToId, string Condition);

public class DependencyResult
{
    public List<DependencyEdge>  Edges           { get; init; } = new();
    public List<ValidationError> Errors          { get; init; } = new();
    public double                ProjectDuration { get; init; }
}

public class DependencyService
{
    public DependencyResult Analyze(List<SequenceItem> items)
    {
        if (items.Count == 0) return new DependencyResult();

        var errors = new List<ValidationError>();
        var edges  = new List<DependencyEdge>();

        // condition → items that produce it (PostConditions)
        var producers = new Dictionary<string, List<SequenceItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            foreach (var post in item.PostConditions)
            {
                var k = post.Trim();
                if (string.IsNullOrEmpty(k)) continue;
                if (!producers.ContainsKey(k)) producers[k] = new();
                producers[k].Add(item);
            }

        foreach (var item in items)
        {
            foreach (var pre in item.PreConditions)
            {
                var k = pre.Trim();
                if (string.IsNullOrEmpty(k)) continue;

                if (producers.TryGetValue(k, out var provList))
                {
                    foreach (var prov in provList)
                    {
                        if (prov.Id == item.Id) continue;
                        if (!edges.Any(e => e.FromId == prov.Id && e.ToId == item.Id && e.Condition == k))
                            edges.Add(new DependencyEdge(prov.Id, item.Id, k));

                        double provEnd = prov.StartTime + prov.Duration;
                        if (provEnd > item.StartTime + 1e-9)
                            errors.Add(new ValidationError(
                                ValidationErrorType.TimeViolation, item.Id, k,
                                $"依存順序エラー: 「{prov.Name}」（終了 {provEnd:0.####}）より前に「{item.Name}」（開始 {item.StartTime:0.####}）が始まっています"));
                    }
                }
                else
                {
                    errors.Add(new ValidationError(
                        ValidationErrorType.UnresolvedPrecondition, item.Id, k,
                        $"未解決: 「{k}」を提供するタスクがありません（{item.Name}）"));
                }
            }
        }

        double projectDuration = items.Max(i => i.StartTime + i.Duration);
        return new DependencyResult { Edges = edges, Errors = errors, ProjectDuration = projectDuration };
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Flow.Models;

namespace Flow.Services;

public enum ProjectExportFormat
{
    Csv,
    Markdown,
}

public sealed record ProjectExportResult(
    ProjectExportFormat Format,
    string Content,
    string FileExtension,
    string ContentType);

public class ProjectExportService
{
    public ProjectExportResult Export(SequenceProject project, ProjectExportFormat format) => format switch
    {
        ProjectExportFormat.Csv => new(ProjectExportFormat.Csv, ExportCsv(project), ".csv", "text/csv"),
        ProjectExportFormat.Markdown => new(ProjectExportFormat.Markdown, ExportMarkdown(project), ".md", "text/markdown"),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public string ExportCsv(SequenceProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var builder = new StringBuilder();
        builder.AppendLine("Project,Time Unit,Cell Duration,Total Duration,Lane,Task,Start,Duration,End,Category,Preconditions,Postconditions,Description");

        foreach (var row in EnumerateRows(project))
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Escape(project.Name),
                Escape(project.TimeUnit),
                Escape(FormatNumber(project.CellDuration)),
                Escape(FormatNumber(project.TotalDuration)),
                Escape(row.LaneName),
                Escape(row.Item.Name),
                Escape(FormatNumber(row.Item.StartTime)),
                Escape(FormatNumber(row.Item.Duration)),
                Escape(FormatNumber(row.Item.StartTime + row.Item.Duration)),
                Escape(row.CategoryName),
                Escape(JoinOrNone(row.Item.PreConditions)),
                Escape(JoinOrNone(row.Item.PostConditions)),
                Escape(row.Item.Description ?? string.Empty),
            }));
        }

        return builder.ToString();
    }

    public string ExportMarkdown(SequenceProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var builder = new StringBuilder();
        var rows = EnumerateRows(project).ToList();
        var laneLookup = project.Lanes.ToDictionary(lane => lane.Id, lane => lane);
        var groupedByLane = rows
            .Where(row => laneLookup.ContainsKey(row.Item.LaneId))
            .GroupBy(row => row.Item.LaneId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var unassigned = rows
            .Where(row => !laneLookup.ContainsKey(row.Item.LaneId))
            .ToList();

        builder.AppendLine($"# {project.Name}");
        builder.AppendLine();
        builder.AppendLine($"- 時間単位: {project.TimeUnit}");
        builder.AppendLine($"- 1マス: {FormatNumber(project.CellDuration)} {project.TimeUnit}");
        builder.AppendLine($"- 全体期間: {FormatNumber(project.TotalDuration)} {project.TimeUnit}");
        builder.AppendLine($"- レーン数: {project.Lanes.Count}");
        builder.AppendLine($"- タスク数: {project.Items.Count}");
        builder.AppendLine();

        foreach (var lane in project.Lanes)
        {
            groupedByLane.TryGetValue(lane.Id, out var laneRows);
            laneRows ??= new List<ExportRow>();

            builder.AppendLine($"## {lane.Name} ({laneRows.Count}件)");
            builder.AppendLine();

            if (laneRows.Count == 0)
            {
                builder.AppendLine("タスクはありません。");
                builder.AppendLine();
                continue;
            }

            AppendMarkdownTasks(builder, laneRows, project.TimeUnit);
        }

        if (unassigned.Count > 0)
        {
            builder.AppendLine($"## 未割り当て ({unassigned.Count}件)");
            builder.AppendLine();
            AppendMarkdownTasks(builder, unassigned, project.TimeUnit);
        }

        return builder.ToString();
    }

    private static void AppendMarkdownTasks(StringBuilder builder, IReadOnlyList<ExportRow> rows, string timeUnit)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var item = row.Item;
            builder.AppendLine($"### {index + 1}. {item.Name}");
            builder.AppendLine($"- 時間: {FormatNumber(item.StartTime)}-{FormatNumber(item.StartTime + item.Duration)} {timeUnit}");
            builder.AppendLine($"- 所要時間: {FormatNumber(item.Duration)} {timeUnit}");
            builder.AppendLine($"- カテゴリ: {row.CategoryName}");
            builder.AppendLine($"- 前提条件: {JoinOrNone(item.PreConditions)}");
            builder.AppendLine($"- 完了条件: {JoinOrNone(item.PostConditions)}");

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                builder.AppendLine("説明:");
                foreach (var line in item.Description
                             .Split(["\r\n", "\n"], StringSplitOptions.None)
                             .Where(line => !string.IsNullOrWhiteSpace(line)))
                {
                    builder.AppendLine(line);
                }
            }

            builder.AppendLine();
        }
    }

    private static IEnumerable<ExportRow> EnumerateRows(SequenceProject project)
    {
        var laneOrder = project.Lanes
            .Select((lane, index) => (lane.Id, index, lane.Name))
            .ToDictionary(entry => entry.Id, entry => (entry.index, entry.Name));
        var categoryNames = project.Categories.ToDictionary(category => category.Id, category => category.Name);

        return project.Items
            .Select(item =>
            {
                bool hasLane = laneOrder.TryGetValue(item.LaneId, out var lane);
                string laneName = hasLane ? lane.Name : "未割り当て";
                int laneIndex = hasLane ? lane.index : int.MaxValue;
                string categoryName = item.CategoryId != Guid.Empty && categoryNames.TryGetValue(item.CategoryId, out var categoryNameValue)
                    ? categoryNameValue
                    : "未分類";

                return new ExportRow(item, laneName, laneIndex, categoryName);
            })
            .OrderBy(row => row.LaneIndex)
            .ThenBy(row => row.Item.StartTime)
            .ThenBy(row => row.Item.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private static string JoinOrNone(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? "なし" : string.Join(", ", values);

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed record ExportRow(SequenceItem Item, string LaneName, int LaneIndex, string CategoryName);
}

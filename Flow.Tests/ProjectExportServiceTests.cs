using System;
using System.Linq;
using Flow.Models;
using Flow.Services;

namespace Flow.Tests;

public sealed class ProjectExportServiceTests
{
    private readonly ProjectExportService _service = new();

    [Fact]
    public void ExportCsv_ProducesOrderedSpreadsheetFriendlyRows()
    {
        var planCategory = new ProjectCategory
        {
            Id = Guid.NewGuid(),
            Name = "企画",
        };
        var reviewCategory = new ProjectCategory
        {
            Id = Guid.NewGuid(),
            Name = "レビュー",
        };
        var planningLane = new Lane
        {
            Id = Guid.NewGuid(),
            Name = "企画レーン",
        };
        var executionLane = new Lane
        {
            Id = Guid.NewGuid(),
            Name = "実装レーン",
        };

        var project = new SequenceProject
        {
            Name = "Sample Flow",
            TimeUnit = "日",
            CellDuration = 0.5,
            TotalDuration = 12,
            Categories = [planCategory, reviewCategory],
            Lanes = [planningLane, executionLane],
            Items =
            [
                new SequenceItem
                {
                    Name = "仕様レビュー",
                    Description = "関係者レビュー",
                    StartTime = 3,
                    Duration = 1,
                    LaneId = executionLane.Id,
                    CategoryId = reviewCategory.Id,
                    PreConditions = ["ドラフト"],
                    PostConditions = ["承認"],
                },
                new SequenceItem
                {
                    Name = "Kickoff, \"Plan\"",
                    Description = "Share \"scope\", estimate",
                    StartTime = 1.25,
                    Duration = 2.5,
                    LaneId = planningLane.Id,
                    CategoryId = planCategory.Id,
                    PostConditions = ["ドラフト", "予算"],
                },
            ],
        };

        string csv = _service.ExportCsv(project);
        var lines = csv.TrimEnd().Split(Environment.NewLine);

        Assert.Equal(
            "Project,Time Unit,Cell Duration,Total Duration,Lane,Task,Start,Duration,End,Category,Preconditions,Postconditions,Description",
            lines[0]);
        Assert.Equal(
            "Sample Flow,日,0.5,12,企画レーン,\"Kickoff, \"\"Plan\"\"\",1.25,2.5,3.75,企画,なし,\"ドラフト, 予算\",\"Share \"\"scope\"\", estimate\"",
            lines[1]);
        Assert.Equal(
            "Sample Flow,日,0.5,12,実装レーン,仕様レビュー,3,1,4,レビュー,ドラフト,承認,関係者レビュー",
            lines[2]);
    }

    [Fact]
    public void ExportMarkdown_GroupsTasksByLaneAndIncludesReadableSections()
    {
        var planCategory = new ProjectCategory
        {
            Id = Guid.NewGuid(),
            Name = "企画",
        };
        var planningLane = new Lane
        {
            Id = Guid.NewGuid(),
            Name = "企画レーン",
        };
        var emptyLane = new Lane
        {
            Id = Guid.NewGuid(),
            Name = "空レーン",
        };

        var project = new SequenceProject
        {
            Name = "Alpha Project",
            TimeUnit = "日",
            CellDuration = 1,
            TotalDuration = 8,
            Categories = [planCategory],
            Lanes = [planningLane, emptyLane],
            Items =
            [
                new SequenceItem
                {
                    Name = "要件定義",
                    Description = "主要機能を整理\n承認用ドラフトを準備",
                    StartTime = 0,
                    Duration = 2,
                    LaneId = planningLane.Id,
                    CategoryId = planCategory.Id,
                    PostConditions = ["ドラフト"],
                },
                new SequenceItem
                {
                    Name = "未割り当てタスク",
                    StartTime = 5,
                    Duration = 1,
                    LaneId = Guid.NewGuid(),
                    PreConditions = ["ドラフト"],
                },
            ],
        };

        string markdown = _service.ExportMarkdown(project);

        Assert.Contains("# Alpha Project", markdown);
        Assert.Contains("- 時間単位: 日", markdown);
        Assert.Contains("- レーン数: 2", markdown);
        Assert.Contains("- タスク数: 2", markdown);
        Assert.Contains("## 企画レーン (1件)", markdown);
        Assert.Contains("### 1. 要件定義", markdown);
        Assert.Contains("- 時間: 0-2 日", markdown);
        Assert.Contains("- 所要時間: 2 日", markdown);
        Assert.Contains("- カテゴリ: 企画", markdown);
        Assert.Contains("- 前提条件: なし", markdown);
        Assert.Contains("- 完了条件: ドラフト", markdown);
        Assert.Contains("説明:", markdown);
        Assert.Contains("主要機能を整理", markdown);
        Assert.Contains("承認用ドラフトを準備", markdown);
        Assert.Contains("## 空レーン (0件)", markdown);
        Assert.Contains("タスクはありません。", markdown);
        Assert.Contains("## 未割り当て (1件)", markdown);
        Assert.Contains("- カテゴリ: 未分類", markdown);
        Assert.Contains("- 前提条件: ドラフト", markdown);

        Assert.True(
            markdown.IndexOf("## 企画レーン (1件)", StringComparison.Ordinal) <
            markdown.IndexOf("## 空レーン (0件)", StringComparison.Ordinal));
        Assert.True(
            markdown.IndexOf("## 空レーン (0件)", StringComparison.Ordinal) <
            markdown.IndexOf("## 未割り当て (1件)", StringComparison.Ordinal));
    }

    [Fact]
    public void Export_ReturnsFormatMetadataForParentIntegration()
    {
        var project = new SequenceProject
        {
            Name = "Integration Sample",
            Items = [],
        };

        var csv = _service.Export(project, ProjectExportFormat.Csv);
        var markdown = _service.Export(project, ProjectExportFormat.Markdown);

        Assert.Equal(ProjectExportFormat.Csv, csv.Format);
        Assert.Equal(".csv", csv.FileExtension);
        Assert.Equal("text/csv", csv.ContentType);
        Assert.StartsWith("Project,Time Unit", csv.Content, StringComparison.Ordinal);

        Assert.Equal(ProjectExportFormat.Markdown, markdown.Format);
        Assert.Equal(".md", markdown.FileExtension);
        Assert.Equal("text/markdown", markdown.ContentType);
        Assert.StartsWith("# Integration Sample", markdown.Content, StringComparison.Ordinal);
    }
}

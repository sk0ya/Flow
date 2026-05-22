using System;
using System.IO;
using Flow.Models;
using Flow.Services;

namespace Flow.Tests;

public sealed class DraftStorageServiceTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsDraftProjectAndMetadata()
    {
        string tempDirectory = CreateTempDirectory();

        try
        {
            var service = new ProjectDraftService(tempDirectory);
            var laneId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var sourceProjectPath = Path.Combine(tempDirectory, "..", "sample.flow");
            var project = new SequenceProject
            {
                Name = "Draft Project",
                TimeUnit = "時間",
                CellDuration = 0.5,
                GridDivisions = 2,
                TotalDuration = 24,
                Categories =
                [
                    new ProjectCategory
                    {
                        Id = categoryId,
                        Name = "Planning",
                        Color = "#112233",
                    },
                ],
                Lanes =
                [
                    new Lane
                    {
                        Id = laneId,
                        Name = "Lane A",
                    },
                ],
                Items =
                [
                    new SequenceItem
                    {
                        Id = Guid.NewGuid(),
                        Name = "Draft Task",
                        Description = "Pending work",
                        StartTime = 2,
                        Duration = 3,
                        LaneId = laneId,
                        CategoryId = categoryId,
                        PreConditions = ["pre-1"],
                        PostConditions = ["post-1", "post-2"],
                    },
                ],
            };

            var beforeSave = DateTimeOffset.UtcNow;
            service.SaveDraft(project, sourceProjectPath);
            var afterSave = DateTimeOffset.UtcNow;

            Assert.True(service.HasDraft());
            Assert.True(File.Exists(service.DraftFilePath));

            var draft = service.LoadDraft();

            Assert.NotNull(draft);
            Assert.Equal(Path.GetFullPath(sourceProjectPath), draft!.SourceProjectPath);
            Assert.InRange(draft.SavedAtUtc, beforeSave, afterSave);
            Assert.Equal("Draft Project", draft.Project.Name);
            Assert.Equal("時間", draft.Project.TimeUnit);
            Assert.Equal(0.5, draft.Project.CellDuration);
            Assert.Equal(2, draft.Project.GridDivisions);
            Assert.Equal(24, draft.Project.TotalDuration);
            Assert.Single(draft.Project.Categories);
            Assert.Equal(categoryId, draft.Project.Categories[0].Id);
            Assert.Equal("Planning", draft.Project.Categories[0].Name);
            Assert.Single(draft.Project.Lanes);
            Assert.Equal(laneId, draft.Project.Lanes[0].Id);
            Assert.Single(draft.Project.Items);
            Assert.Equal("Draft Task", draft.Project.Items[0].Name);
            Assert.Equal(["pre-1"], draft.Project.Items[0].PreConditions);
            Assert.Equal(["post-1", "post-2"], draft.Project.Items[0].PostConditions);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenDraftDoesNotExist_ReturnsNull()
    {
        string tempDirectory = CreateTempDirectory();

        try
        {
            var service = new ProjectDraftService(tempDirectory);

            Assert.False(service.HasDraft());
            Assert.Null(service.LoadDraft());
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Delete_RemovesDraftAndCanBeCalledRepeatedly()
    {
        string tempDirectory = CreateTempDirectory();

        try
        {
            var service = new ProjectDraftService(tempDirectory);
            service.SaveDraft(new SequenceProject { Name = "Delete Me" });

            Assert.True(service.HasDraft());

            service.DeleteDraft();
            service.DeleteDraft();

            Assert.False(service.HasDraft());
            Assert.False(File.Exists(service.DraftFilePath));
            Assert.Null(service.LoadDraft());
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenDraftIsCorrupted_ReturnsNull()
    {
        string tempDirectory = CreateTempDirectory();

        try
        {
            var service = new ProjectDraftService(tempDirectory);
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(service.DraftFilePath, "{ not-valid-json");

            Assert.True(service.HasDraft());
            Assert.Null(service.LoadDraft());
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Flow.Tests",
            nameof(DraftStorageServiceTests),
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}

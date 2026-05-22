using System;
using System.IO;
using System.Text.Json;
using Flow.Models;

namespace Flow.Services;

public class DraftStorageService
{
    private const string DraftFileName = "project-draft.json";
    private readonly string _draftFilePath;

    public DraftStorageService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flow"))
    {
    }

    public DraftStorageService(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        _draftFilePath = Path.Combine(storageDirectory, DraftFileName);
    }

    public string DraftFilePath => _draftFilePath;

    public bool HasDraft() => File.Exists(_draftFilePath);

    public void Save(SequenceProject project, string? sourceProjectPath = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var directory = Path.GetDirectoryName(_draftFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var draft = new ProjectDraft
        {
            SourceProjectPath = NormalizePath(sourceProjectPath),
            SavedAtUtc = DateTimeOffset.UtcNow,
            Project = project,
        };

        var json = JsonSerializer.Serialize(draft, StorageJson.Options);
        File.WriteAllText(_draftFilePath, json);
    }

    public ProjectDraft? Load()
    {
        try
        {
            if (!File.Exists(_draftFilePath))
                return null;

            var json = File.ReadAllText(_draftFilePath);
            var draft = JsonSerializer.Deserialize<ProjectDraft>(json, StorageJson.Options);
            if (draft?.Project == null)
                return null;

            draft.SourceProjectPath = NormalizePath(draft.SourceProjectPath);
            return draft;
        }
        catch
        {
            return null;
        }
    }

    public void Delete()
    {
        if (File.Exists(_draftFilePath))
            File.Delete(_draftFilePath);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}

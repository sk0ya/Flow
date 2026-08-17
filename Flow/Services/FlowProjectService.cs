using System;
using System.IO;
using Flow.Models;

namespace Flow.Services;

/// <summary>
/// Small file API for tools that need to read or write .flow documents without
/// constructing the WPF editor.
/// </summary>
public sealed class FlowProjectService
{
    private readonly StorageService _storage;

    public FlowProjectService(StorageService? storage = null)
    {
        _storage = storage ?? new StorageService();
    }

    public SequenceProject Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return _storage.Load(NormalizePath(filePath));
    }

    public void Save(string filePath, SequenceProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(project);
        _storage.Save(project, NormalizePath(filePath));
    }

    private static string NormalizePath(string filePath)
    {
        var path = Path.GetFullPath(filePath);
        if (!string.Equals(Path.GetExtension(path), ".flow", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Flow project files must use the .flow extension.", nameof(filePath));
        return path;
    }
}

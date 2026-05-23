using System;
using System.IO;
using System.Text.Json;
using Flow.Models;

namespace Flow.Services;

public class AppStateService
{
    private readonly string _stateFilePath;

    public AppStateService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flow");

        _stateFilePath = Path.Combine(appDataDirectory, "app-state.json");
    }

    public AppState Load()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
                return new AppState();

            var json = File.ReadAllText(_stateFilePath);
            return JsonSerializer.Deserialize<AppState>(json, StorageJson.Options) ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public void Save(AppState state)
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state, StorageJson.Options);
        File.WriteAllText(_stateFilePath, json);
    }
}

using System.IO;
using System.Text.Json;
using Flow.Models;

namespace Flow.Services;

public class StorageService
{
    public void Save(SequenceProject project, string filePath)
    {
        var json = JsonSerializer.Serialize(project, StorageJson.Options);
        File.WriteAllText(filePath, json);
    }

    public SequenceProject Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<SequenceProject>(json, StorageJson.Options) ?? new SequenceProject();
    }
}

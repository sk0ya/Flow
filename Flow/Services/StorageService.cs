using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flow.Models;

namespace Flow.Services;

public class StorageService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Save(SequenceProject project, string filePath)
    {
        var json = JsonSerializer.Serialize(project, Options);
        File.WriteAllText(filePath, json);
    }

    public SequenceProject Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<SequenceProject>(json, Options) ?? new SequenceProject();
    }
}

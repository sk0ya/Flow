using System.IO;

namespace Flow.ViewModels;

public sealed class RecentProjectEntry
{
    public RecentProjectEntry(string filePath)
    {
        FilePath = filePath;
        DisplayName = Path.GetFileNameWithoutExtension(filePath);
        DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
    }

    public string FilePath { get; }
    public string DisplayName { get; }
    public string DirectoryPath { get; }
}

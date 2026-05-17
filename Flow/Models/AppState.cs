using System.Collections.Generic;

namespace Flow.Models;

public class AppState
{
    public string? LastProjectPath { get; set; }
    public List<string> RecentProjectPaths { get; set; } = new();
}

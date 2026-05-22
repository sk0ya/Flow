using System;

namespace Flow.Models;

public class ProjectDraft
{
    public string? SourceProjectPath { get; set; }
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public SequenceProject Project { get; set; } = new();
}

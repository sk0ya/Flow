using System;
using System.Collections.Generic;

namespace Flow.Models;

public class SequenceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public double StartTime { get; set; } = 0;
    public double Duration { get; set; } = 1.0;
    public Guid LaneId { get; set; }
    public Guid CategoryId { get; set; } = Guid.Empty;
    public List<string> PreConditions { get; set; } = new();
    public List<string> PostConditions { get; set; } = new();
}

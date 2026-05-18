using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Flow.Models;

public class SequenceProject
{
    public string Name { get; set; } = "新しいプロジェクト";
    public string TimeUnit { get; set; } = "日";
    public double CellDuration { get; set; } = 1.0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? GridDivisions { get; set; }
    public double TotalDuration { get; set; } = 10.0;
    public List<ProjectCategory> Categories { get; set; } = new();
    public List<Lane> Lanes { get; set; } = new() { new Lane { Name = "レーン 1" } };
    public List<SequenceItem> Items { get; set; } = new();
}

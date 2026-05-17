using System.Collections.Generic;

namespace Flow.Models;

public class SequenceProject
{
    public string Name { get; set; } = "新しいプロジェクト";
    public string TimeUnit { get; set; } = "日";
    public double TotalDuration { get; set; } = 10.0;
    public List<Lane> Lanes { get; set; } = new() { new Lane { Name = "レーン 1" } };
    public List<SequenceItem> Items { get; set; } = new();
}

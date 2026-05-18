using System;

namespace Flow.Models;

public class ProjectCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#94A3B8";
}

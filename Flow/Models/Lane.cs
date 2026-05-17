using System;

namespace Flow.Models;

public class Lane
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "レーン 1";
}

using System;
using System.IO;
using Flow.Models;
using Flow.Services;

namespace Flow.Tests;

public sealed class FlowProjectServiceTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsFlowDocument()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var path = Path.Combine(directory, "sample.flow");
            var project = new SequenceProject { Name = "Embedded project" };

            var service = new FlowProjectService();
            service.Save(path, project);

            Assert.Equal("Embedded project", service.Load(path).Name);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenExtensionIsNotFlow_Throws()
    {
        var service = new FlowProjectService();

        Assert.Throws<ArgumentException>(() => service.Load("project.json"));
    }
}

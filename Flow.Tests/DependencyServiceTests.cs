using System;
using System.Collections.Generic;
using System.Linq;
using Flow.Models;
using Flow.Services;

namespace Flow.Tests;

public sealed class DependencyServiceTests
{
    [Fact]
    public void Analyze_WhenPreconditionHasMultipleProducers_ReportsAmbiguityAndAllProducerLinks()
    {
        var producerA = CreateItem("Producer A", duration: 2, postConditions: ["ready"]);
        var producerB = CreateItem("Producer B", duration: 1, postConditions: ["ready"]);
        var consumer = CreateItem("Consumer", duration: 3, preConditions: ["ready"]);
        var service = new DependencyService();

        DependencyResult result = service.Analyze([producerA, producerB, consumer]);

        var requirement = Assert.Single(result.Requirements);
        Assert.Equal(consumer.Id, requirement.ConsumerId);
        Assert.Equal("ready", requirement.Condition);
        Assert.True(requirement.IsResolved);
        Assert.True(requirement.IsAmbiguous);
        Assert.Equal([producerA.Id, producerB.Id], requirement.ProducerIds);

        Assert.Equal(2, result.Edges.Count);
        Assert.Contains(result.Edges, edge => edge.FromId == producerA.Id && edge.ToId == consumer.Id && edge.Condition == "ready");
        Assert.Contains(result.Edges, edge => edge.FromId == producerB.Id && edge.ToId == consumer.Id && edge.Condition == "ready");

        var error = Assert.Single(result.Errors, entry => entry.Type == ValidationErrorType.AmbiguousProducers);
        Assert.Equal(consumer.Id, error.ItemId);
        Assert.Contains("Producer A", error.Message);
        Assert.Contains("Producer B", error.Message);
    }

    [Fact]
    public void Analyze_WhenDependencyGraphContainsCycle_ReportsCycleAndSkipsCriticalPath()
    {
        var itemA = CreateItem("A", duration: 2, preConditions: ["done-c"], postConditions: ["done-a"]);
        var itemB = CreateItem("B", duration: 3, preConditions: ["done-a"], postConditions: ["done-b"]);
        var itemC = CreateItem("C", duration: 1, preConditions: ["done-b"], postConditions: ["done-c"]);
        var service = new DependencyService();

        DependencyResult result = service.Analyze([itemA, itemB, itemC]);

        var cycle = Assert.Single(result.Cycles);
        Assert.Equal([itemA.Id, itemB.Id, itemC.Id], cycle.ItemIds);
        Assert.Equal(3, cycle.Edges.Count);
        Assert.Equal(3, result.Errors.Count(entry => entry.Type == ValidationErrorType.CircularDependency));
        Assert.False(result.CriticalPath.IsAvailable);
        Assert.Equal("Critical path is unavailable while circular dependencies exist.", result.CriticalPath.UnavailableReason);
        Assert.Empty(result.CriticalPath.ItemIds);
        Assert.Empty(result.CriticalPath.Activities);
    }

    [Fact]
    public void Analyze_WhenGraphIsAcyclic_ReturnsCriticalPathScheduleData()
    {
        var itemA = CreateItem("A", duration: 2, postConditions: ["x"]);
        var itemB = CreateItem("B", duration: 4, preConditions: ["x"]);
        var itemC = CreateItem("C", duration: 1, preConditions: ["x"], postConditions: ["y"]);
        var itemD = CreateItem("D", duration: 10, preConditions: ["y"]);
        var service = new DependencyService();

        DependencyResult result = service.Analyze([itemA, itemB, itemC, itemD]);

        Assert.True(result.CriticalPath.IsAvailable);
        Assert.Equal(13, result.CriticalPath.Duration);
        Assert.Equal([itemA.Id, itemC.Id, itemD.Id], result.CriticalPath.ItemIds);

        var activities = result.CriticalPath.Activities.ToDictionary(activity => activity.ItemId);
        Assert.Equal(4, activities.Count);

        Assert.Equal(0, activities[itemA.Id].EarliestStart);
        Assert.Equal(2, activities[itemA.Id].EarliestFinish);
        Assert.Equal(0, activities[itemA.Id].LatestStart);
        Assert.True(activities[itemA.Id].IsCritical);

        Assert.Equal(2, activities[itemB.Id].EarliestStart);
        Assert.Equal(6, activities[itemB.Id].EarliestFinish);
        Assert.Equal(9, activities[itemB.Id].LatestStart);
        Assert.Equal(7, activities[itemB.Id].TotalFloat);
        Assert.False(activities[itemB.Id].IsCritical);

        Assert.Equal(3, activities[itemD.Id].EarliestStart);
        Assert.Equal(13, activities[itemD.Id].EarliestFinish);
        Assert.Equal(13, activities[itemD.Id].LatestFinish);
        Assert.True(activities[itemD.Id].IsCritical);
    }

    private static SequenceItem CreateItem(
        string name,
        double duration,
        IReadOnlyList<string>? preConditions = null,
        IReadOnlyList<string>? postConditions = null)
    {
        return new SequenceItem
        {
            Id = Guid.NewGuid(),
            Name = name,
            Duration = duration,
            PreConditions = preConditions?.ToList() ?? new List<string>(),
            PostConditions = postConditions?.ToList() ?? new List<string>(),
        };
    }
}

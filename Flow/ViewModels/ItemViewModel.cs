using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flow.Models;

namespace Flow.ViewModels;

public partial class ItemViewModel : ObservableObject
{
    [ObservableProperty] private Guid   _id;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private double _startTime;
    [ObservableProperty] private double _duration = 1.0;
    [ObservableProperty] private Guid   _laneId;

    [ObservableProperty] private string _newPreCondition  = "";
    [ObservableProperty] private string _newPostCondition = "";

    // Set by DependencyService
    [ObservableProperty] private bool   _hasErrors;
    [ObservableProperty] private string _errorMessage = "";

    public ObservableCollection<ConditionEntry> PreConditions  { get; } = new();
    public ObservableCollection<ConditionEntry> PostConditions { get; } = new();

    public ItemViewModel() { Id = Guid.NewGuid(); }

    public ItemViewModel(SequenceItem model)
    {
        _id        = model.Id;
        _name      = model.Name;
        _description = model.Description;
        _startTime = model.StartTime;
        _duration  = model.Duration;
        _laneId    = model.LaneId;
        foreach (var c in model.PreConditions)  PreConditions.Add(new ConditionEntry(c));
        foreach (var c in model.PostConditions) PostConditions.Add(new ConditionEntry(c));
    }

    [RelayCommand]
    private void AddPreCondition()
    {
        var v = NewPreCondition.Trim();
        if (!string.IsNullOrEmpty(v)) { PreConditions.Add(new ConditionEntry(v)); NewPreCondition = ""; }
    }

    [RelayCommand]
    private void RemovePreCondition(ConditionEntry e) => PreConditions.Remove(e);

    [RelayCommand]
    private void AddPostCondition()
    {
        var v = NewPostCondition.Trim();
        if (!string.IsNullOrEmpty(v)) { PostConditions.Add(new ConditionEntry(v)); NewPostCondition = ""; }
    }

    [RelayCommand]
    private void RemovePostCondition(ConditionEntry e) => PostConditions.Remove(e);

    public SequenceItem ToModel() => new()
    {
        Id = Id, Name = Name, Description = Description,
        StartTime = StartTime, Duration = Duration, LaneId = LaneId,
        PreConditions  = new([..PreConditions.Select(e => e.Value)]),
        PostConditions = new([..PostConditions.Select(e => e.Value)]),
    };
}

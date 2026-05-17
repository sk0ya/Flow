using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Flow.Models;

namespace Flow.ViewModels;

public partial class LaneViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = "";

    public LaneViewModel(Lane lane) { _id = lane.Id; _name = lane.Name; }
    public LaneViewModel(string name = "新しいレーン") { Id = Guid.NewGuid(); Name = name; }

    public Lane ToModel() => new() { Id = Id, Name = Name };
}

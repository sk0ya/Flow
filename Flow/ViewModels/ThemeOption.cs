namespace Flow.ViewModels;

public sealed class ThemeOption
{
    public ThemeOption(string key, string name, string description)
    {
        Key = key;
        Name = name;
        Description = description;
    }

    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
}

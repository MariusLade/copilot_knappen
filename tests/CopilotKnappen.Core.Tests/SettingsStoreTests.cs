using CopilotKnappen.Core;

namespace CopilotKnappen.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "knappen-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_creates_defaults()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var settings = store.Load();
        Assert.NotEmpty(settings.CustomCommands);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Round_trips_commands()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var settings = new AppSettings
        {
            SearchUrlTemplate = "https://www.bing.com/search?q={query}",
            VolumeStep = 7,
            CustomCommands = [new() { Name = "Test", Keywords = "t", Match = MatchMode.Exact, Action = ActionType.Volume, Target = "set", Arguments = "20" }],
        };
        store.Save(settings);

        var loaded = store.Load();
        Assert.Equal(7, loaded.VolumeStep);
        Assert.Equal("https://www.bing.com/search?q={query}", loaded.SearchUrlTemplate);
        var cmd = Assert.Single(loaded.CustomCommands);
        Assert.Equal(ActionType.Volume, cmd.Action);
        Assert.Equal(MatchMode.Exact, cmd.Match);
        Assert.Contains("\"Volume\"", File.ReadAllText(store.FilePath)); // enums are stored as text
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_keeps_a_backup()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        var settings = new SettingsStore(path).Load();
        Assert.NotEmpty(settings.CustomCommands);
        Assert.True(File.Exists(path + ".broken"));
    }

    [Fact]
    public void Clone_is_deep()
    {
        var a = AppSettings.CreateDefault();
        var b = a.Clone();
        b.CustomCommands[0].Name = "changed";
        b.CustomCommands.Clear();
        Assert.NotEqual("changed", a.CustomCommands[0].Name);
        Assert.NotEmpty(a.CustomCommands);
    }
}

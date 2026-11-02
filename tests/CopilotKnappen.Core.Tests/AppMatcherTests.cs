using CopilotKnappen.Core;

namespace CopilotKnappen.Core.Tests;

public class AppMatcherTests
{
    private static readonly AppEntry[] Apps =
    [
        new("Microsoft Word", "word", AppEntryKind.AppsFolder),
        new("WordPad", "wordpad", AppEntryKind.AppsFolder),
        new("Microsoft Teams", "teams", AppEntryKind.AppsFolder),
        new("Task Manager", "taskmgr", AppEntryKind.AppsFolder),
        new("Task Scheduler", "taskschd", AppEntryKind.AppsFolder),
        new("Steam", "steam", AppEntryKind.Shortcut),
        new("Steam", "steam-folder", AppEntryKind.AppsFolder),
        new("Uninstall Steam", "uninst", AppEntryKind.Shortcut),
        new("Notepad++", "npp", AppEntryKind.AppsFolder),
    ];

    [Theory]
    [InlineData("word", "word")]
    [InlineData("teams", "teams")]
    [InlineData("task manager", "taskmgr")]
    [InlineData("taskmanager", "taskmgr")]
    [InlineData("tm", "taskmgr")]
    [InlineData("notepad++", "npp")]
    public void Finds_expected_app(string query, string target) =>
        Assert.Equal(target, AppMatcher.FindBest(Apps, query).App?.LaunchTarget);

    [Fact]
    public void Prefers_start_menu_entry_on_ties() =>
        Assert.Equal("steam-folder", AppMatcher.FindBest(Apps, "steam").App?.LaunchTarget);

    [Fact]
    public void Skips_uninstallers_unless_asked()
    {
        Assert.NotEqual("uninst", AppMatcher.FindBest(Apps, "steam").App?.LaunchTarget);
        Assert.Equal("uninst", AppMatcher.FindBest(Apps, "uninstall steam").App?.LaunchTarget);
    }

    [Fact]
    public void Exact_match_scores_one() => Assert.Equal(1.0, AppMatcher.FindBest(Apps, "WordPad").Score, 3);

    [Theory]
    [InlineData("pizza")]
    [InlineData("")]
    public void No_match(string query) => Assert.Null(AppMatcher.FindBest(Apps, query).App);

    [Fact]
    public void Normalize_strips_punctuation() =>
        Assert.Equal("visual studio code 2022", AppMatcher.Normalize("  Visual-Studio   Code (2022) "));
}

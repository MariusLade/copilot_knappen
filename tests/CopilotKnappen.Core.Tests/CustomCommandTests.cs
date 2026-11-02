using CopilotKnappen.Core;

namespace CopilotKnappen.Core.Tests;

public class CustomCommandTests
{
    private static readonly IAppCatalog Apps = new StaticAppCatalog(
    [
        new("Visual Studio Code", "Microsoft.VisualStudioCode", AppEntryKind.AppsFolder),
    ]);

    private static ResolvedAction Parse(string text, params CustomCommand[] commands) =>
        new IntentParser(new AppSettings { CustomCommands = commands.ToList() }, Apps).Parse(text);

    [Fact]
    public void Url_command_encodes_query()
    {
        var action = Parse("wiki new york city",
            new CustomCommand { Name = "Wiki", Keywords = "wiki, wikipedia", Action = ActionType.OpenUrl,
                                Target = "https://en.wikipedia.org/w/index.php?search={query}" });
        Assert.Equal(ActionType.OpenUrl, action.Type);
        Assert.Equal("https://en.wikipedia.org/w/index.php?search=new%20york%20city", action.Target);
        Assert.Equal("Wiki", action.CommandName);
        Assert.StartsWith("Wiki — ", action.Description);
    }

    [Fact]
    public void Custom_commands_override_built_ins()
    {
        var action = Parse("play something",
            new CustomCommand { Keywords = "play", Action = ActionType.YouTubeSearch });
        Assert.Equal(ActionType.YouTubeSearch, action.Type);
        Assert.Equal("something", action.Target);
    }

    [Fact]
    public void Longest_keyword_wins()
    {
        var action = Parse("g maps oslo",
            new CustomCommand { Name = "short", Keywords = "g", Action = ActionType.OpenUrl, Target = "https://a/{query}" },
            new CustomCommand { Name = "long", Keywords = "g maps", Action = ActionType.OpenUrl, Target = "https://b/{query}" });
        Assert.Equal("https://b/oslo", action.Target);
    }

    [Fact]
    public void Keyword_must_be_a_whole_word()
    {
        var action = Parse("gmail",
            new CustomCommand { Keywords = "g", Action = ActionType.OpenUrl, Target = "https://a/{query}" });
        Assert.NotEqual("https://a/mail", action.Target);
    }

    [Fact]
    public void Exact_match_requires_whole_prompt()
    {
        var cmd = new CustomCommand { Keywords = "lock", Match = MatchMode.Exact, Action = ActionType.RunProgram,
                                      Target = "rundll32.exe", Arguments = "user32.dll,LockWorkStation" };
        var action = Parse("lock", cmd);
        Assert.Equal(ActionType.RunProgram, action.Type);
        Assert.Equal("user32.dll,LockWorkStation", action.Arguments);
        Assert.NotEqual(ActionType.RunProgram, Parse("lock screen tips", cmd).Type);
    }

    [Fact]
    public void Disabled_commands_are_ignored()
    {
        var action = Parse("wiki x", new CustomCommand { Enabled = false, Keywords = "wiki", Target = "https://w/{query}" });
        Assert.Equal(ActionType.WebSearch, action.Type);
    }

    [Fact]
    public void Regex_command_uses_query_group()
    {
        var action = Parse("weather in bergen",
            new CustomCommand { Keywords = @"^weather (?:in |for )?(?<query>.+)$", Match = MatchMode.Regex,
                                Action = ActionType.OpenUrl, Target = "https://www.yr.no/en/search?q={query}" });
        Assert.Equal("https://www.yr.no/en/search?q=bergen", action.Target);
    }

    [Fact]
    public void Invalid_regex_is_ignored()
    {
        var action = Parse("anything", new CustomCommand { Keywords = "([", Match = MatchMode.Regex, Target = "https://x" });
        Assert.Equal(ActionType.WebSearch, action.Type);
    }

    [Fact]
    public void Program_command_expands_raw_query_in_arguments()
    {
        var action = Parse("note hello world",
            new CustomCommand { Keywords = "note", Action = ActionType.RunProgram, Target = "notepad.exe", Arguments = "\"{query}.txt\"" });
        Assert.Equal("notepad.exe", action.Target);
        Assert.Equal("\"hello world.txt\"", action.Arguments);
    }

    [Fact]
    public void Open_app_command_resolves_app()
    {
        var action = Parse("code", new CustomCommand { Keywords = "code", Match = MatchMode.Exact, Action = ActionType.OpenApp, Target = "visual studio code" });
        Assert.Equal(ActionType.OpenApp, action.Type);
        Assert.Equal("Visual Studio Code", action.App!.Name);
    }

    [Theory]
    [InlineData("toggle", "", "toggle", "spotify")]
    [InlineData("skip", "any", "next", "any")]
    [InlineData("previous", "Spotify", "previous", "spotify")]
    public void Media_command(string target, string args, string expectedCommand, string expectedPlayer)
    {
        var action = Parse("x", new CustomCommand { Keywords = "x", Action = ActionType.Media, Target = target, Arguments = args });
        Assert.Equal(ActionType.Media, action.Type);
        Assert.Equal(expectedCommand, action.Target);
        Assert.Equal(expectedPlayer, action.Arguments);
    }

    [Theory]
    [InlineData("v 35", "set", "{query}", "set", "35")]
    [InlineData("v", "up", "", "up", "10")]
    [InlineData("v", "mute", "", "mute", "")]
    public void Volume_command(string input, string target, string args, string expectedCommand, string expectedAmount)
    {
        var action = Parse(input, new CustomCommand { Keywords = "v", Action = ActionType.Volume, Target = target, Arguments = args });
        Assert.Equal(ActionType.Volume, action.Type);
        Assert.Equal(expectedCommand, action.Target);
        Assert.Equal(expectedAmount, action.Arguments);
    }

    [Fact]
    public void Search_commands_default_to_query()
    {
        var action = Parse("sp abba", new CustomCommand { Keywords = "sp", Action = ActionType.SpotifySearch });
        Assert.Equal(ActionType.SpotifySearch, action.Type);
        Assert.Equal("abba", action.Target);
    }
}

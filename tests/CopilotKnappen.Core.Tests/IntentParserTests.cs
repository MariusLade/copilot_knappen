using CopilotKnappen.Core;

namespace CopilotKnappen.Core.Tests;

public class IntentParserTests
{
    private static readonly IAppCatalog Apps = new StaticAppCatalog(
    [
        new("Spotify", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", AppEntryKind.AppsFolder),
        new("Uninstall Spotify", @"C:\Users\me\uninstall.lnk", AppEntryKind.Shortcut),
        new("Google Chrome", "Chrome", AppEntryKind.AppsFolder),
        new("Microsoft Edge", "MSEdge", AppEntryKind.AppsFolder),
        new("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", AppEntryKind.AppsFolder),
        new("Visual Studio Code", "Microsoft.VisualStudioCode", AppEntryKind.AppsFolder),
        new("Discord", "com.squirrel.Discord.Discord", AppEntryKind.AppsFolder),
        new("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", AppEntryKind.AppsFolder),
        new("Steam", "Valve.Steam", AppEntryKind.AppsFolder),
        new("Word", "Microsoft.Office.WINWORD.EXE.15", AppEntryKind.AppsFolder),
    ]);

    private static IntentParser Parser(AppSettings? settings = null) =>
        new(settings ?? new AppSettings(), Apps);

    private static ResolvedAction Parse(string text, AppSettings? settings = null) => Parser(settings).Parse(text);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_does_nothing(string text) => Assert.Equal(ActionType.None, Parse(text).Type);

    [Theory]
    [InlineData("settings")]
    [InlineData("Settings")]
    [InlineData("  preferences ")]
    public void Settings_command_opens_settings(string text) => Assert.Equal(ActionType.OpenSettings, Parse(text).Type);

    [Fact]
    public void Sentence_searches_the_web()
    {
        var action = Parse("how tall is the eiffel tower");
        Assert.Equal(ActionType.WebSearch, action.Type);
        Assert.Equal("https://www.google.com/search?q=how%20tall%20is%20the%20eiffel%20tower", action.Target);
    }

    [Fact]
    public void Web_search_uses_configured_engine()
    {
        var settings = new AppSettings { SearchUrlTemplate = "https://duckduckgo.com/?q={query}" };
        Assert.Equal("https://duckduckgo.com/?q=best%20pizza%20in%20oslo", Parse("best pizza in oslo", settings).Target);
    }

    [Theory]
    [InlineData("notepad", "Notepad")]
    [InlineData("Discord", "Discord")]
    [InlineData("chrome", "Google Chrome")]
    [InlineData("edge", "Microsoft Edge")]
    [InlineData("visual studio code", "Visual Studio Code")]
    [InlineData("vscode", "Visual Studio Code")]
    [InlineData("vsc", "Visual Studio Code")]
    [InlineData("calc", "Calculator")]
    [InlineData("open calculator", "Calculator")]
    [InlineData("launch steam", "Steam")]
    [InlineData("spotify", "Spotify")]
    [InlineData("open spotify", "Spotify")]
    public void App_names_open_the_app(string text, string appName)
    {
        var action = Parse(text);
        Assert.Equal(ActionType.OpenApp, action.Type);
        Assert.Equal(appName, action.App!.Name);
    }

    [Fact]
    public void App_detection_can_be_disabled()
    {
        Assert.Equal(ActionType.WebSearch, Parse("discord", new AppSettings { DetectApplications = false }).Type);
    }

    [Theory]
    [InlineData("what is word")]
    [InlineData("word?")]
    [InlineData("cats")]
    public void Questions_and_unknown_words_are_web_searches(string text) =>
        Assert.Equal(ActionType.WebSearch, Parse(text).Type);

    [Theory]
    [InlineData("play bohemian rhapsody", "bohemian rhapsody")]
    [InlineData("spotify daft punk", "daft punk")]
    [InlineData("Spotify search lofi beats", "lofi beats")]
    [InlineData("listen to taylor swift", "taylor swift")]
    [InlineData("play queen on spotify", "queen")]
    [InlineData("radiohead on spotify", "radiohead")]
    public void Spotify_prompts_search_spotify(string text, string query)
    {
        var action = Parse(text);
        Assert.Equal(ActionType.SpotifySearch, action.Type);
        Assert.Equal(query, action.Target);
    }

    [Theory]
    [InlineData("play", MediaCommand.Play)]
    [InlineData("resume", MediaCommand.Play)]
    [InlineData("pause", MediaCommand.Pause)]
    [InlineData("stop music", MediaCommand.Pause)]
    [InlineData("spotify pause", MediaCommand.Pause)]
    [InlineData("skip", MediaCommand.Next)]
    [InlineData("next song", MediaCommand.Next)]
    [InlineData("spotify skip", MediaCommand.Next)]
    [InlineData("skip on spotify", MediaCommand.Next)]
    [InlineData("previous", MediaCommand.Previous)]
    [InlineData("previous track", MediaCommand.Previous)]
    [InlineData("play/pause", MediaCommand.Toggle)]
    [InlineData("spotify play", MediaCommand.Play)]
    public void Media_commands(string text, MediaCommand expected)
    {
        var action = Parse(text);
        Assert.Equal(ActionType.Media, action.Type);
        Assert.Equal(expected.ToString().ToLowerInvariant(), action.Target);
    }

    [Theory]
    [InlineData("volume 50", "set", "50")]
    [InlineData("vol 30%", "set", "30")]
    [InlineData("set volume to 75", "set", "75")]
    [InlineData("40% volume", "set", "40")]
    [InlineData("volume 150", "set", "100")]
    [InlineData("volume up", "up", "10")]
    [InlineData("vol down 5", "down", "5")]
    [InlineData("volume up by 20%", "up", "20")]
    [InlineData("louder", "up", "10")]
    [InlineData("quieter", "down", "10")]
    [InlineData("turn it up", "up", "10")]
    [InlineData("turn the volume down", "down", "10")]
    [InlineData("increase volume", "up", "10")]
    [InlineData("lower the volume by 15", "down", "15")]
    [InlineData("mute", "mute", "")]
    [InlineData("unmute", "unmute", "")]
    [InlineData("max volume", "set", "100")]
    public void Volume_commands(string text, string command, string amount)
    {
        var action = Parse(text);
        Assert.Equal(ActionType.Volume, action.Type);
        Assert.Equal(command, action.Target);
        Assert.Equal(amount, action.Arguments);
    }

    [Fact]
    public void Volume_step_comes_from_settings()
    {
        Assert.Equal("5", Parse("volume up", new AppSettings { VolumeStep = 5 }).Arguments);
    }

    [Theory]
    [InlineData("youtube cat videos", "cat videos")]
    [InlineData("yt how to tie a tie", "how to tie a tie")]
    [InlineData("watch minecraft speedruns", "minecraft speedruns")]
    [InlineData("search youtube for lofi", "lofi")]
    [InlineData("lofi hip hop on youtube", "lofi hip hop")]
    [InlineData("play never gonna give you up on youtube", "never gonna give you up")]
    public void YouTube_prompts_search_youtube(string text, string query)
    {
        var action = Parse(text);
        Assert.Equal(ActionType.YouTubeSearch, action.Type);
        Assert.Equal(query, action.Target);
    }

    [Theory]
    [InlineData("github.com", "https://github.com")]
    [InlineData("www.vg.no", "https://www.vg.no")]
    [InlineData("https://example.org/a?b=c", "https://example.org/a?b=c")]
    [InlineData("localhost:5173", "http://localhost:5173")]
    [InlineData("ms-settings:display", "ms-settings:display")]
    public void Urls_are_opened(string text, string url)
    {
        var action = Parse(text);
        Assert.Equal(ActionType.OpenUrl, action.Type);
        Assert.Equal(url, action.Target);
    }

    [Theory]
    [InlineData(@"C:\Users")]
    [InlineData(@"%APPDATA%\Code")]
    [InlineData("regedit.exe")]
    [InlineData("run regedit")]
    public void Paths_and_programs_run(string text) => Assert.Equal(ActionType.RunProgram, Parse(text).Type);

    [Theory]
    [InlineData("google cheap flights", "https://www.google.com/search?q=cheap%20flights")]
    [InlineData("bing weather", "https://www.bing.com/search?q=weather")]
    public void Explicit_search_engines(string text, string url)
    {
        var action = Parse(text);
        Assert.Equal(ActionType.OpenUrl, action.Type);
        Assert.Equal(url, action.Target);
    }

    [Fact]
    public void Search_prefix_forces_web_search_even_for_app_names()
    {
        var action = Parse("search discord");
        Assert.Equal(ActionType.WebSearch, action.Type);
        Assert.Equal("discord", action.Arguments);
    }

    [Fact]
    public void Start_a_sentence_is_a_web_search() =>
        Assert.Equal(ActionType.WebSearch, Parse("start a small business").Type);
}

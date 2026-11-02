namespace CopilotKnappen.Core;

/// <summary>The kinds of things a prompt (or a custom command) can do.</summary>
public enum ActionType
{
    None,
    WebSearch,
    OpenUrl,
    OpenApp,
    RunProgram,
    ShellCommand,
    SpotifySearch,
    YouTubeSearch,
    Media,
    Volume,
    OpenSettings,
    Exit,
}

public enum MediaCommand
{
    Toggle,
    Play,
    Pause,
    Next,
    Previous,
}

public enum VolumeCommand
{
    Set,
    Up,
    Down,
    Mute,
    Unmute,
    ToggleMute,
}

/// <summary>An action the parser decided on. It is executed by the platform layer.</summary>
/// <param name="Type">What to do.</param>
/// <param name="Target">Main parameter: query, URL, program path, media/volume command…</param>
/// <param name="Arguments">Secondary parameter: program arguments, volume amount, media player…</param>
/// <param name="Description">Human readable preview shown under the text field.</param>
/// <param name="App">The resolved application for <see cref="ActionType.OpenApp"/>.</param>
/// <param name="CommandName">Name of the custom command that produced this action, if any.</param>
public sealed record ResolvedAction(
    ActionType Type,
    string Target = "",
    string Arguments = "",
    string Description = "",
    AppEntry? App = null,
    string? CommandName = null)
{
    public static readonly ResolvedAction None = new(ActionType.None);
}

/// <summary>Describes an action type for the settings UI.</summary>
public sealed record ActionTypeInfo(
    ActionType Type,
    string DisplayName,
    string TargetLabel,
    string TargetHint,
    string ArgumentsLabel,
    string ArgumentsHint)
{
    public override string ToString() => DisplayName;

    /// <summary>Action types a user can pick for a custom command.</summary>
    public static IReadOnlyList<ActionTypeInfo> All { get; } =
    [
        new(ActionType.OpenUrl, "Open URL",
            "URL", "e.g. https://en.wikipedia.org/wiki/Special:Search?search={query}  ({query} is URL-encoded)",
            "", "Not used"),
        new(ActionType.WebSearch, "Web search (default engine)",
            "Search text", "Leave empty to search for {query}, or e.g. \"{query} recipe\"",
            "", "Not used"),
        new(ActionType.OpenApp, "Open application",
            "Application name", "Name as shown in the Start menu, e.g. \"Visual Studio Code\"",
            "", "Not used"),
        new(ActionType.RunProgram, "Run program / open file or folder",
            "Program, file or folder", "e.g. notepad.exe, C:\\Projects, %USERPROFILE%\\Downloads",
            "Arguments", "Optional, e.g. \"{query}\""),
        new(ActionType.ShellCommand, "Shell command (cmd)",
            "Command line", "e.g. shutdown /s /t 0   or   powershell -Command \"...\"",
            "Window", "Leave empty to run hidden, or type \"show\" to keep the console open"),
        new(ActionType.SpotifySearch, "Spotify search",
            "Search text", "Leave empty to search for {query}",
            "", "Not used"),
        new(ActionType.YouTubeSearch, "YouTube search",
            "Search text", "Leave empty to search for {query}",
            "", "Not used"),
        new(ActionType.Media, "Media control",
            "Command", "toggle, play, pause, next or previous",
            "Player", "spotify (default, falls back to any player) or any"),
        new(ActionType.Volume, "PC volume",
            "Command", "set, up, down, mute, unmute or togglemute",
            "Amount", "Percent for set/up/down, e.g. 30 or {query}. Empty = default step"),
        new(ActionType.OpenSettings, "Open Copilot Knappen settings",
            "", "Not used", "", "Not used"),
        new(ActionType.Exit, "Exit Copilot Knappen",
            "", "Not used", "", "Not used"),
    ];

    public static ActionTypeInfo For(ActionType type) =>
        All.FirstOrDefault(i => i.Type == type) ?? new(type, type.ToString(), "Target", "", "Arguments", "");
}

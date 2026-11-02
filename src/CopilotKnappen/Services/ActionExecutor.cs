using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using CopilotKnappen.Core;
using Microsoft.Win32;

namespace CopilotKnappen.Services;

/// <param name="Success">Whether the action ran.</param>
/// <param name="Message">Status text to show in the launcher (errors, new volume…).</param>
/// <param name="KeepOpen">Keep the launcher open, e.g. so "volume up" can be repeated with Enter.</param>
public sealed record ExecutionResult(bool Success, string? Message = null, bool KeepOpen = false)
{
    public static readonly ExecutionResult Done = new(true);
    public static ExecutionResult Fail(string message) => new(false, message, KeepOpen: true);
}

/// <summary>Runs <see cref="ResolvedAction"/>s on Windows.</summary>
public sealed class ActionExecutor
{
    private readonly Func<AppSettings> _settings;
    private readonly IAppCatalog _apps;
    private readonly MediaController _media = new();
    private readonly VolumeController _volume = new();

    public ActionExecutor(Func<AppSettings> settings, IAppCatalog apps)
    {
        _settings = settings;
        _apps = apps;
    }

    public async Task<ExecutionResult> ExecuteAsync(ResolvedAction action, string input)
    {
        try
        {
            switch (action.Type)
            {
                case ActionType.None:
                    return new ExecutionResult(false, KeepOpen: true);

                case ActionType.WebSearch:
                case ActionType.OpenUrl:
                    ShellOpen(action.Target);
                    return ExecutionResult.Done;

                case ActionType.OpenApp:
                    LaunchApp(action.App ?? new AppEntry(action.Target, action.Target, AppEntryKind.Executable));
                    return ExecutionResult.Done;

                case ActionType.RunProgram:
                    return RunProgram(action, input);

                case ActionType.ShellCommand:
                    RunShell(action.Target, show: action.Arguments.Equals("show", StringComparison.OrdinalIgnoreCase));
                    return ExecutionResult.Done;

                case ActionType.SpotifySearch:
                    SpotifySearch(action.Target);
                    return ExecutionResult.Done;

                case ActionType.YouTubeSearch:
                    ShellOpen("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(action.Target));
                    return ExecutionResult.Done;

                case ActionType.Media:
                    var command = IntentParserCommands.Media(action.Target);
                    var message = await _media.SendAsync(command, preferSpotify: action.Arguments != "any", LaunchSpotify);
                    return new ExecutionResult(true, message);

                case ActionType.Volume:
                    var amount = int.TryParse(action.Arguments, out var value) ? value : (int?)null;
                    var status = _volume.Apply(IntentParserCommands.Volume(action.Target), amount, _settings().VolumeStep);
                    return new ExecutionResult(true, status, KeepOpen: true);

                default:
                    return ExecutionResult.Fail($"Unsupported action: {action.Type}");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        {
            return ExecutionResult.Fail(ex.Message);
        }
    }

    private ExecutionResult RunProgram(ResolvedAction action, string input)
    {
        var target = ExpandPath(action.Target);
        try
        {
            Process.Start(new ProcessStartInfo(target, action.Arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            })?.Dispose();
            return ExecutionResult.Done;
        }
        catch (Win32Exception) when (action.CommandName is null)
        {
            // Guessed program ("open foo") that doesn't exist: search for it instead.
            var settings = _settings();
            ShellOpen(Templates.Expand(settings.SearchUrlTemplate, input, input, urlEncode: true));
            return ExecutionResult.Done;
        }
        catch (Win32Exception)
        {
            return ExecutionResult.Fail($"Couldn't find “{target}”.");
        }
    }

    private static void RunShell(string commandLine, bool show)
    {
        Process.Start(new ProcessStartInfo("cmd.exe", (show ? "/k " : "/c ") + commandLine)
        {
            UseShellExecute = false,
            CreateNoWindow = !show,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        })?.Dispose();
    }

    private void SpotifySearch(string query)
    {
        if (_settings().PreferSpotifyDesktop && IsSpotifyInstalled())
            ShellOpen("spotify:search:" + Uri.EscapeDataString(query));
        else
            ShellOpen("https://open.spotify.com/search/" + Uri.EscapeDataString(query));
    }

    private bool LaunchSpotify()
    {
        if (!IsSpotifyInstalled()) return false;
        ShellOpen("spotify:");
        return true;
    }

    private bool IsSpotifyInstalled()
    {
        if (_apps.Apps.Any(a => a.Name.Equals("Spotify", StringComparison.OrdinalIgnoreCase))) return true;
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey("spotify");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void LaunchApp(AppEntry app)
    {
        switch (app.Kind)
        {
            case AppEntryKind.AppsFolder:
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.LaunchTarget}") { UseShellExecute = true })?.Dispose();
                break;
            default:
                ShellOpen(ExpandPath(app.LaunchTarget));
                break;
        }
    }

    public static void ShellOpen(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    private static string ExpandPath(string path)
    {
        var p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (Regex.IsMatch(p, @"^~[\\/]")) p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]);
        return p;
    }
}

/// <summary>Maps the lowercase command names in <see cref="ResolvedAction.Target"/> back to enums.</summary>
internal static class IntentParserCommands
{
    public static MediaCommand Media(string value) =>
        Enum.TryParse<MediaCommand>(value, ignoreCase: true, out var c) ? c : MediaCommand.Toggle;

    public static VolumeCommand Volume(string value) =>
        Enum.TryParse<VolumeCommand>(value, ignoreCase: true, out var c) ? c : VolumeCommand.Set;
}

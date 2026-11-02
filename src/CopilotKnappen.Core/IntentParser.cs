using System.Globalization;
using System.Text.RegularExpressions;

namespace CopilotKnappen.Core;

/// <summary>
/// Turns free text typed into the launcher into an action. Order of precedence:
/// app commands (settings/exit) → custom commands → media → volume → Spotify / YouTube / search prefixes →
/// "open …" → URLs and paths → installed application names → web search.
/// </summary>
public sealed partial class IntentParser
{
    private readonly Func<AppSettings> _settings;
    private readonly IAppCatalog _apps;

    public IntentParser(Func<AppSettings> settings, IAppCatalog apps)
    {
        _settings = settings;
        _apps = apps;
    }

    public IntentParser(AppSettings settings, IAppCatalog apps) : this(() => settings, apps) { }

    private static readonly HashSet<string> SettingsWords =
        ["settings", "setting", "preferences", "prefs", "options", "config", "configure", "knappen settings", "/settings", ":settings"];

    private static readonly HashSet<string> ExitWords = ["exit", "quit", "/exit", ":q", "exit knappen", "quit knappen"];

    private static readonly HashSet<string> QuestionWords =
        ["what", "whats", "what's", "who", "whos", "who's", "why", "how", "hows", "how's", "when", "where", "which", "is", "are",
         "can", "could", "does", "do", "did", "should", "would", "will", "define", "meaning", "explain"];

    public ResolvedAction Parse(string input)
    {
        var text = CollapseWhitespace(input);
        if (text.Length == 0) return ResolvedAction.None;
        var lower = text.ToLowerInvariant();
        var settings = _settings();

        if (SettingsWords.Contains(lower))
            return new(ActionType.OpenSettings, Description: "Open Copilot Knappen settings");
        if (ExitWords.Contains(lower))
            return new(ActionType.Exit, Description: "Exit Copilot Knappen");

        return TryCustomCommand(text, settings)
               ?? TryMedia(lower)
               ?? TryVolume(lower, settings)
               ?? TrySpotify(text, lower)
               ?? TryYouTube(text, lower)
               ?? TrySearchPrefix(text, lower, settings)
               ?? TryOpenVerb(text, lower)
               ?? TryUrlOrPath(text)
               ?? TryApplication(text, lower, settings)
               ?? WebSearch(text, settings);
    }

    /// <summary>Treats the whole input as a web search (Shift+Enter).</summary>
    public ResolvedAction ParseAsWebSearch(string input)
    {
        var text = CollapseWhitespace(input);
        return text.Length == 0 ? ResolvedAction.None : WebSearch(text, _settings());
    }

    // ------------------------------------------------------------------ custom commands

    private ResolvedAction? TryCustomCommand(string text, AppSettings settings)
    {
        CustomCommand? best = null;
        string bestQuery = "";
        var bestLength = -1;

        foreach (var cmd in settings.CustomCommands)
        {
            if (!cmd.Enabled) continue;
            foreach (var keyword in cmd.KeywordList)
            {
                if (!TryMatch(cmd.Match, keyword, text, out var query)) continue;
                // The longest (most specific) keyword wins; custom commands are checked in list order on ties.
                if (keyword.Length > bestLength)
                {
                    best = cmd;
                    bestQuery = query;
                    bestLength = keyword.Length;
                }
            }
        }

        return best is null ? null : FromCustomCommand(best, bestQuery, text, settings);
    }

    internal static bool TryMatch(MatchMode mode, string keyword, string text, out string query)
    {
        query = "";
        switch (mode)
        {
            case MatchMode.Exact:
                return string.Equals(CollapseWhitespace(keyword), text, StringComparison.OrdinalIgnoreCase);

            case MatchMode.Regex:
                try
                {
                    var match = Regex.Match(text, keyword, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(100));
                    if (!match.Success) return false;
                    var group = match.Groups["query"];
                    query = group.Success ? group.Value.Trim() : text[(match.Index + match.Length)..].Trim();
                    return true;
                }
                catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
                {
                    return false;
                }

            default:
                var kw = CollapseWhitespace(keyword);
                if (kw.Length == 0) return false;
                if (string.Equals(kw, text, StringComparison.OrdinalIgnoreCase)) return true;
                if (text.Length > kw.Length && text.StartsWith(kw, StringComparison.OrdinalIgnoreCase) &&
                    (text[kw.Length] == ' ' || !char.IsLetterOrDigit(kw[^1])))
                {
                    query = text[kw.Length..].Trim();
                    return true;
                }
                return false;
        }
    }

    private ResolvedAction FromCustomCommand(CustomCommand cmd, string query, string input, AppSettings settings)
    {
        string Raw(string template) => Templates.Expand(template, query, input, urlEncode: false);
        string QueryOr(string template) => string.IsNullOrWhiteSpace(template) ? query : Raw(template);

        var action = cmd.Action switch
        {
            ActionType.OpenUrl => OpenUrl(Templates.Expand(cmd.Target, query, input, urlEncode: true)),
            ActionType.WebSearch => WebSearch(QueryOr(cmd.Target), settings),
            ActionType.OpenApp => OpenNamedApp(Raw(cmd.Target)),
            ActionType.RunProgram => RunProgram(Raw(cmd.Target), Raw(cmd.Arguments)),
            ActionType.ShellCommand => new ResolvedAction(ActionType.ShellCommand, Raw(cmd.Target), cmd.Arguments.Trim(),
                $"Run command: {Raw(cmd.Target)}"),
            ActionType.SpotifySearch => SpotifySearch(QueryOr(cmd.Target)),
            ActionType.YouTubeSearch => YouTubeSearch(QueryOr(cmd.Target)),
            ActionType.Media => Media(ParseMediaCommand(cmd.Target),
                string.IsNullOrWhiteSpace(cmd.Arguments) ? "spotify" : cmd.Arguments.Trim().ToLowerInvariant()),
            ActionType.Volume => VolumeFromCustom(cmd, Raw(cmd.Arguments), settings),
            ActionType.OpenSettings => new ResolvedAction(ActionType.OpenSettings, Description: "Open Copilot Knappen settings"),
            ActionType.Exit => new ResolvedAction(ActionType.Exit, Description: "Exit Copilot Knappen"),
            _ => WebSearch(input, settings),
        };

        return action with
        {
            CommandName = cmd.Name,
            Description = string.IsNullOrWhiteSpace(cmd.Name) ? action.Description : $"{cmd.Name} — {action.Description}",
        };
    }

    private static ResolvedAction VolumeFromCustom(CustomCommand cmd, string amount, AppSettings settings)
    {
        var command = ParseVolumeCommand(cmd.Target);
        var digits = new string(amount.Where(char.IsDigit).ToArray());
        int? value = int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
        return command switch
        {
            VolumeCommand.Set => Volume(VolumeCommand.Set, value ?? 50),
            VolumeCommand.Up or VolumeCommand.Down => Volume(command, value ?? settings.VolumeStep),
            _ => Volume(command, null),
        };
    }

    // ------------------------------------------------------------------ media

    private static readonly Dictionary<string, MediaCommand> MediaVerbs = new()
    {
        ["play"] = MediaCommand.Play, ["resume"] = MediaCommand.Play, ["unpause"] = MediaCommand.Play,
        ["continue"] = MediaCommand.Play, ["start"] = MediaCommand.Play,
        ["pause"] = MediaCommand.Pause, ["stop"] = MediaCommand.Pause,
        ["next"] = MediaCommand.Next, ["skip"] = MediaCommand.Next, ["forward"] = MediaCommand.Next,
        ["previous"] = MediaCommand.Previous, ["prev"] = MediaCommand.Previous, ["back"] = MediaCommand.Previous,
        ["last"] = MediaCommand.Previous, ["go back"] = MediaCommand.Previous, ["skip back"] = MediaCommand.Previous,
        ["toggle"] = MediaCommand.Toggle, ["play/pause"] = MediaCommand.Toggle, ["play pause"] = MediaCommand.Toggle,
        ["playpause"] = MediaCommand.Toggle, ["play-pause"] = MediaCommand.Toggle,
    };

    private static readonly string[] MediaObjects =
        ["", "song", "track", "music", "it", "playback", "spotify", "the song", "the track", "the music", "this song", "this track", "a song"];

    private ResolvedAction? TryMedia(string lower)
    {
        var phrase = lower.TrimEnd('.', '!');
        var spotifyExplicit = false;
        if (phrase.StartsWith("spotify "))
        {
            phrase = phrase["spotify ".Length..];
            spotifyExplicit = true;
        }
        if (phrase.EndsWith(" on spotify"))
        {
            phrase = phrase[..^" on spotify".Length];
            spotifyExplicit = true;
        }
        if (phrase.EndsWith(" in spotify"))
        {
            phrase = phrase[..^" in spotify".Length];
            spotifyExplicit = true;
        }

        foreach (var (verb, command) in MediaVerbs.OrderByDescending(kv => kv.Key.Length))
        {
            if (phrase != verb && !phrase.StartsWith(verb + " ")) continue;
            var rest = phrase[verb.Length..].Trim();
            // "start" and "last" are too ambiguous on their own ("start" might mean start menu) unless an object follows.
            if (rest.Length == 0 && verb is "start" or "last" or "continue" or "forward" && !spotifyExplicit) return null;
            if (verb == "start" && rest == "spotify") return null; // "start spotify" opens the app
            if (MediaObjects.Contains(rest)) return Media(command, "spotify");
        }
        return null;
    }

    private static ResolvedAction Media(MediaCommand command, string player)
    {
        var what = command switch
        {
            MediaCommand.Play => "Play / resume",
            MediaCommand.Pause => "Pause",
            MediaCommand.Next => "Next track",
            MediaCommand.Previous => "Previous track",
            _ => "Play / pause",
        };
        var who = player == "any" ? "current media" : "Spotify";
        return new ResolvedAction(ActionType.Media, command.ToString().ToLowerInvariant(), player, $"{what} ({who})");
    }

    // ------------------------------------------------------------------ volume

    [GeneratedRegex(@"^(?:set |change )?(?:the )?(?:volume|vol|sound)(?: to| at| level)?\s*[:=]?\s*(\d{1,3})\s*%?$")]
    private static partial Regex VolumeSetRegex();

    [GeneratedRegex(@"^(\d{1,3})\s*%?\s*(?:volume|vol)$")]
    private static partial Regex VolumeSetSuffixRegex();

    [GeneratedRegex(@"^(?:volume|vol|sound)\s*(up|down|\+|-|higher|lower)(?:\s*(?:by\s*)?(\d{1,3})\s*%?)?$")]
    private static partial Regex VolumeStepRegex();

    [GeneratedRegex(@"^turn (?:it |the (?:volume|sound|music) )?(up|down)(?: (?:the )?(?:volume|sound|music))?(?: (?:by )?(\d{1,3})\s*%?)?$")]
    private static partial Regex TurnRegex();

    [GeneratedRegex(@"^(increase|raise|lower|decrease|reduce)(?: the)? (?:volume|sound)(?: by (\d{1,3})\s*%?)?$")]
    private static partial Regex VerbVolumeRegex();

    private static ResolvedAction? TryVolume(string lower, AppSettings settings)
    {
        var s = lower.TrimEnd('.', '!');
        switch (s)
        {
            case "mute" or "mute volume" or "mute sound" or "mute pc" or "mute audio" or "silence" or "volume mute" or "volume off":
                return Volume(VolumeCommand.Mute, null);
            case "unmute" or "unmute volume" or "unmute sound" or "unmute pc" or "unmute audio" or "volume unmute" or "volume on":
                return Volume(VolumeCommand.Unmute, null);
            case "toggle mute" or "mute/unmute":
                return Volume(VolumeCommand.ToggleMute, null);
            case "louder" or "volume+" or "vol+" or "more volume":
                return Volume(VolumeCommand.Up, settings.VolumeStep);
            case "quieter" or "softer" or "volume-" or "vol-" or "less volume":
                return Volume(VolumeCommand.Down, settings.VolumeStep);
            case "max volume" or "volume max" or "full volume" or "volume full":
                return Volume(VolumeCommand.Set, 100);
            case "volume" or "vol" or "sound settings":
                return new ResolvedAction(ActionType.OpenUrl, "ms-settings:sound", Description: "Open sound settings");
        }

        Match m;
        if ((m = VolumeSetRegex().Match(s)).Success || (m = VolumeSetSuffixRegex().Match(s)).Success)
            return Volume(VolumeCommand.Set, Math.Clamp(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 0, 100));

        if ((m = VolumeStepRegex().Match(s)).Success || (m = TurnRegex().Match(s)).Success)
        {
            var up = m.Groups[1].Value is "up" or "+" or "higher";
            return Volume(up ? VolumeCommand.Up : VolumeCommand.Down, StepOrDefault(m.Groups[2], settings));
        }

        if ((m = VerbVolumeRegex().Match(s)).Success)
        {
            var up = m.Groups[1].Value is "increase" or "raise";
            return Volume(up ? VolumeCommand.Up : VolumeCommand.Down, StepOrDefault(m.Groups[2], settings));
        }

        return null;
    }

    private static int StepOrDefault(Group group, AppSettings settings) =>
        group.Success && int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v)
            ? Math.Clamp(v, 1, 100)
            : settings.VolumeStep;

    private static ResolvedAction Volume(VolumeCommand command, int? amount)
    {
        var description = command switch
        {
            VolumeCommand.Set => $"Set volume to {amount}%",
            VolumeCommand.Up => $"Volume up {amount}%",
            VolumeCommand.Down => $"Volume down {amount}%",
            VolumeCommand.Mute => "Mute volume",
            VolumeCommand.Unmute => "Unmute volume",
            _ => "Toggle mute",
        };
        return new ResolvedAction(ActionType.Volume, command.ToString().ToLowerInvariant(),
            amount?.ToString(CultureInfo.InvariantCulture) ?? "", description);
    }

    // ------------------------------------------------------------------ spotify / youtube / search

    private ResolvedAction? TrySpotify(string text, string lower)
    {
        if (lower is "spotify" or "open spotify" or "launch spotify" or "start spotify")
        {
            var (app, score) = AppMatcher.FindBest(_apps.Apps, "spotify");
            return app is not null && score >= AppMatcher.ImplicitThreshold
                ? OpenApp(app)
                : new ResolvedAction(ActionType.OpenUrl, "spotify:", Description: "Open Spotify");
        }

        // "play x on youtube" goes to YouTube.
        if (StripPrefix(text, "play ") is { } p && StripSuffix(p, " on youtube") is { } yt && yt.Length > 0)
            return YouTubeSearch(yt);

        foreach (var prefix in new[] { "spotify search ", "search spotify for ", "search spotify ", "spotify ", "play ", "listen to ", "put on ", "sp " })
        {
            if (StripPrefix(text, prefix) is { } rest && rest.Length > 0)
            {
                rest = StripSuffix(rest, " on spotify") ?? StripSuffix(rest, " in spotify") ?? rest;
                return SpotifySearch(rest);
            }
        }

        if ((StripSuffix(text, " on spotify") ?? StripSuffix(text, " in spotify")) is { Length: > 0 } q)
            return SpotifySearch(q);

        return null;
    }

    private static ResolvedAction SpotifySearch(string query) =>
        new(ActionType.SpotifySearch, query.Trim(), Description: $"Search Spotify for “{query.Trim()}”");

    private static ResolvedAction? TryYouTube(string text, string lower)
    {
        if (lower is "youtube" or "yt")
            return new ResolvedAction(ActionType.OpenUrl, "https://www.youtube.com", Description: "Open YouTube");

        foreach (var prefix in new[] { "youtube search ", "search youtube for ", "search youtube ", "youtube ", "yt ", "watch " })
        {
            if (StripPrefix(text, prefix) is { Length: > 0 } rest)
            {
                rest = StripPrefix(rest, "search for ") ?? StripPrefix(rest, "search ") ?? rest;
                rest = StripSuffix(rest, " on youtube") ?? rest;
                return YouTubeSearch(rest);
            }
        }

        if ((StripSuffix(text, " on youtube") ?? StripSuffix(text, " on yt")) is { Length: > 0 } q)
            return YouTubeSearch(StripPrefix(q, "search for ") ?? StripPrefix(q, "watch ") ?? q);

        return null;
    }

    private static ResolvedAction YouTubeSearch(string query) =>
        new(ActionType.YouTubeSearch, query.Trim(), Description: $"Search YouTube for “{query.Trim()}”");

    private static readonly (string Prefix, string? Template, string Engine)[] SearchPrefixes =
    [
        ("search the web for ", null, ""),
        ("search for ", null, ""),
        ("search ", null, ""),
        ("look up ", null, ""),
        ("lookup ", null, ""),
        ("? ", null, ""),
        ("google ", "https://www.google.com/search?q={query}", "Google"),
        ("bing ", "https://www.bing.com/search?q={query}", "Bing"),
        ("ddg ", "https://duckduckgo.com/?q={query}", "DuckDuckGo"),
        ("duckduckgo ", "https://duckduckgo.com/?q={query}", "DuckDuckGo"),
    ];

    private static ResolvedAction? TrySearchPrefix(string text, string lower, AppSettings settings)
    {
        if (lower == "google") return OpenUrl("https://www.google.com");

        foreach (var (prefix, template, engine) in SearchPrefixes)
        {
            if (StripPrefix(text, prefix) is not { Length: > 0 } q) continue;
            if (template is null) return WebSearch(q, settings);
            return new ResolvedAction(ActionType.OpenUrl, Templates.Expand(template, q, text, urlEncode: true),
                Description: $"Search {engine} for “{q}”");
        }
        return null;
    }

    private static ResolvedAction WebSearch(string query, AppSettings settings)
    {
        var template = string.IsNullOrWhiteSpace(settings.SearchUrlTemplate) ? AppSettings.GoogleSearch : settings.SearchUrlTemplate;
        if (!template.Contains("{query", StringComparison.Ordinal)) template += "{query}";
        return new ResolvedAction(ActionType.WebSearch, Templates.Expand(template, query.Trim(), query, urlEncode: true),
            query.Trim(), $"Search the web for “{query.Trim()}”");
    }

    // ------------------------------------------------------------------ open / urls / apps

    private ResolvedAction? TryOpenVerb(string text, string lower)
    {
        foreach (var verb in new[] { "open ", "launch ", "start ", "run " })
        {
            if (StripPrefix(text, verb) is not { Length: > 0 } target) continue;

            if (TryUrlOrPath(target) is { } direct) return direct;

            var (app, score) = AppMatcher.FindBest(_apps.Apps, target);
            if (app is not null && score >= AppMatcher.ExplicitThreshold) return OpenApp(app);

            // A single word that isn't a known app is probably a program on PATH ("run regedit").
            if (!target.Contains(' ')) return RunProgram(target, "");

            // "start a business" etc.: leave it to the web search.
            return null;
        }
        return null;
    }

    [GeneratedRegex(@"^(?:(?:https?|ftp)://\S+|www\.\S+\.\S+|localhost(?::\d+)?(?:/\S*)?|(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?(?:/\S*)?|(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(?:com|org|net|io|dev|app|ai|no|se|dk|fi|is|de|uk|co|us|eu|fr|es|it|nl|be|ch|at|pl|ru|jp|cn|in|br|ca|au|me|tv|gg|xyz|info|edu|gov|ly|so|to|fm|sh|gl|gd|page|site|online|tech|blog|news)(?::\d+)?(?:[/?#]\S*)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^(?:ms-[a-z0-9-]+|mailto|spotify|steam|tel|discord|slack|zoommtg|msteams|vscode|calculator|onenote|outlook|ftp|file):\S*$", RegexOptions.IgnoreCase)]
    private static partial Regex UriSchemeRegex();

    [GeneratedRegex(@"^(?:[a-z]:[\\/]|\\\\|%[a-z_]+%|~[\\/]|\.{1,2}[\\/])", RegexOptions.IgnoreCase)]
    private static partial Regex PathRegex();

    private static ResolvedAction? TryUrlOrPath(string text)
    {
        var t = text.Trim().Trim('"');
        if (PathRegex().IsMatch(t)) return RunProgram(t, "");
        if (t.Contains(' ')) return null;
        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return RunProgram(t, "");
        if (UriSchemeRegex().IsMatch(t)) return new ResolvedAction(ActionType.OpenUrl, t, Description: $"Open {t}");
        if (UrlRegex().IsMatch(t)) return OpenUrl(t);
        return null;
    }

    private ResolvedAction? TryApplication(string text, string lower, AppSettings settings)
    {
        if (!settings.DetectApplications) return null;
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 4 || text.EndsWith('?') || (words.Length > 1 && QuestionWords.Contains(words[0]))) return null;

        var (app, score) = AppMatcher.FindBest(_apps.Apps, text);
        if (app is null) return null;
        var required = words.Length >= 3 ? 0.95 : AppMatcher.ImplicitThreshold;
        return score >= required ? OpenApp(app) : null;
    }

    private ResolvedAction OpenNamedApp(string name)
    {
        if (TryUrlOrPath(name) is { } direct) return direct;
        var (app, score) = AppMatcher.FindBest(_apps.Apps, name);
        return app is not null && score >= AppMatcher.ExplicitThreshold ? OpenApp(app) : RunProgram(name, "");
    }

    private static ResolvedAction OpenApp(AppEntry app) =>
        new(ActionType.OpenApp, app.LaunchTarget, Description: $"Open {app.Name}", App: app);

    private static ResolvedAction OpenUrl(string url)
    {
        var u = url.Trim();
        if (!Regex.IsMatch(u, @"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase) || Regex.IsMatch(u, @"^localhost:\d", RegexOptions.IgnoreCase))
            u = (u.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ? "http://" : "https://") + u;
        return new ResolvedAction(ActionType.OpenUrl, u, Description: $"Open {u}");
    }

    private static ResolvedAction RunProgram(string target, string arguments)
    {
        var t = target.Trim();
        var a = arguments.Trim();
        return new ResolvedAction(ActionType.RunProgram, t, a, a.Length > 0 ? $"Run {t} {a}" : $"Open {t}");
    }

    // ------------------------------------------------------------------ helpers

    private static string? StripPrefix(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? text[prefix.Length..].Trim() : null;

    private static string? StripSuffix(string text, string suffix) =>
        text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? text[..^suffix.Length].Trim() : null;

    internal static string CollapseWhitespace(string text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static MediaCommand ParseMediaCommand(string value) =>
        value.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "") switch
        {
            "play" or "resume" => MediaCommand.Play,
            "pause" or "stop" => MediaCommand.Pause,
            "next" or "skip" => MediaCommand.Next,
            "previous" or "prev" or "back" => MediaCommand.Previous,
            _ => MediaCommand.Toggle,
        };

    internal static VolumeCommand ParseVolumeCommand(string value) =>
        value.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "") switch
        {
            "up" or "+" or "increase" or "louder" => VolumeCommand.Up,
            "down" or "decrease" or "quieter" or "lower" => VolumeCommand.Down,
            "mute" => VolumeCommand.Mute,
            "unmute" => VolumeCommand.Unmute,
            "togglemute" or "toggle" => VolumeCommand.ToggleMute,
            _ => VolumeCommand.Set,
        };
}

public static class Templates
{
    /// <summary>
    /// Replaces placeholders: {query} (URL-encoded when <paramref name="urlEncode"/> is true),
    /// {query:raw}, {query:url} and {input} (the whole prompt).
    /// </summary>
    public static string Expand(string template, string query, string input, bool urlEncode)
    {
        if (string.IsNullOrEmpty(template)) return "";
        var encoded = Uri.EscapeDataString(query);
        return template
            .Replace("{query:raw}", query, StringComparison.OrdinalIgnoreCase)
            .Replace("{query:url}", encoded, StringComparison.OrdinalIgnoreCase)
            .Replace("{input}", urlEncode ? Uri.EscapeDataString(input) : input, StringComparison.OrdinalIgnoreCase)
            .Replace("{query}", urlEncode ? encoded : query, StringComparison.OrdinalIgnoreCase);
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotKnappen.Core;

public enum MatchMode
{
    /// <summary>The prompt starts with the keyword; the rest becomes {query}.</summary>
    StartsWith,
    /// <summary>The whole prompt must equal the keyword.</summary>
    Exact,
    /// <summary>The keyword is a regular expression. A named group "query" becomes {query}.</summary>
    Regex,
}

/// <summary>A user defined command: keywords that map to an action with parameters.</summary>
public sealed class CustomCommand : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _name = "New command";
    private string _keywords = "";
    private MatchMode _match = MatchMode.StartsWith;
    private ActionType _action = ActionType.OpenUrl;
    private string _target = "";
    private string _arguments = "";

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Comma separated keywords, e.g. "wiki, wikipedia".</summary>
    public string Keywords { get => _keywords; set => Set(ref _keywords, value); }
    public MatchMode Match { get => _match; set => Set(ref _match, value); }
    public ActionType Action { get => _action; set => Set(ref _action, value); }

    /// <summary>Main parameter. Supports {query}, {query:raw}, {query:url} and {input} placeholders.</summary>
    public string Target { get => _target; set => Set(ref _target, value); }

    /// <summary>Secondary parameter. Supports the same placeholders as <see cref="Target"/>.</summary>
    public string Arguments { get => _arguments; set => Set(ref _arguments, value); }

    private static readonly char[] KeywordSeparators = [',', ';', '\n'];
    private static readonly char[] RegexSeparators = ['\n'];

    [JsonIgnore]
    public IReadOnlyList<string> KeywordList =>
        Keywords.Split(Match == MatchMode.Regex ? RegexSeparators : KeywordSeparators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => k.Length > 0)
            .ToList();

    [JsonIgnore]
    public string Summary => $"{(string.IsNullOrWhiteSpace(Keywords) ? "(no keywords)" : Keywords)}  →  {ActionTypeInfo.For(Action).DisplayName}";

    public CustomCommand Clone() => new()
    {
        Enabled = Enabled, Name = Name, Keywords = Keywords, Match = Match,
        Action = Action, Target = Target, Arguments = Arguments,
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(Keywords) or nameof(Action))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
    }
}

public sealed class AppSettings
{
    public const string GoogleSearch = "https://www.google.com/search?q={query}";

    public static IReadOnlyList<string> SearchEnginePresets { get; } =
    [
        GoogleSearch,
        "https://www.bing.com/search?q={query}",
        "https://duckduckgo.com/?q={query}",
        "https://search.brave.com/search?q={query}",
        "https://www.ecosia.org/search?q={query}",
    ];

    /// <summary>URL template for web searches. {query} is replaced by the URL-encoded text.</summary>
    public string SearchUrlTemplate { get; set; } = GoogleSearch;

    /// <summary>Volume step in percent for "volume up/down".</summary>
    public int VolumeStep { get; set; } = 10;

    /// <summary>Also open the launcher with a keyboard shortcut (for PCs without a Copilot key).</summary>
    public bool EnableHotkey { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>Open Spotify searches in the desktop app when it is installed (otherwise the web player).</summary>
    public bool PreferSpotifyDesktop { get; set; } = true;

    /// <summary>Keep running in the background (tray icon) so the next key press is instant.</summary>
    public bool KeepRunningInBackground { get; set; } = true;

    /// <summary>Open applications when the prompt looks like an app name.</summary>
    public bool DetectApplications { get; set; } = true;

    public List<CustomCommand> CustomCommands { get; set; } = [];

    public AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        clone.CustomCommands = CustomCommands.Select(c => c.Clone()).ToList();
        return clone;
    }

    public static AppSettings CreateDefault() => new()
    {
        CustomCommands =
        [
            new() { Name = "Wikipedia", Keywords = "wiki, wikipedia", Action = ActionType.OpenUrl,
                    Target = "https://en.wikipedia.org/wiki/Special:Search?search={query}" },
            new() { Name = "Google Maps", Keywords = "maps, map, directions", Action = ActionType.OpenUrl,
                    Target = "https://www.google.com/maps/search/{query}" },
            new() { Name = "GitHub search", Keywords = "gh, github", Action = ActionType.OpenUrl,
                    Target = "https://github.com/search?q={query}" },
            new() { Name = "Translate", Keywords = "translate", Action = ActionType.OpenUrl,
                    Target = "https://translate.google.com/?sl=auto&tl=en&text={query}" },
            new() { Name = "Lock PC", Keywords = "lock", Match = MatchMode.Exact, Action = ActionType.RunProgram,
                    Target = "rundll32.exe", Arguments = "user32.dll,LockWorkStation" },
            new() { Name = "Downloads folder", Keywords = "downloads", Match = MatchMode.Exact, Action = ActionType.RunProgram,
                    Target = "%USERPROFILE%\\Downloads" },
            new() { Name = "Quiet mode", Keywords = "quiet", Match = MatchMode.Exact, Action = ActionType.Volume,
                    Target = "set", Arguments = "15" },
        ],
    };
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    public SettingsStore(string filePath) => FilePath = filePath;

    public string FilePath { get; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotKnappen", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (settings is not null)
                {
                    settings.CustomCommands ??= [];
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep a copy of the broken file so the user doesn't lose their commands silently.
            try { File.Copy(FilePath, FilePath + ".broken", overwrite: true); } catch { /* ignored */ }
        }

        var defaults = AppSettings.CreateDefault();
        try { Save(defaults); } catch { /* read-only location; run with defaults */ }
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using CopilotKnappen.Core;
using CopilotKnappen.Services;

namespace CopilotKnappen.Views;

public sealed record BuiltInExample(string Example, string Meaning);

/// <summary>Settings panel: custom commands (keywords → action + parameters) and general options.</summary>
public partial class SettingsWindow : Window
{
    private static readonly BuiltInExample[] BuiltIns =
    [
        new("how do magnets work", "Any sentence or unknown text → search in your default browser"),
        new("search … / google … / bing … / ddg …", "Force a web search (Shift+Enter does the same)"),
        new("notepad · chrome · vscode · discord", "Open an installed application (Start-menu name, fuzzy)"),
        new("open … / launch … / run …", "Open an app, program, file or URL explicitly"),
        new("github.com · C:\\Users · %APPDATA%", "Open a website, folder or file"),
        new("play <song> · spotify <artist> · <x> on spotify", "Search on Spotify"),
        new("spotify", "Open Spotify"),
        new("play · pause · skip · next · previous", "Control Spotify (or the current media player)"),
        new("spotify pause · next song · play/pause", "More ways to control playback"),
        new("volume 40 · vol up · vol down 5 · louder", "Set or change the PC volume"),
        new("mute · unmute · max volume", "Mute / unmute the PC"),
        new("youtube <x> · yt <x> · watch <x> · <x> on youtube", "Search on YouTube"),
        new("settings", "Open this window"),
        new("exit", "Close Copilot Knappen (also in the tray icon menu)"),
        new("↑ / ↓ · Tab · Esc", "Prompt history · complete app name · close"),
    ];

    private readonly AppSettings _draft;
    private readonly ObservableCollection<CustomCommand> _commands;
    private readonly IntentParser _testParser;
    private CustomCommand? _selected;

    public SettingsWindow()
    {
        InitializeComponent();

        _draft = App.Current.Settings.Clone();
        _commands = new ObservableCollection<CustomCommand>(_draft.CustomCommands);
        _testParser = new IntentParser(() =>
        {
            _draft.CustomCommands = _commands.ToList();
            return _draft;
        }, App.Current.Catalog);

        CommandList.ItemsSource = _commands;
        MatchCombo.ItemsSource = Enum.GetValues<MatchMode>();
        ActionCombo.ItemsSource = ActionTypeInfo.All;
        SearchEngineCombo.ItemsSource = AppSettings.SearchEnginePresets;
        GeneralPanel.DataContext = _draft;
        BuiltInList.ItemsSource = BuiltIns;

        SettingsPathText.Text = $"Settings file: {App.Current.Store.FilePath}";
        UpdateAppCount();
        App.Current.Catalog.Refreshed += OnCatalogRefreshed;
        Closed += (_, _) =>
        {
            App.Current.Catalog.Refreshed -= OnCatalogRefreshed;
            Select(null);
        };

        if (_commands.Count > 0) CommandList.SelectedIndex = 0;
        else Select(null);
    }

    private void OnCatalogRefreshed() => Dispatcher.BeginInvoke(UpdateAppCount);

    private void UpdateAppCount() => AppCountText.Text = $"Indexed applications: {App.Current.Catalog.Apps.Count}";

    // ------------------------------------------------------------------ command list

    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Select(CommandList.SelectedItem as CustomCommand);

    private void Select(CustomCommand? command)
    {
        if (_selected is not null) _selected.PropertyChanged -= Selected_PropertyChanged;
        _selected = command;
        if (_selected is not null) _selected.PropertyChanged += Selected_PropertyChanged;

        Editor.DataContext = command;
        EditorFields.Visibility = command is null ? Visibility.Collapsed : Visibility.Visible;
        NoSelection.Visibility = command is null ? Visibility.Visible : Visibility.Collapsed;
        UpdateHints();
        UpdateTest();
    }

    private void Selected_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CustomCommand.Action) or nameof(CustomCommand.Match)) UpdateHints();
        UpdateTest();
    }

    private void UpdateHints()
    {
        if (_selected is null) return;
        var info = ActionTypeInfo.For(_selected.Action);

        TargetPanel.Visibility = string.IsNullOrEmpty(info.TargetLabel) ? Visibility.Collapsed : Visibility.Visible;
        TargetLabel.Text = info.TargetLabel;
        TargetHint.Text = info.TargetHint;

        ArgumentsPanel.Visibility = string.IsNullOrEmpty(info.ArgumentsLabel) ? Visibility.Collapsed : Visibility.Visible;
        ArgumentsLabel.Text = info.ArgumentsLabel;
        ArgumentsHint.Text = info.ArgumentsHint;

        KeywordsHint.Text = _selected.Match switch
        {
            MatchMode.Exact => "Comma separated. The whole prompt must equal one of the keywords, e.g. “lock”.",
            MatchMode.Regex => "One regular expression per line (case-insensitive). A named group (?<query>…) becomes {query}, e.g. ^weather (?:in )?(?<query>.+)$",
            _ => "Comma separated, e.g. “wiki, wikipedia”. Typing “wiki new york” runs this command with {query} = “new york”.",
        };
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var command = new CustomCommand { Name = "New command", Keywords = "", Action = ActionType.OpenUrl, Target = "https://example.com/search?q={query}" };
        _commands.Add(command);
        CommandList.SelectedItem = command;
        CommandList.ScrollIntoView(command);
        KeywordsBox.Focus();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var copy = _selected.Clone();
        copy.Name += " (copy)";
        _commands.Insert(_commands.IndexOf(_selected) + 1, copy);
        CommandList.SelectedItem = copy;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var index = _commands.IndexOf(_selected);
        _commands.Remove(_selected);
        if (_commands.Count > 0) CommandList.SelectedIndex = Math.Min(index, _commands.Count - 1);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int delta)
    {
        if (_selected is null) return;
        var item = _selected;
        var index = _commands.IndexOf(item);
        var target = index + delta;
        if (target < 0 || target >= _commands.Count) return;
        _commands.Move(index, target);
        CommandList.SelectedItem = item;
    }

    // ------------------------------------------------------------------ test box

    private void TestBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateTest();

    private void UpdateTest()
    {
        if (TestBox is null || TestResult is null) return;
        if (string.IsNullOrWhiteSpace(TestBox.Text))
        {
            TestResult.Text = "";
            return;
        }
        var action = _testParser.Parse(TestBox.Text);
        var detail = action.Type switch
        {
            ActionType.WebSearch or ActionType.OpenUrl => action.Target,
            ActionType.RunProgram => $"{action.Target} {action.Arguments}".Trim(),
            ActionType.OpenApp => action.App?.LaunchTarget ?? action.Target,
            _ => "",
        };
        TestResult.Text = $"→ {action.Description}" + (detail.Length > 0 && detail != action.Description ? $"\n   {detail}" : "");
    }

    // ------------------------------------------------------------------ general tab

    private void OpenCopilotKeySettings_Click(object sender, RoutedEventArgs e) =>
        TryOpen("ms-settings:personalization-textinput");

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) =>
        TryOpen(Path.GetDirectoryName(App.Current.Store.FilePath)!);

    private void Reindex_Click(object sender, RoutedEventArgs e)
    {
        AppCountText.Text = "Indexing applications…";
        App.Current.Catalog.RefreshInBackground(force: true);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var existing = new HashSet<string>(_commands.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var command in AppSettings.CreateDefault().CustomCommands.Where(c => !existing.Contains(c.Name)))
            _commands.Add(command);
    }

    private void TryOpen(string target)
    {
        try
        {
            ActionExecutor.ShellOpen(target);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    // ------------------------------------------------------------------ save / cancel

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Commit a search engine typed into the editable combo box that still has focus.
        _draft.SearchUrlTemplate = string.IsNullOrWhiteSpace(SearchEngineCombo.Text) ? AppSettings.GoogleSearch : SearchEngineCombo.Text.Trim();

        foreach (var command in _commands.Where(c => c.Enabled && c.Match == MatchMode.Regex))
        {
            foreach (var pattern in command.KeywordList)
            {
                try
                {
                    _ = new Regex(pattern);
                }
                catch (ArgumentException ex)
                {
                    CommandList.SelectedItem = command;
                    StatusText.Text = $"“{command.Name}” has an invalid regular expression: {ex.Message}";
                    return;
                }
            }
        }

        if (_draft.EnableHotkey && !HotkeyManager.TryParse(_draft.Hotkey, out _, out _))
        {
            StatusText.Text = $"Couldn't understand the shortcut “{_draft.Hotkey}”. Use something like Ctrl+Alt+Space.";
            return;
        }

        _draft.CustomCommands = _commands.ToList();
        try
        {
            App.Current.SaveSettings(_draft);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Couldn't save settings: {ex.Message}";
            return;
        }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}

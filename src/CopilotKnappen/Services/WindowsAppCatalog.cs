using System.IO;
using CopilotKnappen.Core;

namespace CopilotKnappen.Services;

/// <summary>
/// Index of installed applications: everything in shell:AppsFolder (classic and Store apps, i.e. what the
/// Start menu shows), Start-menu shortcuts as a fallback, plus a few well-known system tools.
/// </summary>
public sealed class WindowsAppCatalog : IAppCatalog
{
    private static readonly AppEntry[] BuiltIns =
    [
        new("Windows Settings", "ms-settings:", AppEntryKind.Uri),
        new("Sound settings", "ms-settings:sound", AppEntryKind.Uri),
        new("Bluetooth settings", "ms-settings:bluetooth", AppEntryKind.Uri),
        new("Display settings", "ms-settings:display", AppEntryKind.Uri),
        new("Wi-Fi settings", "ms-settings:network-wifi", AppEntryKind.Uri),
        new("Windows Update", "ms-settings:windowsupdate", AppEntryKind.Uri),
        new("File Explorer", "explorer.exe", AppEntryKind.Executable),
        new("Explorer", "explorer.exe", AppEntryKind.Executable),
        new("Task Manager", "taskmgr.exe", AppEntryKind.Executable),
        new("Command Prompt", "cmd.exe", AppEntryKind.Executable),
        new("cmd", "cmd.exe", AppEntryKind.Executable),
        new("PowerShell", "powershell.exe", AppEntryKind.Executable),
        new("Terminal", "wt.exe", AppEntryKind.Executable),
        new("Control Panel", "control.exe", AppEntryKind.Executable),
        new("Registry Editor", "regedit.exe", AppEntryKind.Executable),
        new("Notepad", "notepad.exe", AppEntryKind.Executable),
        new("Paint", "mspaint.exe", AppEntryKind.Executable),
        new("Calculator", "calc.exe", AppEntryKind.Executable),
    ];

    private volatile IReadOnlyList<AppEntry> _apps = BuiltIns;
    private DateTime _lastRefresh = DateTime.MinValue;
    private int _refreshing;

    public IReadOnlyList<AppEntry> Apps => _apps;

    public event Action? Refreshed;

    /// <summary>Re-indexes on a background STA thread (Shell COM objects need STA).</summary>
    public void RefreshInBackground(bool force = false)
    {
        if (!force && DateTime.UtcNow - _lastRefresh < TimeSpan.FromMinutes(5)) return;
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;

        var thread = new Thread(() =>
        {
            try
            {
                _apps = Load();
                _lastRefresh = DateTime.UtcNow;
                Refreshed?.Invoke();
            }
            catch
            {
                // Keep the previous index.
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        })
        { IsBackground = true, Name = "App index" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static List<AppEntry> Load()
    {
        var result = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(AppEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.LaunchTarget)) return;
            if (seen.Add(entry.Name)) result.Add(entry);
        }

        foreach (var entry in LoadAppsFolder()) Add(entry);
        foreach (var entry in LoadStartMenuShortcuts()) Add(entry);
        foreach (var entry in BuiltIns) Add(entry);
        return result;
    }

    private static IEnumerable<AppEntry> LoadAppsFolder()
    {
        var list = new List<AppEntry>();
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return list;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic? folder = shell.NameSpace("shell:AppsFolder");
            if (folder is null) return list;
            foreach (dynamic item in folder.Items())
            {
                string name = item.Name;
                string path = item.Path;
                if (string.IsNullOrWhiteSpace(path) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new AppEntry(name, path, AppEntryKind.AppsFolder));
            }
        }
        catch
        {
            // Shell unavailable (e.g. Windows Sandbox without explorer); fall back to shortcuts.
        }
        return list;
    }

    private static IEnumerable<AppEntry> LoadStartMenuShortcuts()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };
        var list = new List<AppEntry>();
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                foreach (var file in Directory.EnumerateFiles(root, "*.lnk", options))
                    list.Add(new AppEntry(Path.GetFileNameWithoutExtension(file), file, AppEntryKind.Shortcut));
            }
            catch
            {
                // Inaccessible folder.
            }
        }
        return list;
    }
}

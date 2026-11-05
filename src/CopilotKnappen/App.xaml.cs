using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CopilotKnappen.Core;
using CopilotKnappen.Services;
using CopilotKnappen.Views;

namespace CopilotKnappen;

public partial class App : Application
{
    private SingleInstance? _instance;
    private SettingsStore _store = null!;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkey;
    private CopilotKeyFastPath? _fastPath;
    private LauncherWindow? _launcher;
    private SettingsWindow? _settingsWindow;

    public AppSettings Settings { get; private set; } = new();
    public WindowsAppCatalog Catalog { get; } = new();
    public IntentParser Parser { get; private set; } = null!;
    public ActionExecutor Executor { get; private set; } = null!;
    public SettingsStore Store => _store;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var activation = ActivationParser.FromArguments(e.Args);

        _instance = new SingleInstance();
        if (!_instance.IsPrimary)
        {
            // Another instance is already running in the background: hand over and quit.
            if (SingleInstance.SendToPrimary(activation))
            {
                Shutdown();
                return;
            }
        }

        DispatcherUnhandledException += OnUnhandledException;

        _store = new SettingsStore(SettingsStore.DefaultPath);
        Settings = _store.Load();
        Parser = new IntentParser(() => Settings, Catalog);
        Executor = new ActionExecutor(() => Settings, Catalog);
        Catalog.RefreshInBackground(force: true);

        _launcher = new LauncherWindow();
        _launcher.InitializeHidden();
        var source = HwndSource.FromHwnd(new WindowInteropHelper(_launcher).Handle);

        // Copilot key presses while we're running arrive as window messages (see CopilotKeyFastPath).
        _fastPath = new CopilotKeyFastPath(source);
        _fastPath.KeyPressed += wParam => HandleActivation(wParam switch
        {
            CopilotKeyFastPath.PressAndHoldStart => ActivationKind.Show,
            CopilotKeyFastPath.PressAndHoldStop => ActivationKind.KeyReleased,
            _ => ActivationKind.Toggle,
        });
        _launcher.SummonOnActivation = !_fastPath.Register();

        _hotkey = new HotkeyManager(source);
        _hotkey.Pressed += () => HandleActivation(ActivationKind.Toggle);

        _tray = new TrayIcon(
            open: () => HandleActivation(ActivationKind.Show),
            settings: () => HandleActivation(ActivationKind.Settings),
            exit: ExitApp);

        ApplyHotkey(showErrors: true);

        _instance.Activated += kind => Dispatcher.BeginInvoke(() => HandleActivation(kind));
        if (_instance.IsPrimary) _instance.StartListening();

        HandleActivation(activation == ActivationKind.KeyReleased ? ActivationKind.Show : activation);
    }

    public void HandleActivation(ActivationKind kind)
    {
        if (_launcher is null) return;
        switch (kind)
        {
            case ActivationKind.Settings:
                ShowSettings();
                _launcher.HideLauncher();
                break;
            case ActivationKind.Show:
                Catalog.RefreshInBackground();
                _launcher.ShowLauncher();
                break;
            case ActivationKind.Toggle:
                Catalog.RefreshInBackground();
                _launcher.ToggleLauncher();
                break;
            case ActivationKind.KeyReleased:
                break;
        }
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            ExitIfIdle();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
        NativeMethods.ForceForeground(new WindowInteropHelper(_settingsWindow).Handle);
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        _store.Save(settings);
        ApplyHotkey(showErrors: true);
    }

    private void ApplyHotkey(bool showErrors)
    {
        if (_hotkey is null) return;
        var error = _hotkey.Register(Settings.EnableHotkey ? Settings.Hotkey : null);
        if (error is not null && showErrors) _tray?.ShowMessage("Copilot Knappen", error);
    }

    /// <summary>Called when the launcher hides; quits if the user doesn't want a background instance.</summary>
    public void ExitIfIdle()
    {
        if (!Settings.KeepRunningInBackground && _settingsWindow is null && _launcher is { IsShown: false })
            ExitApp();
    }

    public void ExitApp()
    {
        _fastPath?.Dispose();
        _fastPath = null;
        _hotkey?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        _hotkey = null;
        _tray = null;
        _instance = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _fastPath?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Never let one bad command take down the background instance.
        _tray?.ShowMessage("Copilot Knappen", e.Exception.Message);
        e.Handled = true;
    }
}

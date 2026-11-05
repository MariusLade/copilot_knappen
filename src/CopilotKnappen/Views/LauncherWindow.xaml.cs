using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CopilotKnappen.Core;
using CopilotKnappen.Services;
using Forms = System.Windows.Forms;

namespace CopilotKnappen.Views;

/// <summary>The text field in the middle of the screen.</summary>
public partial class LauncherWindow : Window
{
    private const string DefaultHint = "Try “play daft punk”, “volume 30”, “youtube cats”, “notepad” or “settings”";
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x7A));
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0xDB, 0x9A));
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF));

    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private bool _busy;
    private bool _settingText;
    private bool _shown;
    private bool _cloakHiding;
    private DateTime _lastAutoHide = DateTime.MinValue;
    private ResolvedAction _current = ResolvedAction.None;

    public LauncherWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (!_shown) return;
            _lastAutoHide = DateTime.UtcNow;
            HideLauncher();
        };
        Activated += (_, _) =>
        {
            // Without the Copilot key fast path, Windows "switches to" the running provider's window when the key
            // is pressed. Treat activation of our (invisible) window as a request to show the launcher.
            if (!_shown && SummonOnActivation) Dispatcher.BeginInvoke(ShowLauncher);
        };
        App.Current.Catalog.Refreshed += () => Dispatcher.BeginInvoke(() =>
        {
            if (_shown) UpdatePreview();
        });
    }

    /// <summary>Whether the launcher is on screen (the window itself stays "visible" but cloaked when hidden).</summary>
    public bool IsShown => _shown;

    /// <summary>Show the launcher when Windows activates our hidden window (fallback when the fast path failed).</summary>
    public bool SummonOnActivation { get; set; }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>
    /// Creates the window in its hidden state. Like PowerToys Command Palette, the window is kept visible but
    /// DWM-cloaked (and click-through) while hidden, so Windows always has a live window of ours to deliver
    /// Copilot key presses to. Falls back to a plain hidden window if cloaking is unavailable.
    /// </summary>
    public void InitializeHidden()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _cloakHiding = NativeMethods.SetCloaked(hwnd, true);
        if (!_cloakHiding) return;
        NativeMethods.SetClickThrough(hwnd, true);
        Topmost = false;
        Show(); // ShowActivated=False: no focus change
    }

    public void ToggleLauncher()
    {
        if (_shown)
        {
            HideLauncher();
            return;
        }
        // Pressing the Copilot key can steal focus from us a moment before we're activated again;
        // in that case the user meant "close", which already happened.
        if ((DateTime.UtcNow - _lastAutoHide).TotalMilliseconds < 400) return;
        ShowLauncher();
    }

    public void ShowLauncher()
    {
        if (_shown && IsActive) return;
        _shown = true;

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        UpdateLayout();
        CenterOnActiveScreen();
        // A DPI change after moving to another monitor resizes the window; centre again afterwards.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, CenterOnActiveScreen);
        Opacity = 1;

        if (_cloakHiding)
        {
            NativeMethods.SetClickThrough(Handle, false);
            Topmost = true;
            NativeMethods.SetCloaked(Handle, false);
        }

        Activate();
        NativeMethods.ForceForeground(Handle);
        Input.Focus();
        Keyboard.Focus(Input);
        Input.SelectAll();
        UpdatePreview();
    }

    public void HideLauncher()
    {
        if (!_shown) return;
        _shown = false;

        if (_cloakHiding)
        {
            NativeMethods.SetCloaked(Handle, true);
            NativeMethods.SetClickThrough(Handle, true);
            Topmost = false;
            // Hide for a moment so Windows hands the focus back to the right window, then make it "visible"
            // again (still cloaked) without activating it.
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNA);
        }
        else
        {
            Hide();
        }

        SetText("");
        _historyIndex = -1;
        if (!_busy) App.Current.ExitIfIdle();
    }

    private void CenterOnActiveScreen()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var area = screen.WorkingArea;
        if (!NativeMethods.GetWindowRect(Handle, out var rect)) return;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var x = area.Left + (area.Width - width) / 2;
        var y = area.Top + (area.Height - height) / 2;
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    // ------------------------------------------------------------------ input

    private void Input_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_settingText) _historyIndex = -1;
        UpdatePreview();
    }

    private async void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                HideLauncher();
                break;

            case Key.Enter:
                e.Handled = true;
                await ExecuteAsync(forceWebSearch: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;

            case Key.Tab:
                e.Handled = true;
                if (_current.App is { } app) SetText(app.Name);
                break;

            case Key.Up:
                e.Handled = true;
                if (_history.Count == 0) break;
                _historyIndex = _historyIndex < 0 ? _history.Count - 1 : Math.Max(0, _historyIndex - 1);
                SetText(_history[_historyIndex]);
                break;

            case Key.Down:
                e.Handled = true;
                if (_historyIndex < 0) break;
                _historyIndex++;
                if (_historyIndex >= _history.Count)
                {
                    _historyIndex = -1;
                    SetText("");
                }
                else
                {
                    SetText(_history[_historyIndex]);
                }
                break;
        }
    }

    private void SetText(string text)
    {
        _settingText = true;
        Input.Text = text;
        Input.CaretIndex = text.Length;
        _settingText = false;
    }

    private async Task ExecuteAsync(bool forceWebSearch)
    {
        var text = Input.Text.Trim();
        if (text.Length == 0 || _busy) return;

        var app = App.Current;
        var action = forceWebSearch ? app.Parser.ParseAsWebSearch(text) : app.Parser.Parse(text);
        Remember(text);

        switch (action.Type)
        {
            case ActionType.None:
                return;
            case ActionType.OpenSettings:
                app.ShowSettings();
                HideLauncher();
                return;
            case ActionType.Exit:
                app.ExitApp();
                return;
        }

        _busy = true;
        ExecutionResult result;
        try
        {
            ShowStatus("", action.Description + "…", NormalBrush);
            result = await app.Executor.ExecuteAsync(action, text);
        }
        finally
        {
            _busy = false;
        }

        if (!result.Success)
        {
            if (!_shown) ShowLauncher();
            SetText(text);
            ShowStatus("", result.Message ?? "Something went wrong.", ErrorBrush);
            return;
        }

        if (result.KeepOpen && _shown)
        {
            ShowStatus("", result.Message ?? "Done", SuccessBrush);
            Input.SelectAll();
            return;
        }

        if (_shown) HideLauncher();
        else App.Current.ExitIfIdle();
    }

    private void Remember(string text)
    {
        _history.Remove(text);
        _history.Add(text);
        if (_history.Count > 50) _history.RemoveAt(0);
    }

    // ------------------------------------------------------------------ preview

    private void UpdatePreview()
    {
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _current = App.Current.Parser.Parse(Input.Text);
        if (_current.Type == ActionType.None)
            ShowStatus("", DefaultHint, NormalBrush, AccentBrush);
        else
            ShowStatus(Glyph(_current), _current.Description, NormalBrush, AccentBrush);
    }

    private void ShowStatus(string glyph, string text, Brush textBrush, Brush? iconBrush = null)
    {
        PreviewIcon.Text = glyph;
        PreviewIcon.Foreground = iconBrush ?? textBrush;
        PreviewText.Text = text;
        PreviewText.Foreground = textBrush;
        PreviewText.ToolTip = text;
    }

    private static string Glyph(ResolvedAction action) => action.Type switch
    {
        ActionType.WebSearch => "",
        ActionType.OpenUrl => "",
        ActionType.OpenApp => "",
        ActionType.RunProgram => action.Target.Contains('\\') || action.Target.Contains('%') ? "" : "",
        ActionType.ShellCommand => "",
        ActionType.SpotifySearch => "",
        ActionType.YouTubeSearch => "",
        ActionType.Media => action.Target switch
        {
            "pause" => "",
            "next" => "",
            "previous" => "",
            _ => "",
        },
        ActionType.Volume => action.Target switch
        {
            "up" => "",
            "down" => "",
            "mute" => "",
            _ => "",
        },
        ActionType.OpenSettings => "",
        ActionType.Exit => "",
        _ => "",
    };
}

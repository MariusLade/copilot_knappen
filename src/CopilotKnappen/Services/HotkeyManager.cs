using System.Windows.Input;
using System.Windows.Interop;

namespace CopilotKnappen.Services;

/// <summary>Optional global shortcut (default Ctrl+Alt+Space) for PCs without a Copilot key.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x4B4E; // "KN"
    private readonly HwndSource _source;
    private bool _registered;

    public HotkeyManager(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
    }

    public event Action? Pressed;

    /// <summary>Registers the shortcut. Returns an error message, or null on success.</summary>
    public string? Register(string? gesture)
    {
        Unregister();
        if (string.IsNullOrWhiteSpace(gesture)) return null;
        if (!TryParse(gesture, out var modifiers, out var vk)) return $"Couldn't understand the shortcut “{gesture}”.";
        if (!NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, vk))
            return $"The shortcut {gesture} is already used by another app.";
        _registered = true;
        return null;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    public static bool TryParse(string gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        Key? key = null;
        foreach (var part in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "win" or "windows" or "meta": modifiers |= NativeMethods.MOD_WIN; break;
                default:
                    try
                    {
                        key = (Key?)new KeyConverter().ConvertFromInvariantString(part);
                    }
                    catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FormatException)
                    {
                        return false;
                    }
                    break;
            }
        }
        if (key is null || key == Key.None) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return virtualKey != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }
}

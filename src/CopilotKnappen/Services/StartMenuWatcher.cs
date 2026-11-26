using System.Runtime.InteropServices;
using System.Text;

namespace CopilotKnappen.Services;

/// <summary>
/// Keeps the launcher and the Windows Start menu (including its search) from being open at the same time:
/// raises <see cref="Opened"/> when the Start menu comes up, and can close it before the launcher is shown.
/// </summary>
public sealed class StartMenuWatcher : IDisposable
{
    // The Start menu (Windows 10/11) and Start search (Windows 11: SearchHost, Windows 10: SearchApp).
    private static readonly string[] StartMenuProcesses = ["StartMenuExperienceHost.exe", "SearchHost.exe", "SearchApp.exe"];
    private const string StartMenuWindowClass = "Windows.UI.Core.CoreWindow";

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_UNCLOAKED = 0x8018;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const byte VK_ESCAPE = 0x1B;

    private readonly WinEventProc _callback; // must stay referenced while the hooks exist
    private readonly IntPtr _foregroundHook;
    private readonly IntPtr _uncloakedHook;

    public StartMenuWatcher()
    {
        _callback = OnWinEvent;
        // Out-of-context hooks are called on this (UI) thread through its message loop.
        const uint flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, flags);
        _uncloakedHook = SetWinEventHook(EVENT_OBJECT_UNCLOAKED, EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, _callback, 0, 0, flags);
    }

    /// <summary>Raised on the UI thread when the Start menu or Start search opens.</summary>
    public event Action? Opened;

    /// <summary>Whether the Start menu or Start search is open (it has the keyboard focus while open).</summary>
    public static bool IsOpen => IsStartMenuWindow(NativeMethods.GetForegroundWindow());

    /// <summary>Closes the Start menu if it is open. Returns true if it was.</summary>
    public static bool Dismiss()
    {
        if (!IsOpen) return false;
        // Escape goes to the Start menu, which has the focus, and closes it like a user key press would.
        NativeMethods.keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(VK_ESCAPE, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        return true;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;
        if (IsStartMenuWindow(hwnd)) Opened?.Invoke();
    }

    private static bool IsStartMenuWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        var className = new StringBuilder(64);
        if (GetClassName(hwnd, className, className.Capacity) == 0 || className.ToString() != StartMenuWindowClass)
            return false;

        GetWindowThreadProcessId(hwnd, out var pid);
        var name = ProcessFileName(pid);
        return name is not null && StartMenuProcesses.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ProcessFileName(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            var path = new StringBuilder(1024);
            var size = path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref size) ? System.IO.Path.GetFileName(path.ToString()) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public void Dispose()
    {
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        if (_uncloakedHook != IntPtr.Zero) UnhookWinEvent(_uncloakedHook);
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc callback,
        uint idProcess, uint idThread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

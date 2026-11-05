using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CopilotKnappen.Services;

/// <summary>
/// "Fast path" for the Copilot key. Windows only uses URI activation to *start* a Copilot key provider. While the
/// app is already running, Windows does not launch it again. Instead it posts a window message to the window that
/// registered itself here. Registration means storing the message id in the window's shell property store under
/// the Copilot fast-path key. The message's wParam is the MessageWParam value declared for SingleTap etc. in
/// AppxManifest.xml.
/// See https://learn.microsoft.com/windows/apps/develop/windows-integration/copilot-key-state
/// (PowerToys Command Palette does the same thing: CopilotKeyRegistration.cs).
/// </summary>
public sealed class CopilotKeyFastPath : IDisposable
{
    /// <summary>WM_APP + 1. The message Windows posts to our window on a key press.</summary>
    public const int MessageId = 0x8001;

    /// <summary>Must match &lt;SingleTap MessageWParam="0"&gt; in packaging/AppxManifest.xml.</summary>
    public const int SingleTap = 0;
    public const int PressAndHoldStart = 1;
    public const int PressAndHoldStop = 2;

    private static readonly Guid FastPathFormatId = new("38652BCA-4329-4E74-86F9-39CF29345EEA");
    private const uint FastPathPropertyId = 2;

    private readonly HwndSource _source;
    private bool _registered;

    public CopilotKeyFastPath(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
    }

    /// <summary>Raised with the wParam of the message (SingleTap, PressAndHoldStart, ...).</summary>
    public event Action<int>? KeyPressed;

    public bool IsRegistered => _registered;

    /// <summary>Registers the window for fast-path messages. Returns false if Windows refused.</summary>
    public bool Register()
    {
        try
        {
            var value = new PROPVARIANT { vt = VT_UINT, uintVal = MessageId };
            SetWindowProperty(_source.Handle, ref value);
            _registered = true;
        }
        catch (Exception ex) when (ex is COMException or EntryPointNotFoundException or InvalidCastException)
        {
            _registered = false;
        }
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == MessageId)
        {
            handled = true;
            KeyPressed?.Invoke((int)wParam.ToInt64());
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
        {
            try
            {
                // VT_EMPTY removes the property; it must be gone before the window is destroyed.
                var empty = new PROPVARIANT { vt = VT_EMPTY };
                SetWindowProperty(_source.Handle, ref empty);
            }
            catch (COMException)
            {
                // Window already going away.
            }
            _registered = false;
        }
        _source.RemoveHook(WndProc);
    }

    private static void SetWindowProperty(IntPtr hwnd, ref PROPVARIANT value)
    {
        var iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out var store));
        try
        {
            var key = new PROPERTYKEY { fmtid = FastPathFormatId, pid = FastPathPropertyId };
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
            store.Commit(); // documented as unnecessary for window stores, but harmless
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private const ushort VT_EMPTY = 0;
    private const ushort VT_UINT = 23;

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, out IPropertyStore propertyStore);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    // PROPVARIANT is 24 bytes on 64-bit Windows: VARTYPE + 3 reserved WORDs, then a 16-byte union.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint uintVal;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }
}

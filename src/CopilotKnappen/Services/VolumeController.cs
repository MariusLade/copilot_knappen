using System.Runtime.InteropServices;
using CopilotKnappen.Core;

namespace CopilotKnappen.Services;

/// <summary>Master volume of the default playback device via the Core Audio API (IAudioEndpointVolume).</summary>
public sealed class VolumeController
{
    /// <summary>Applies the command and returns a short status text such as "Volume 40%".</summary>
    public string Apply(VolumeCommand command, int? amount, int defaultStep)
    {
        try
        {
            var endpoint = GetEndpoint();
            try
            {
                var context = Guid.Empty;
                Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var level));
                Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));

                switch (command)
                {
                    case VolumeCommand.Set:
                    case VolumeCommand.Up:
                    case VolumeCommand.Down:
                        var percent = (int)Math.Round(level * 100);
                        var step = amount ?? defaultStep;
                        percent = command switch
                        {
                            VolumeCommand.Up => percent + step,
                            VolumeCommand.Down => percent - step,
                            _ => amount ?? percent,
                        };
                        percent = Math.Clamp(percent, 0, 100);
                        Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(percent / 100f, ref context));
                        if (muted && percent > 0 && command != VolumeCommand.Down)
                            Marshal.ThrowExceptionForHR(endpoint.SetMute(false, ref context));
                        return $"Volume {percent}%";

                    case VolumeCommand.Mute:
                        Marshal.ThrowExceptionForHR(endpoint.SetMute(true, ref context));
                        return "Muted";

                    case VolumeCommand.Unmute:
                        Marshal.ThrowExceptionForHR(endpoint.SetMute(false, ref context));
                        return $"Unmuted · volume {(int)Math.Round(level * 100)}%";

                    default:
                        Marshal.ThrowExceptionForHR(endpoint.SetMute(!muted, ref context));
                        return muted ? "Unmuted" : "Muted";
                }
            }
            finally
            {
                Marshal.ReleaseComObject(endpoint);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // No audio device or Core Audio unavailable: fall back to volume keys (2% per press).
            switch (command)
            {
                case VolumeCommand.Up:
                    for (var i = 0; i < Math.Max(1, (amount ?? defaultStep) / 2); i++) NativeMethods.PressKey(NativeMethods.VK_VOLUME_UP);
                    return "Volume up";
                case VolumeCommand.Down:
                    for (var i = 0; i < Math.Max(1, (amount ?? defaultStep) / 2); i++) NativeMethods.PressKey(NativeMethods.VK_VOLUME_DOWN);
                    return "Volume down";
                case VolumeCommand.Mute or VolumeCommand.Unmute or VolumeCommand.ToggleMute:
                    NativeMethods.PressKey(NativeMethods.VK_VOLUME_MUTE);
                    return "Toggled mute";
                default:
                    throw new InvalidOperationException("Could not access the audio device.", ex);
            }
        }
    }

    private static IAudioEndpointVolume GetEndpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device));
            try
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var obj));
                return (IAudioEndpointVolume)obj;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private const int ClsCtxAll = 0x17;

    private enum EDataFlow { Render = 0, Capture = 1, All = 2 }

    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}

using System.Runtime.InteropServices;

namespace DynamicIsland.Windows.Interop;

internal enum EDataFlow { Render, Capture, All }
internal enum ERole { Console, Multimedia, Communications }
internal enum AudioSessionState { Inactive, Active, Expired }

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject;

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    int RegisterEndpointNotificationCallback(nint callback);
    int UnregisterEndpointNotificationCallback(nint callback);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid interfaceId, uint classContext, nint activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    int OpenPropertyStore(uint storageAccess, out IPropertyStore properties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out uint state);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    int GetCount(out int count);
    int Item(int index, out IMMDevice device);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    int GetCount(out int count);
    int GetAt(int index, out PropertyKey key);
    int GetValue(ref PropertyKey key, out PropVariant value);
    int SetValue(ref PropertyKey key, ref PropVariant value);
    int Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;
}

[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public nint PointerValue;
}

// Undocumented but stable audio-endpoint policy interface (used to switch the default output device).
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    int GetMixFormat(nint a, nint b);
    int GetDeviceFormat(nint a, int b, nint c);
    int ResetDeviceFormat(nint a);
    int SetDeviceFormat(nint a, nint b, nint c);
    int GetProcessingPeriod(nint a, int b, nint c, nint d);
    int SetProcessingPeriod(nint a, nint b);
    int GetShareMode(nint a, nint b);
    int SetShareMode(nint a, nint b);
    int GetPropertyValue(nint a, nint b, nint c);
    int SetPropertyValue(nint a, nint b, nint c);
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    int SetEndpointVisibility(nint a, int b);
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class CPolicyConfigClient;

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    int RegisterControlChangeNotify(nint notify);
    int UnregisterControlChangeNotify(nint notify);
    int GetChannelCount(out uint count);
    int SetMasterVolumeLevel(float levelDb, nint eventContext);
    int SetMasterVolumeLevelScalar(float level, nint eventContext);
    int GetMasterVolumeLevel(out float levelDb);
    int GetMasterVolumeLevelScalar(out float level);
    int SetChannelVolumeLevel(uint channel, float levelDb, nint eventContext);
    int SetChannelVolumeLevelScalar(uint channel, float level, nint eventContext);
    int GetChannelVolumeLevel(uint channel, out float levelDb);
    int GetChannelVolumeLevelScalar(uint channel, out float level);
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, nint eventContext);
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    int GetVolumeStepInfo(out uint step, out uint stepCount);
    int VolumeStepUp(nint eventContext);
    int VolumeStepDown(nint eventContext);
    int QueryHardwareSupport(out uint mask);
    int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    int GetPeakValue(out float peak);
    int GetMeteringChannelCount(out int count);
    int GetChannelsPeakValues(int channelCount, [Out] float[] values);
    int QueryHardwareSupport(out int mask);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    int GetAudioSessionControl(nint sessionGuid, uint streamFlags, out nint sessionControl);
    int GetSimpleAudioVolume(nint sessionGuid, uint streamFlags, out nint audioVolume);
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
    int RegisterSessionNotification(nint notification);
    int UnregisterSessionNotification(nint notification);
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, nint notification);
    int UnregisterDuckNotification(nint notification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    int GetCount(out int sessionCount);
    int GetSession(int sessionIndex, out IAudioSessionControl sessionControl);
}

[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    int GetState(out AudioSessionState state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, nint eventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, nint eventContext);
    int GetGroupingParam(out Guid groupingId);
    int SetGroupingParam(ref Guid groupingId, nint eventContext);
    int RegisterAudioSessionNotification(nint client);
    int UnregisterAudioSessionNotification(nint client);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    int GetState(out AudioSessionState state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, nint eventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, nint eventContext);
    int GetGroupingParam(out Guid groupingId);
    int SetGroupingParam(ref Guid groupingId, nint eventContext);
    int RegisterAudioSessionNotification(nint client);
    int UnregisterAudioSessionNotification(nint client);
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionIdentifier);
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceIdentifier);
    int GetProcessId(out uint processId);
    int IsSystemSoundsSession();
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    int SetMasterVolume(float level, nint eventContext);
    int GetMasterVolume(out float level);
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, nint eventContext);
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, nint format, nint sessionGuid);
    int GetBufferSize(out uint numBufferFrames);
    int GetStreamLatency(out long latency);
    int GetCurrentPadding(out uint numPaddingFrames);
    int IsFormatSupported(int shareMode, nint format, out nint closestMatch);
    int GetMixFormat(out nint deviceFormat);
    int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
    int Start();
    int Stop();
    int Reset();
    int SetEventHandle(nint eventHandle);
    int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    int GetBuffer(out nint data, out uint numFramesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    int ReleaseBuffer(uint numFramesRead);
    int GetNextPacketSize(out uint numFramesInNextPacket);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormatEx
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

internal static class CoreAudioFactory
{
    internal const uint ClsctxAll = 23;

    public static (IMMDevice Device, T Interface) ActivateDefault<T>() where T : class
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device));
        var iid = typeof(T).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsctxAll, nint.Zero, out var instance));
        return (device, (T)instance);
    }

    public static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.ReleaseComObject(value); } catch { }
        }
    }

    private const uint DeviceStateActive = 0x1;
    private const uint StgmRead = 0x0;
    // PKEY_Device_FriendlyName — the user-visible endpoint name (e.g. "Speakers (Realtek)").
    private static PropertyKey FriendlyNameKey => new()
    { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), PropertyId = 14 };

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    /// <summary>Active render (output) endpoints as (device id, friendly name) pairs.</summary>
    public static IReadOnlyList<(string Id, string Name)> GetRenderEndpoints()
    {
        var result = new List<(string, string)>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out collection) < 0) return result;
            collection.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(i, out device) < 0) continue;
                    device.GetId(out var id);
                    result.Add((id, ReadFriendlyName(device) ?? id));
                }
                catch { }
                finally { Release(device); }
            }
        }
        catch { }
        finally { Release(collection); Release(enumerator); }
        return result;
    }

    /// <summary>Friendly name of the current default multimedia render endpoint (empty if unavailable).</summary>
    public static string GetDefaultRenderName()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device) < 0) return "";
            return ReadFriendlyName(device) ?? "";
        }
        catch { return ""; }
        finally { Release(device); Release(enumerator); }
    }

    /// <summary>Makes the given endpoint the default for all three roles.</summary>
    public static void SetDefaultRenderDevice(string deviceId)
    {
        IPolicyConfig? config = null;
        try
        {
            config = (IPolicyConfig)new CPolicyConfigClient();
            Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(deviceId, ERole.Console));
            Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(deviceId, ERole.Multimedia));
            Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(deviceId, ERole.Communications));
        }
        finally { Release(config); }
    }

    /// <summary>Friendly name for an already-activated endpoint (empty if unavailable).</summary>
    public static string FriendlyNameOf(object device) =>
        device is IMMDevice d ? ReadFriendlyName(d) ?? "" : "";

    public static string IdOf(object device)
    {
        try
        {
            if (device is IMMDevice d && d.GetId(out var id) >= 0) return id;
        }
        catch { }
        return "";
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(StgmRead, out store) < 0) return null;
            var key = FriendlyNameKey;
            if (store.GetValue(ref key, out var value) < 0) return null;
            try { return value.PointerValue != nint.Zero ? Marshal.PtrToStringUni(value.PointerValue) : null; }
            finally { PropVariantClear(ref value); }
        }
        catch { return null; }
        finally { Release(store); }
    }
}

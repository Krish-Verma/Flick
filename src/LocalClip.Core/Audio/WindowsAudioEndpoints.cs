using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace LocalClip.Core.Audio;

public enum AudioEndpointFlow { Output, Input }

/// <summary>A Windows Core Audio endpoint. The ID is the same ID expected by OBS WASAPI device_id.</summary>
public sealed record AudioEndpoint(string Id, string Name, bool IsDefault);
public sealed record AudioEndpointChoice(string Id, string Label, bool IsAvailable);

public static class AudioEndpointChoices
{
    public static IReadOnlyList<AudioEndpointChoice> ForSelection(
        AudioEndpointFlow flow, string? selectedId, IEnumerable<AudioEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        string original = string.IsNullOrWhiteSpace(selectedId) ? "default" : selectedId;
        string defaultLabel = flow == AudioEndpointFlow.Output
            ? "Windows default playback device (follows Windows)"
            : "Windows default communications microphone (follows Windows)";
        var choices = new List<AudioEndpointChoice> { new("default", defaultLabel, true) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "default" };
        foreach (var endpoint in endpoints.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(endpoint.Id) || !seen.Add(endpoint.Id)) continue;
            var name = string.IsNullOrWhiteSpace(endpoint.Name) ? "Audio device" : endpoint.Name.Trim();
            if (name.Length > 110) name = name[..110] + "…";
            choices.Add(new(endpoint.Id, name + (endpoint.IsDefault ? " (Windows default)" : ""), true));
        }
        // Never erase an unplugged but intentionally selected endpoint.
        // The UI marks it unavailable, and explicit Save of an active source
        // must require a device choice rather than silently using Default.
        if (!seen.Contains(original))
            choices.Add(new(original, "Previous device unavailable — refresh or select another", false));
        return choices;
    }
}

/// <summary>
/// Read-only enumeration through Windows IMMDeviceEnumerator/IMMDevice, not an
/// OBS capture session. The IDs are used unchanged by wasapi_input_capture and
/// wasapi_output_capture; no audio stream or microphone is opened here.
/// </summary>
public static class WindowsAudioEndpointEnumerator
{
    private const uint DeviceStateActive = 0x1;
    private const ushort VtLpWStr = 31;
    private static readonly PropertyKey FriendlyName =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static IReadOnlyList<AudioEndpoint> Enumerate(AudioEndpointFlow flow)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<AudioEndpoint>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? devices = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MmDeviceEnumeratorClass();
            int direction = flow == AudioEndpointFlow.Output ? 0 : 1;
            int role = flow == AudioEndpointFlow.Output ? 0 : 2; // OBS: eConsole output; eCommunications microphone.
            string? defaultId = GetDefaultId(enumerator, direction, role);
            enumerator.EnumAudioEndpoints(direction, DeviceStateActive, out devices);
            devices.GetCount(out uint count);
            var results = new List<AudioEndpoint>();
            // Windows can have many endpoints but UI does not need to enumerate
            // an unbounded list or access any endpoint beyond this limit.
            for (uint i = 0; i < Math.Min(count, 128u); i++)
            {
                devices.Item(i, out IMMDevice endpoint);
                try
                {
                    string id = GetId(endpoint);
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    string label = GetFriendlyName(endpoint) ?? "Audio device";
                    label = label.Replace('\r', ' ').Replace('\n', ' ').Trim();
                    results.Add(new(id, label, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                }
                finally { ReleaseCom(endpoint); }
            }
            return results;
        }
        finally
        {
            ReleaseCom(devices);
            ReleaseCom(enumerator);
        }
    }

    private static string? GetDefaultId(IMMDeviceEnumerator enumerator, int flow, int role)
    {
        IMMDevice? device = null;
        try
        {
            enumerator.GetDefaultAudioEndpoint(flow, role, out device);
            return device is null ? null : GetId(device);
        }
        catch (COMException) { return null; } // A machine may have no active default.
        finally { ReleaseCom(device); }
    }

    private static string GetId(IMMDevice device)
    {
        device.GetId(out IntPtr buffer);
        try { return Marshal.PtrToStringUni(buffer) ?? ""; }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeCoTaskMem(buffer); }
    }

    private static string? GetFriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(0, out IPropertyStore store); // STGM_READ
        try
        {
            var key = FriendlyName;
            store.GetValue(ref key, out PropVariant value);
            try
            {
                return value.Type == VtLpWStr && value.Pointer != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.Pointer) : null;
            }
            finally { _ = PropVariantClear(ref value); }
        }
        finally { ReleaseCom(store); }
    }

    private static void ReleaseCom(object? item)
    {
        if (item != null && Marshal.IsComObject(item)) _ = Marshal.ReleaseComObject(item);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(ref PropVariant variant);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey
    {
        public PropertyKey(Guid format, uint id) { Format = format; Id = id; }
        public readonly Guid Format;
        public readonly uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MmDeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection collection);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IntPtr callback);
        void UnregisterEndpointNotificationCallback(IntPtr callback);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid interfaceId, uint classContext, IntPtr activationParams, out IntPtr instance);
        void OpenPropertyStore(uint access, out IPropertyStore properties);
        void GetId(out IntPtr id);
        void GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
}

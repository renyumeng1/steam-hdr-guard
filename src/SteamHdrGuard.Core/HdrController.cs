using System.Runtime.InteropServices;

namespace SteamHdrGuard.Core;

public sealed class HdrController
{
    private const uint QdcOnlyActive = 2;
    private const int Ok = 0;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var paths = QueryPaths();
        var list = new List<DisplayInfo>();
        foreach (var p in paths)
        {
            var t = p.targetInfo;
            if (!GetColor(t.adapterId, t.id, out var c)) continue;
            string name = $"Display {list.Count + 1}";
            string devicePath = "";
            if (GetName(t.adapterId, t.id, out var n))
            {
                if (!string.IsNullOrWhiteSpace(n.monitorFriendlyDeviceName)) name = n.monitorFriendlyDeviceName;
                devicePath = n.monitorDevicePath ?? "";
            }
            list.Add(new DisplayInfo
            {
                MonitorName = name,
                MonitorDevicePath = devicePath,
                TargetId = t.id,
                AdapterLowPart = t.adapterId.LowPart,
                AdapterHighPart = t.adapterId.HighPart,
                AdvancedColorSupported = (c.value & 1) != 0,
                AdvancedColorEnabled = (c.value & 2) != 0,
                WideColorEnforced = (c.value & 4) != 0,
                AdvancedColorForceDisabled = (c.value & 8) != 0,
                ColorEncoding = c.colorEncoding,
                BitsPerColorChannel = c.bitsPerColorChannel
            });
        }
        return list;
    }

    public bool IsAnyHdrEnabled() => GetDisplays().Any(x => x.AdvancedColorSupported && x.AdvancedColorEnabled);

    public int SetHdrForAllSupportedDisplays(bool enabled)
    {
        int changed = 0;
        foreach (var p in QueryPaths())
        {
            var t = p.targetInfo;
            if (!GetColor(t.adapterId, t.id, out var c)) continue;
            if ((c.value & 1) == 0 || (c.value & 8) != 0) continue;

            var s = new SetColor
            {
                header = MakeHeader(DeviceInfoType.SetAdvancedColorState, t.adapterId, t.id, Marshal.SizeOf<SetColor>()),
                value = enabled ? 1u : 0u
            };
            if (DisplayConfigSetDeviceInfo(ref s) == Ok) changed++;
        }
        if (changed == 0) throw new InvalidOperationException("No HDR-capable active display was changed.");
        return changed;
    }

    private static bool GetColor(Luid adapter, uint id, out GetColor info)
    {
        info = new GetColor { header = MakeHeader(DeviceInfoType.GetAdvancedColorInfo, adapter, id, Marshal.SizeOf<GetColor>()) };
        return DisplayConfigGetDeviceInfo(ref info) == Ok;
    }

    private static bool GetName(Luid adapter, uint id, out TargetName name)
    {
        name = new TargetName { header = MakeHeader(DeviceInfoType.GetTargetName, adapter, id, Marshal.SizeOf<TargetName>()) };
        return DisplayConfigGetDeviceInfo(ref name) == Ok;
    }

    private static Header MakeHeader(DeviceInfoType type, Luid adapter, uint id, int size)
    {
        return new Header { type = type, size = (uint)size, adapterId = adapter, id = id };
    }

    private static PathInfo[] QueryPaths()
    {
        int status = GetDisplayConfigBufferSizes(QdcOnlyActive, out uint pc, out uint mc);
        if (status != Ok) throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed: {status}");
        var paths = new PathInfo[(int)pc];
        var modes = new ModeInfo[(int)mc];
        status = QueryDisplayConfig(QdcOnlyActive, ref pc, paths, ref mc, modes, IntPtr.Zero);
        if (status != Ok) throw new InvalidOperationException($"QueryDisplayConfig failed: {status}");
        return paths.Take((int)pc).ToArray();
    }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PathInfo[] pathArray, ref uint modes, [Out] ModeInfo[] modeArray, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int DisplayConfigGetDeviceInfo(ref GetColor packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", CharSet = CharSet.Unicode)] private static extern int DisplayConfigGetDeviceInfo(ref TargetName packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] private static extern int DisplayConfigSetDeviceInfo(ref SetColor packet);

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public SourceInfo sourceInfo; public TargetInfo targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] private struct SourceInfo { public Luid adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct TargetInfo
    {
        public Luid adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling;
        public Rational refreshRate; public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Explicit)] private struct ModeInfo
    {
        [FieldOffset(0)] public uint infoType; [FieldOffset(4)] public uint id; [FieldOffset(8)] public Luid adapterId;
        [FieldOffset(16)] public TargetMode targetMode; [FieldOffset(16)] public SourceMode sourceMode; [FieldOffset(16)] public DesktopImageInfo desktopImageInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TargetMode { public VideoSignalInfo targetVideoSignalInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct VideoSignalInfo { public ulong pixelRate; public Rational hSyncFreq; public Rational vSyncFreq; public Region activeSize; public Region totalSize; public uint videoStandard; public uint scanLineOrdering; }
    [StructLayout(LayoutKind.Sequential)] private struct Region { public uint cx; public uint cy; }
    [StructLayout(LayoutKind.Sequential)] private struct SourceMode { public uint width; public uint height; public uint pixelFormat; public PointL position; }
    [StructLayout(LayoutKind.Sequential)] private struct PointL { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectL { public int left; public int top; public int right; public int bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct DesktopImageInfo { public PointL pathSourceSize; public RectL desktopImageRegion; public RectL desktopImageClip; }

    [StructLayout(LayoutKind.Sequential)] private struct Header { public DeviceInfoType type; public uint size; public Luid adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential)] private struct GetColor { public Header header; public uint value; public uint colorEncoding; public uint bitsPerColorChannel; }
    [StructLayout(LayoutKind.Sequential)] private struct SetColor { public Header header; public uint value; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName
    {
        public Header header; public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }
    private enum DeviceInfoType : uint { GetTargetName = 2, GetAdvancedColorInfo = 9, SetAdvancedColorState = 10 }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HaPcRemote.Service.Native;

/// <summary>
/// P/Invoke declarations for the Windows CCD (Connecting and Configuring Displays) API.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class DisplayConfigApi
{
    // ── P/Invoke ──────────────────────────────────────────────────────

    [LibraryImport("user32.dll")]
    internal static partial int GetDisplayConfigBufferSizes(
        QueryDisplayConfigFlags flags,
        out int numPathArrayElements,
        out int numModeInfoArrayElements);

    [LibraryImport("user32.dll")]
    internal static partial int QueryDisplayConfig(
        QueryDisplayConfigFlags flags,
        ref int numPathArrayElements,
        [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref int numModeInfoArrayElements,
        [In, Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        out DISPLAYCONFIG_TOPOLOGY_ID currentTopologyId);

    [LibraryImport("user32.dll")]
    internal static partial int QueryDisplayConfig(
        QueryDisplayConfigFlags flags,
        ref int numPathArrayElements,
        [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref int numModeInfoArrayElements,
        [In, Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        nint currentTopologyId); // IntPtr.Zero when topology not needed

    [LibraryImport("user32.dll")]
    internal static partial int SetDisplayConfig(
        int numPathArrayElements,
        [In] DISPLAYCONFIG_PATH_INFO[]? pathArray,
        int numModeInfoArrayElements,
        [In] DISPLAYCONFIG_MODE_INFO[]? modeInfoArray,
        SetDisplayConfigFlags flags);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    // ── Constants ─────────────────────────────────────────────────────

    internal const int ERROR_SUCCESS = 0;
    internal const int ERROR_GEN_FAILURE = 31;
    internal const int ERROR_INVALID_PARAMETER = 87;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;

    // ── Enums ─────────────────────────────────────────────────────────

    [Flags]
    internal enum QueryDisplayConfigFlags : uint
    {
        QDC_ALL_PATHS = 0x00000001,
        QDC_ONLY_ACTIVE_PATHS = 0x00000002,
        QDC_DATABASE_CURRENT = 0x00000004,
    }

    [Flags]
    internal enum SetDisplayConfigFlags : uint
    {
        SDC_TOPOLOGY_INTERNAL = 0x00000001,
        SDC_TOPOLOGY_CLONE = 0x00000002,
        SDC_TOPOLOGY_EXTEND = 0x00000004,
        SDC_TOPOLOGY_EXTERNAL = 0x00000008,
        SDC_TOPOLOGY_SUPPLIED = 0x00000010,
        SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020,
        SDC_VALIDATE = 0x00000040,
        SDC_APPLY = 0x00000080,
        SDC_NO_OPTIMIZATION = 0x00000100,
        SDC_SAVE_TO_DATABASE = 0x00000200,
        SDC_ALLOW_CHANGES = 0x00000400,
        SDC_PATH_PERSIST_IF_REQUIRED = 0x00000800,
        SDC_FORCE_MODE_ENUMERATION = 0x00001000,
        SDC_ALLOW_PATH_ORDER_CHANGES = 0x00002000,
    }

    [Flags]
    internal enum DISPLAYCONFIG_PATH_FLAGS : uint
    {
        NONE = 0,
        ACTIVE = 0x00000001,
        SUPPORT_VIRTUAL_MODE = 0x00000008,
    }

    internal enum DISPLAYCONFIG_MODE_INFO_TYPE : uint
    {
        SOURCE = 1,
        TARGET = 2,
        DESKTOP_IMAGE = 3,
    }

    internal enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
    {
        GET_SOURCE_NAME = 1,
        GET_TARGET_NAME = 2,
        GET_TARGET_PREFERRED_MODE = 3,
        GET_ADAPTER_NAME = 4,
    }

    internal enum DISPLAYCONFIG_TOPOLOGY_ID : uint
    {
        INTERNAL = 0x00000001,
        CLONE = 0x00000002,
        EXTEND = 0x00000004,
        EXTERNAL = 0x00000008,
    }

    internal enum DISPLAYCONFIG_PIXELFORMAT : uint
    {
        PIXELFORMAT_8BPP = 1,
        PIXELFORMAT_16BPP = 2,
        PIXELFORMAT_24BPP = 3,
        PIXELFORMAT_32BPP = 4,
        PIXELFORMAT_NONGDI = 5,
    }

    internal enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint
    {
        OTHER = unchecked((uint)-1),
        HD15 = 0,
        SVIDEO = 1,
        COMPOSITE_VIDEO = 2,
        COMPONENT_VIDEO = 3,
        DVI = 4,
        HDMI = 5,
        LVDS = 6,
        D_JPN = 8,
        SDI = 9,
        DISPLAYPORT_EXTERNAL = 10,
        DISPLAYPORT_EMBEDDED = 11,
        UDI_EXTERNAL = 12,
        UDI_EMBEDDED = 13,
        SDTVDONGLE = 14,
        MIRACAST = 15,
        INDIRECT_WIRED = 16,
        INDIRECT_VIRTUAL = 17,
        INTERNAL = 0x80000000,
    }

    internal enum DISPLAYCONFIG_ROTATION : uint
    {
        IDENTITY = 1,
        ROTATE90 = 2,
        ROTATE180 = 3,
        ROTATE270 = 4,
    }

    internal enum DISPLAYCONFIG_SCALING : uint
    {
        IDENTITY = 1,
        CENTERED = 2,
        STRETCHED = 3,
        ASPECTRATIOCENTEREDMAX = 4,
        CUSTOM = 5,
        PREFERRED = 128,
    }

    internal enum DISPLAYCONFIG_SCANLINE_ORDERING : uint
    {
        UNSPECIFIED = 0,
        PROGRESSIVE = 1,
        INTERLACED = 2,
        INTERLACED_UPPERFIELDFIRST = 2,
        INTERLACED_LOWERFIELDFIRST = 3,
    }

    // ── Structs ───────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        public uint LowPart;
        public int HighPart;

        public override readonly bool Equals(object? obj) =>
            obj is LUID other && LowPart == other.LowPart && HighPart == other.HighPart;

        public override readonly int GetHashCode() => HashCode.Combine(LowPart, HighPart);

        public static bool operator ==(LUID left, LUID right) => left.Equals(right);
        public static bool operator !=(LUID left, LUID right) => !left.Equals(right);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;

        public readonly int ToHz() =>
            Denominator == 0 ? 0 : (int)Math.Round((double)Numerator / Denominator);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
        public DISPLAYCONFIG_ROTATION rotation;
        public DISPLAYCONFIG_SCALING scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
        public uint targetAvailable; // BOOL — nonzero = true
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public DISPLAYCONFIG_PATH_FLAGS flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public DISPLAYCONFIG_PIXELFORMAT pixelFormat;
        public POINTL position;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)] public DISPLAYCONFIG_TARGET_MODE targetMode;
        [FieldOffset(0)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_MODE_INFO
    {
        public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_MODE_INFO_UNION info;
    }

    // ── DeviceInfo request structs ────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }
}

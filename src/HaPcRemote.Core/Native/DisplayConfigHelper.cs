using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static HaPcRemote.Service.Native.DisplayConfigApi;

namespace HaPcRemote.Service.Native;

/// <summary>
/// Wraps the raw CCD P/Invoke calls into usable C# methods.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DisplayConfigHelper : IDisplayConfigApi
{
    public (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) QueryConfig(QueryDisplayConfigFlags flags)
    {
        int status;
        DISPLAYCONFIG_PATH_INFO[] paths;
        DISPLAYCONFIG_MODE_INFO[] modes;

        // Retry loop: buffer size can change between GetBufferSizes and QueryDisplayConfig
        // due to hotplug events.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            status = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            ThrowOnError(status, nameof(GetDisplayConfigBufferSizes));

            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            status = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, nint.Zero);

            if (status == ERROR_INSUFFICIENT_BUFFER)
                continue;

            ThrowOnError(status, nameof(QueryDisplayConfig));

            // Trim arrays if actual count < allocated
            if (pathCount < paths.Length)
                Array.Resize(ref paths, pathCount);
            if (modeCount < modes.Length)
                Array.Resize(ref modes, modeCount);

            return (paths, modes);
        }

        throw new Win32Exception(ERROR_INSUFFICIENT_BUFFER,
            "QueryDisplayConfig buffer size kept changing after 3 attempts.");
    }

    public void ApplyConfig(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, SetDisplayConfigFlags flags)
    {
        var status = SetDisplayConfig(paths.Length, paths, modes.Length, modes, flags);
        ThrowOnError(status, nameof(SetDisplayConfig));
    }

    public (string FriendlyName, ushort ManufacturerId, ushort ProductCodeId) GetTargetDeviceInfo(LUID adapterId, uint targetId)
    {
        var deviceName = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        deviceName.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_NAME;
        deviceName.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
        deviceName.header.adapterId = adapterId;
        deviceName.header.id = targetId;

        var status = DisplayConfigGetDeviceInfo(ref deviceName);
        ThrowOnError(status, nameof(DisplayConfigGetDeviceInfo));

        return (
            deviceName.monitorFriendlyDeviceName ?? "",
            deviceName.edidManufactureId,
            deviceName.edidProductCodeId
        );
    }

    public string GetSourceGdiName(LUID adapterId, uint sourceId)
    {
        var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_SOURCE_NAME;
        sourceName.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
        sourceName.header.adapterId = adapterId;
        sourceName.header.id = sourceId;

        var status = DisplayConfigGetDeviceInfo(ref sourceName);
        ThrowOnError(status, nameof(DisplayConfigGetDeviceInfo));

        return sourceName.viewGdiDeviceName ?? "";
    }

    private static void ThrowOnError(int status, string functionName)
    {
        if (status != ERROR_SUCCESS)
            throw new Win32Exception(status, $"{functionName} failed with error code {status}.");
    }
}

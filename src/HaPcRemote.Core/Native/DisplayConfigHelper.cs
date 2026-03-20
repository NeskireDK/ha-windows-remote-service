using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using static HaPcRemote.Service.Native.DisplayConfigApi;

namespace HaPcRemote.Service.Native;

/// <summary>
/// Wraps the raw CCD P/Invoke calls into usable C# methods.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DisplayConfigHelper(ILogger<DisplayConfigHelper> logger) : IDisplayConfigApi
{
    public (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) QueryConfig(QueryDisplayConfigFlags flags)
    {
        int status;
        DISPLAYCONFIG_PATH_INFO[] paths;
        DISPLAYCONFIG_MODE_INFO[] modes;

        logger.LogTrace("QueryConfig: flags={Flags}", flags);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            status = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            ThrowOnError(status, nameof(GetDisplayConfigBufferSizes));

            logger.LogTrace("QueryConfig: buffers allocated — {Paths} paths, {Modes} modes", pathCount, modeCount);

            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            status = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, nint.Zero);

            if (status == ERROR_INSUFFICIENT_BUFFER)
            {
                logger.LogTrace("QueryConfig: buffer too small on attempt {Attempt}, retrying", attempt + 1);
                continue;
            }

            ThrowOnError(status, nameof(QueryDisplayConfig));

            if (pathCount < paths.Length)
                Array.Resize(ref paths, pathCount);
            if (modeCount < modes.Length)
                Array.Resize(ref modes, modeCount);

            logger.LogTrace("QueryConfig: returned {Paths} paths, {Modes} modes", paths.Length, modes.Length);
            return (paths, modes);
        }

        throw new Win32Exception(ERROR_INSUFFICIENT_BUFFER,
            "QueryDisplayConfig buffer size kept changing after 3 attempts.");
    }

    public void ApplyConfig(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, SetDisplayConfigFlags flags)
    {
        logger.LogDebug("ApplyConfig: {Paths} paths, {Modes} modes, flags={Flags}", paths.Length, modes.Length, flags);

        for (var i = 0; i < paths.Length; i++)
        {
            var p = paths[i];
            var active = (p.flags & DISPLAYCONFIG_PATH_FLAGS.ACTIVE) != 0;
            logger.LogTrace(
                "  Path[{I}]: adapter=({Lo},{Hi}) source={Src} target={Tgt} active={Active} srcMode={SrcIdx} tgtMode={TgtIdx}",
                i, p.sourceInfo.adapterId.LowPart, p.sourceInfo.adapterId.HighPart,
                p.sourceInfo.id, p.targetInfo.id, active,
                p.sourceInfo.modeInfoIdx, p.targetInfo.modeInfoIdx);
        }

        for (var i = 0; i < modes.Length; i++)
        {
            var m = modes[i];
            if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE.SOURCE)
            {
                logger.LogTrace(
                    "  Mode[{I}]: SOURCE {W}x{H} pos=({X},{Y})",
                    i, m.info.sourceMode.width, m.info.sourceMode.height,
                    m.info.sourceMode.position.x, m.info.sourceMode.position.y);
            }
            else if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE.TARGET)
            {
                logger.LogTrace(
                    "  Mode[{I}]: TARGET vSync={Hz}Hz",
                    i, m.info.targetMode.targetVideoSignalInfo.vSyncFreq.ToHz());
            }
        }

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

        var friendly = deviceName.monitorFriendlyDeviceName ?? "";
        var path = deviceName.monitorDevicePath ?? "";
        logger.LogTrace(
            "GetTargetDeviceInfo: target={TargetId} friendly=\"{Friendly}\" edid={Mfg:X4}:{Prod:X4} connector={Conn} devicePath=\"{Path}\"",
            targetId, friendly, deviceName.edidManufactureId, deviceName.edidProductCodeId,
            deviceName.connectorInstance, path);

        return (friendly, deviceName.edidManufactureId, deviceName.edidProductCodeId);
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

        var gdiName = sourceName.viewGdiDeviceName ?? "";
        logger.LogTrace("GetSourceGdiName: source={SourceId} gdi=\"{GdiName}\"", sourceId, gdiName);
        return gdiName;
    }

    private static void ThrowOnError(int status, string functionName)
    {
        if (status != ERROR_SUCCESS)
            throw new Win32Exception(status, $"{functionName} failed with error code {status}.");
    }
}

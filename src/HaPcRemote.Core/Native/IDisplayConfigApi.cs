using System.Runtime.Versioning;
using static HaPcRemote.Service.Native.DisplayConfigApi;

namespace HaPcRemote.Service.Native;

/// <summary>
/// Abstraction over the Windows CCD API for testability.
/// </summary>
[SupportedOSPlatform("windows")]
internal interface IDisplayConfigApi
{
    (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) QueryConfig(QueryDisplayConfigFlags flags);

    void ApplyConfig(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, SetDisplayConfigFlags flags);

    /// <summary>
    /// Returns friendly name, EDID manufacturer ID, and EDID product code in a single P/Invoke call.
    /// </summary>
    (string FriendlyName, ushort ManufacturerId, ushort ProductCodeId) GetTargetDeviceInfo(LUID adapterId, uint targetId);

    string GetSourceGdiName(LUID adapterId, uint sourceId);
}

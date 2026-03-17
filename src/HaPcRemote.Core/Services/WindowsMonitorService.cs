using System.Runtime.Versioning;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Native;
using Microsoft.Extensions.Logging;
using static HaPcRemote.Service.Native.DisplayConfigApi;

namespace HaPcRemote.Service.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsMonitorService : IMonitorService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly IDisplayConfigApi _api;
    private readonly ILogger<WindowsMonitorService> _logger;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private List<MonitorInfo>? _cachedMonitors;
    private DateTime _cacheTime;

    // Maps MonitorId (e.g. "GSM59A4") → native (adapterId, targetId) for path resolution
    private readonly Dictionary<string, (LUID adapterId, uint targetId)> _targetKeys = new(StringComparer.OrdinalIgnoreCase);

    public WindowsMonitorService(IDisplayConfigApi api, ILogger<WindowsMonitorService> logger)
    {
        _api = api;
        _logger = logger;
    }

    // ── Query ─────────────────────────────────────────────────────────

    public async Task<List<MonitorInfo>> GetMonitorsAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (_cachedMonitors is not null && DateTime.UtcNow - _cacheTime < CacheDuration)
                return _cachedMonitors;

            _cachedMonitors = QueryMonitors();
            _cacheTime = DateTime.UtcNow;
            return _cachedMonitors;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    internal List<MonitorInfo> QueryMonitors()
    {
        var (paths, modes) = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS);
        var monitors = new List<MonitorInfo>();
        var seen = new HashSet<(LUID adapterId, uint targetId)>();
        var edidCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        _targetKeys.Clear();

        foreach (var path in paths)
        {
            if (path.targetInfo.targetAvailable == 0)
                continue;

            var key = (path.targetInfo.adapterId, path.targetInfo.id);

            // Prefer the active path when deduplicating
            if (seen.Contains(key))
            {
                var existingIdx = monitors.FindIndex(m =>
                    _targetKeys.TryGetValue(m.MonitorId, out var k) && k == key);
                if (existingIdx >= 0 && !monitors[existingIdx].IsActive
                    && (path.flags & DISPLAYCONFIG_PATH_FLAGS.ACTIVE) != 0)
                {
                    var oldId = monitors[existingIdx].MonitorId;
                    monitors.RemoveAt(existingIdx);
                    _targetKeys.Remove(oldId);

                    // Roll back the EDID count so the replacement gets the same index
                    var oldBaseId = oldId.Contains('#') ? oldId[..oldId.IndexOf('#')] : oldId;
                    if (edidCounts.TryGetValue(oldBaseId, out var c))
                        edidCounts[oldBaseId] = c - 1;
                }
                else
                {
                    continue;
                }
            }

            seen.Add(key);

            var isActive = (path.flags & DISPLAYCONFIG_PATH_FLAGS.ACTIVE) != 0;

            string friendlyName;
            ushort edidMfg, edidProduct;
            string gdiName;

            try
            {
                (friendlyName, edidMfg, edidProduct) = _api.GetTargetDeviceInfo(path.targetInfo.adapterId, path.targetInfo.id);
                gdiName = isActive
                    ? _api.GetSourceGdiName(path.sourceInfo.adapterId, path.sourceInfo.id)
                    : "";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get device info for target {TargetId}", path.targetInfo.id);
                continue;
            }

            var baseId = FormatEdidId(edidMfg, edidProduct);
            edidCounts.TryGetValue(baseId, out var count);
            count++;
            edidCounts[baseId] = count;

            // First occurrence keeps the base ID; subsequent ones get #2, #3, etc.
            var monitorId = count == 1 ? baseId : $"{baseId}#{count}";
            _targetKeys[monitorId] = key;

            int width = 0, height = 0, hz = 0;
            var isPrimary = false;

            if (isActive && path.sourceInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
            {
                var sourceMode = FindSourceMode(modes, path.sourceInfo.modeInfoIdx);
                if (sourceMode.HasValue)
                {
                    width = (int)sourceMode.Value.width;
                    height = (int)sourceMode.Value.height;
                    isPrimary = sourceMode.Value.position.x == 0 && sourceMode.Value.position.y == 0;
                }
            }

            if (isActive && path.targetInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
            {
                var targetMode = FindTargetMode(modes, path.targetInfo.modeInfoIdx);
                if (targetMode.HasValue)
                    hz = targetMode.Value.targetVideoSignalInfo.vSyncFreq.ToHz();
            }

            // Fallback: use refreshRate from path target info
            if (hz == 0)
                hz = path.targetInfo.refreshRate.ToHz();

            monitors.Add(new MonitorInfo
            {
                Name = gdiName,
                MonitorId = monitorId,
                SerialNumber = null,
                MonitorName = friendlyName,
                Width = width,
                Height = height,
                DisplayFrequency = hz,
                IsActive = isActive,
                IsPrimary = isPrimary,
            });
        }

        return monitors;
    }

    // ── Control ───────────────────────────────────────────────────────

    public async Task EnableMonitorAsync(string id)
    {
        var monitors = await GetMonitorsAsync();
        var target = FindMonitor(monitors, id);
        _logger.LogInformation("Enabling monitor: {Name} ({Id})", target.MonitorName, target.MonitorId);

        var (paths, modes) = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS);
        var idx = FindPathIndex(paths, ResolveTargetKey(target));

        paths[idx].flags |= DISPLAYCONFIG_PATH_FLAGS.ACTIVE;
        // Invalidate mode indexes so Windows picks best available mode
        paths[idx].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        paths[idx].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;

        Apply(paths, modes);
        InvalidateCache();
    }

    public async Task DisableMonitorAsync(string id)
    {
        var monitors = await GetMonitorsAsync();
        var target = FindMonitor(monitors, id);
        _logger.LogInformation("Disabling monitor: {Name} ({Id})", target.MonitorName, target.MonitorId);

        var (paths, modes) = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS);
        var idx = FindPathIndex(paths, ResolveTargetKey(target));

        paths[idx].flags &= ~DISPLAYCONFIG_PATH_FLAGS.ACTIVE;

        Apply(paths, modes);
        InvalidateCache();
    }

    public async Task SetPrimaryAsync(string id)
    {
        var monitors = await GetMonitorsAsync();
        var target = FindMonitor(monitors, id);
        _logger.LogInformation("Setting primary monitor: {Name} ({Id})", target.MonitorName, target.MonitorId);

        var (paths, modes) = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ONLY_ACTIVE_PATHS);
        var targetKey = ResolveTargetKey(target);

        // Find the source mode for the target monitor
        POINTL targetPosition = default;
        uint? targetSourceIdx = null;

        for (var i = 0; i < paths.Length; i++)
        {
            if (paths[i].targetInfo.adapterId == targetKey.adapterId
                && paths[i].targetInfo.id == targetKey.targetId
                && paths[i].sourceInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
            {
                targetSourceIdx = paths[i].sourceInfo.modeInfoIdx;
                targetPosition = modes[paths[i].sourceInfo.modeInfoIdx].info.sourceMode.position;
                break;
            }
        }

        if (!targetSourceIdx.HasValue)
            throw new InvalidOperationException($"Could not find source mode for monitor '{id}'.");

        // Already primary
        if (targetPosition.x == 0 && targetPosition.y == 0)
        {
            _logger.LogDebug("Monitor {Id} is already primary", id);
            return;
        }

        // Offset all source modes so the target ends up at (0,0)
        var offsetX = targetPosition.x;
        var offsetY = targetPosition.y;

        for (var i = 0; i < modes.Length; i++)
        {
            if (modes[i].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.SOURCE)
            {
                modes[i].info.sourceMode.position.x -= offsetX;
                modes[i].info.sourceMode.position.y -= offsetY;
            }
        }

        Apply(paths, modes);
        InvalidateCache();
    }

    public async Task SoloMonitorAsync(string id)
    {
        var monitors = await GetMonitorsAsync();
        var target = FindMonitor(monitors, id);

        _logger.LogInformation("Solo monitor: {Name} ({Id})", target.MonitorName, target.MonitorId);

        var (paths, modes) = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS);
        var targetKey = ResolveTargetKey(target);
        uint? targetSourceModeIdx = null;

        // Single pass: activate target, deactivate others, capture source mode index
        for (var i = 0; i < paths.Length; i++)
        {
            var isTarget = paths[i].targetInfo.adapterId == targetKey.adapterId
                           && paths[i].targetInfo.id == targetKey.targetId;

            if (isTarget)
            {
                paths[i].flags |= DISPLAYCONFIG_PATH_FLAGS.ACTIVE;
                if (paths[i].sourceInfo.modeInfoIdx == DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
                    paths[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;

                if (targetSourceModeIdx is null
                    && paths[i].sourceInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID
                    && paths[i].sourceInfo.modeInfoIdx < modes.Length)
                {
                    targetSourceModeIdx = paths[i].sourceInfo.modeInfoIdx;
                }
            }
            else
            {
                paths[i].flags &= ~DISPLAYCONFIG_PATH_FLAGS.ACTIVE;
            }
        }

        // Set the target's source mode position to (0,0) so it becomes primary
        if (targetSourceModeIdx.HasValue)
            modes[targetSourceModeIdx.Value].info.sourceMode.position = default;

        Apply(paths, modes);
        InvalidateCache();

        // Verify
        var updated = QueryMonitors();
        var activeMonitors = updated.Where(m => m.IsActive).ToList();
        if (activeMonitors.Count != 1 || !MatchesId(activeMonitors[0], id))
        {
            var activeNames = string.Join(", ", activeMonitors.Select(m => $"{m.MonitorName} ({m.MonitorId})"));
            _logger.LogWarning("Solo monitor '{Id}' may have failed — active monitors: [{Active}]", id, activeNames);
        }
    }

    // ── Profiles (not supported) ──────────────────────────────────────

    public Task<List<MonitorProfile>> GetProfilesAsync() =>
        Task.FromResult(new List<MonitorProfile>());

    public Task ApplyProfileAsync(string profileName) =>
        throw new NotSupportedException("Monitor profiles are not supported with the native display API.");

    public Task SaveProfileAsync(string profileName) =>
        throw new NotSupportedException("Monitor profiles are not supported with the native display API.");

    // ── Helpers ────────────────────────────────────────────────────────

    internal void InvalidateCache()
    {
        _cachedMonitors = null;
    }

    private void Apply(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        const SetDisplayConfigFlags flags =
            SetDisplayConfigFlags.SDC_APPLY
            | SetDisplayConfigFlags.SDC_USE_SUPPLIED_DISPLAY_CONFIG
            | SetDisplayConfigFlags.SDC_ALLOW_CHANGES
            | SetDisplayConfigFlags.SDC_SAVE_TO_DATABASE;

        _api.ApplyConfig(paths, modes, flags);
    }

    private static int FindPathIndex(DISPLAYCONFIG_PATH_INFO[] paths, (LUID adapterId, uint targetId) targetKey)
    {
        for (var i = 0; i < paths.Length; i++)
        {
            if (paths[i].targetInfo.adapterId == targetKey.adapterId
                && paths[i].targetInfo.id == targetKey.targetId)
                return i;
        }

        throw new InvalidOperationException($"Could not find path for target {targetKey.targetId}.");
    }

    private (LUID adapterId, uint targetId) ResolveTargetKey(MonitorInfo monitor)
    {
        if (_targetKeys.TryGetValue(monitor.MonitorId, out var key))
            return key;

        throw new KeyNotFoundException($"No native target key found for monitor '{monitor.MonitorId}'. Was GetMonitorsAsync() called first?");
    }

    internal static MonitorInfo FindMonitor(List<MonitorInfo> monitors, string id) =>
        monitors.Find(m => MatchesId(m, id))
        ?? throw new KeyNotFoundException($"Monitor '{id}' not found.");

    internal static bool MatchesId(MonitorInfo m, string id) =>
        string.Equals(m.Name, id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(m.MonitorId, id, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrEmpty(m.SerialNumber)
            && string.Equals(m.SerialNumber, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Decodes EDID manufacturer ID (big-endian compressed PNP) + product code into "GSM59A4" format.
    /// </summary>
    internal static string FormatEdidId(ushort edidManufacturerId, ushort edidProductCodeId)
    {
        // EDID manufacturer ID is big-endian 3x5-bit compressed ASCII
        // Swap bytes from big-endian to native
        var mfg = (ushort)((edidManufacturerId >> 8) | (edidManufacturerId << 8));

        var c1 = (char)('A' + ((mfg >> 10) & 0x1F) - 1);
        var c2 = (char)('A' + ((mfg >> 5) & 0x1F) - 1);
        var c3 = (char)('A' + (mfg & 0x1F) - 1);

        return $"{c1}{c2}{c3}{edidProductCodeId:X4}";
    }

    private static DISPLAYCONFIG_SOURCE_MODE? FindSourceMode(DISPLAYCONFIG_MODE_INFO[] modes, uint index)
    {
        if (index >= modes.Length)
            return null;
        return modes[index].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.SOURCE
            ? modes[index].info.sourceMode
            : null;
    }

    private static DISPLAYCONFIG_TARGET_MODE? FindTargetMode(DISPLAYCONFIG_MODE_INFO[] modes, uint index)
    {
        if (index >= modes.Length)
            return null;
        return modes[index].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.TARGET
            ? modes[index].info.targetMode
            : null;
    }
}

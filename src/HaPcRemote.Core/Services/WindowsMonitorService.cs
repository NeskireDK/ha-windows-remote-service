using System.ComponentModel;
using System.Runtime.Versioning;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static HaPcRemote.Service.Native.DisplayConfigApi;

namespace HaPcRemote.Service.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsMonitorService : IMonitorService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly IDisplayConfigApi _api;
    private readonly ILogger<WindowsMonitorService> _logger;
    private readonly IOptionsMonitor<PcRemoteOptions> _options;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private List<MonitorInfo>? _cachedMonitors;
    private DateTime _cacheTime;

    // Maps MonitorId (e.g. "GSM59A4") → native (adapterId, targetId) for path resolution
    private readonly Dictionary<string, (LUID adapterId, uint targetId)> _targetKeys = new(StringComparer.OrdinalIgnoreCase);

    private const int MaxVerifyAttempts = 3;

    internal bool UseCompatibleMode => _options.CurrentValue.DisplaySwitching == DisplaySwitchingMode.Compatible;

    public WindowsMonitorService(IDisplayConfigApi api, ILogger<WindowsMonitorService> logger, IOptionsMonitor<PcRemoteOptions> options)
    {
        _api = api;
        _logger = logger;
        _options = options;
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
        _logger.LogDebug("QueryMonitors: starting enumeration");
        var (paths, modes) = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS);
        var monitors = new List<MonitorInfo>();
        var seen = new HashSet<(LUID adapterId, uint targetId)>();
        var edidCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        _targetKeys.Clear();

        _logger.LogDebug("QueryMonitors: processing {Count} paths", paths.Length);

        var unavailableCount = 0;
        var duplicateCount = 0;

        foreach (var path in paths)
        {
            if (path.targetInfo.targetAvailable == 0)
            {
                unavailableCount++;
                continue;
            }

            var key = (path.targetInfo.adapterId, path.targetInfo.id);
            var isActive = (path.flags & DISPLAYCONFIG_PATH_FLAGS.ACTIVE) != 0;

            // Prefer the active path when deduplicating
            if (seen.Contains(key))
            {
                var existingIdx = monitors.FindIndex(m =>
                    _targetKeys.TryGetValue(m.MonitorId, out var k) && k == key);
                if (existingIdx >= 0 && !monitors[existingIdx].IsActive && isActive)
                {
                    var oldId = monitors[existingIdx].MonitorId;
                    _logger.LogDebug("  Replacing inactive duplicate {OldId} with active path", oldId);
                    monitors.RemoveAt(existingIdx);
                    _targetKeys.Remove(oldId);

                    var oldBaseId = oldId.Contains('#') ? oldId[..oldId.IndexOf('#')] : oldId;
                    if (edidCounts.TryGetValue(oldBaseId, out var c))
                        edidCounts[oldBaseId] = c - 1;
                }
                else
                {
                    duplicateCount++;
                    continue;
                }
            }

            seen.Add(key);

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

            if (hz == 0)
                hz = path.targetInfo.refreshRate.ToHz();

            _logger.LogDebug(
                "  Monitor: id={MonitorId} name=\"{FriendlyName}\" gdi={Gdi} {W}x{H}@{Hz}Hz active={Active} primary={Primary}",
                monitorId, friendlyName, gdiName, width, height, hz, isActive, isPrimary);

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

        _logger.LogDebug("QueryMonitors: skipped {Unavailable} unavailable and {Duplicate} duplicate paths", unavailableCount, duplicateCount);
        _logger.LogDebug("QueryMonitors: found {Count} monitors", monitors.Count);
        foreach (var m in monitors)
            _logger.LogDebug("  {Id}: \"{Name}\" ({Gdi}) {W}x{H}@{Hz}Hz active={Active} primary={Primary}",
                m.MonitorId, m.MonitorName, m.Name, m.Width, m.Height, m.DisplayFrequency, m.IsActive, m.IsPrimary);

        return monitors;
    }

    // ── Control ───────────────────────────────────────────────────────

    public async Task EnableMonitorAsync(string id)
    {
        if (UseCompatibleMode) { await EnableCompatibleAsync(id); return; }

        var monitors = await GetMonitorsAsync();
        var target = FindMonitor(monitors, id);

        if (target.IsActive)
        {
            _logger.LogDebug("Monitor '{Id}' is already enabled, skipping", id);
            return;
        }

        _logger.LogInformation("Enabling monitor: {Name} ({Id})", target.MonitorName, target.MonitorId);

        var targetKey = ResolveTargetKey(target);
        ApplyWithRetry(() => BuildEnableConfig(targetKey), SetDisplayConfigFlags.SDC_TOPOLOGY_EXTEND);
        InvalidateCache();
    }

    public async Task DisableMonitorAsync(string id)
    {
        if (UseCompatibleMode) { await DisableCompatibleAsync(id); return; }

        var monitors = await GetMonitorsAsync();
        var target = FindMonitor(monitors, id);

        if (!target.IsActive)
        {
            _logger.LogDebug("Monitor '{Id}' is already disabled, skipping", id);
            return;
        }

        _logger.LogInformation("Disabling monitor: {Name} ({Id})", target.MonitorName, target.MonitorId);

        var targetKey = ResolveTargetKey(target);
        ApplyWithRetry(() => BuildDisableConfig(targetKey));
        InvalidateCache();
    }

    public async Task SetPrimaryAsync(string id)
    {
        if (UseCompatibleMode) { await SetPrimaryCompatibleAsync(id); return; }

        var monitors = await GetMonitorsAsync();
        var target = FindMonitor(monitors, id);
        _logger.LogInformation("Setting primary monitor: {Name} ({Id})", target.MonitorName, target.MonitorId);

        // Check if already primary before entering retry loop
        if (target.IsPrimary)
        {
            _logger.LogDebug("Monitor {Id} is already primary", id);
            return;
        }

        var targetKey = ResolveTargetKey(target);
        ApplyWithRetry(() => BuildSetPrimaryConfig(targetKey, id));
        InvalidateCache();
    }

    private (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) BuildEnableConfig(
        (LUID adapterId, uint targetId) targetKey)
    {
        // TODO #121: Re-enable QDC_DATABASE_CURRENT with UI toggle
        // Currently disabled — causes error 87 on SetDisplayConfig when no saved layout exists
        // try
        // {
        //     config = _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT);
        // }
        // catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_INVALID_PARAMETER)
        // {
        //     _logger.LogDebug("QDC_DATABASE_CURRENT unavailable, falling back to QDC_ALL_PATHS");
        //     config = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS);
        // }
        var (paths, modes) = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS);
        var idx = FindPathIndex(paths, targetKey);
        paths[idx].flags |= DISPLAYCONFIG_PATH_FLAGS.ACTIVE;
        paths[idx].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        paths[idx].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        return (paths, modes);
    }

    private (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) BuildDisableConfig(
        (LUID adapterId, uint targetId) targetKey)
    {
        var (paths, modes) = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS);
        var idx = FindPathIndex(paths, targetKey);
        paths[idx].flags &= ~DISPLAYCONFIG_PATH_FLAGS.ACTIVE;
        return (paths, modes);
    }

    private (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) BuildSetPrimaryConfig(
        (LUID adapterId, uint targetId) targetKey, string id)
    {
        var (paths, modes) = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ONLY_ACTIVE_PATHS);

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

        return (paths, modes);
    }

    public async Task SoloMonitorAsync(string id)
    {
        if (UseCompatibleMode) { await SoloCompatibleAsync(id); return; }

        var monitors = await GetMonitorsAsync();
        var target = FindMonitor(monitors, id);

        _logger.LogInformation("Solo monitor: {Name} ({Id})", target.MonitorName, target.MonitorId);

        for (var round = 0; round < MaxVerifyAttempts; round++)
        {
            if (round > 0)
            {
                _logger.LogWarning("Solo monitor '{Id}' — retrying (round {Round}/{Max})", id, round + 1, MaxVerifyAttempts);
                if (StepDelayMs > 0)
                    await Task.Delay(StepDelayMs);
                // Re-query to get fresh adapter IDs
                monitors = QueryMonitors();
                target = FindMonitor(monitors, id);
            }

            var targetKey = ResolveTargetKey(target);
            ApplyWithRetry(() => BuildSoloConfig(targetKey, target.MonitorId));
            InvalidateCache();

            var updated = QueryMonitors();
            var activeMonitors = updated.Where(m => m.IsActive).ToList();
            if (activeMonitors.Count == 1 && MatchesId(activeMonitors[0], id))
            {
                _logger.LogDebug("Solo monitor '{Id}' verified successfully", id);
                return;
            }

            var activeNames = string.Join(", ", activeMonitors.Select(m => $"{m.MonitorName} ({m.MonitorId})"));
            _logger.LogWarning("Solo monitor '{Id}' may have failed — active monitors: [{Active}]", id, activeNames);
        }

        _logger.LogWarning("Solo monitor '{Id}' could not be verified after {Max} rounds", id, MaxVerifyAttempts);
    }

    private (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) BuildSoloConfig(
        (LUID adapterId, uint targetId) targetKey, string monitorId)
    {
        var (paths, modes) = _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS);

        var activeCount = 0;
        var inactiveCount = 0;

        for (var i = 0; i < paths.Length; i++)
        {
            var isTarget = paths[i].targetInfo.adapterId == targetKey.adapterId
                           && paths[i].targetInfo.id == targetKey.targetId;

            if (isTarget)
            {
                paths[i].flags |= DISPLAYCONFIG_PATH_FLAGS.ACTIVE;
                paths[i].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                paths[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                activeCount++;
            }
            else
            {
                paths[i].flags &= ~DISPLAYCONFIG_PATH_FLAGS.ACTIVE;
                paths[i].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                paths[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                inactiveCount++;
            }
        }

        _logger.LogDebug("Solo applying: {Active} active, {Inactive} inactive paths for target {Id}",
            activeCount, inactiveCount, monitorId);

        return (paths, modes);
    }

    // ── Compatible mode ────────────────────────────────────────────────

    private async Task SoloCompatibleAsync(string id)
    {
        for (var round = 0; round < MaxVerifyAttempts; round++)
        {
            if (round > 0)
            {
                _logger.LogWarning("Solo compatible '{Id}' — retrying (round {Round}/{Max})", id, round + 1, MaxVerifyAttempts);
                if (StepDelayMs > 0)
                    await Task.Delay(StepDelayMs);
            }

            _logger.LogInformation("Solo monitor (compatible): {Id}", id);

            // Step 1: Enable target if not active
            var monitors = InvalidateAndQueryMonitors();
            var target = FindMonitor(monitors, id);
            if (!target.IsActive)
            {
                await EnableCompatibleAsync(id);
                monitors = InvalidateAndQueryMonitors();
                target = FindMonitor(monitors, id);
            }

            // Step 2: Set primary (doesn't change which monitors are active)
            if (!target.IsPrimary)
                await SetPrimaryCompatibleAsync(id);

            // Step 3: Disable each other active monitor
            var others = monitors.Where(m => m.IsActive && !MatchesId(m, id)).ToList();
            foreach (var other in others)
            {
                await DisableCompatibleAsync(other.MonitorId);
            }

            // Final verification
            var final_ = InvalidateAndQueryMonitors();
            var active = final_.Where(m => m.IsActive).ToList();
            if (active.Count == 1 && MatchesId(active[0], id))
            {
                _logger.LogDebug("Solo compatible '{Id}' verified successfully", id);
                return;
            }

            var activeNames = string.Join(", ", active.Select(m => $"{m.MonitorName} ({m.MonitorId})"));
            _logger.LogWarning("Solo compatible '{Id}' — unexpected result: [{Active}]", id, activeNames);
        }

        _logger.LogWarning("Solo compatible '{Id}' could not be verified after {Max} rounds", id, MaxVerifyAttempts);
    }

    private async Task EnableCompatibleAsync(string id)
    {
        var monitors = InvalidateAndQueryMonitors();
        var target = FindMonitor(monitors, id);

        if (target.IsActive)
        {
            _logger.LogDebug("Monitor '{Id}' is already enabled, skipping (compatible)", id);
            return;
        }

        _logger.LogInformation("Enabling monitor (compatible): {Name} ({Id})", target.MonitorName, target.MonitorId);

        var targetKey = ResolveTargetKey(target);
        await ApplyStepWithVerification(
            () => BuildEnableConfig(targetKey),
            () => FindMonitor(QueryMonitors(), id).IsActive,
            $"Enable({id})",
            SetDisplayConfigFlags.SDC_TOPOLOGY_EXTEND);
    }

    private async Task SetPrimaryCompatibleAsync(string id)
    {
        var monitors = InvalidateAndQueryMonitors();
        var target = FindMonitor(monitors, id);

        if (!target.IsActive)
        {
            _logger.LogInformation("Monitor '{Id}' not active — enabling first (compatible)", id);
            await EnableCompatibleAsync(id);
            monitors = InvalidateAndQueryMonitors();
            target = FindMonitor(monitors, id);
        }

        if (target.IsPrimary)
        {
            _logger.LogDebug("Monitor {Id} is already primary (compatible)", id);
            return;
        }

        _logger.LogInformation("Setting primary (compatible): {Name} ({Id})", target.MonitorName, target.MonitorId);

        var targetKey = ResolveTargetKey(target);
        await ApplyStepWithVerification(
            () => BuildSetPrimaryConfig(targetKey, id),
            () => FindMonitor(QueryMonitors(), id).IsPrimary,
            $"SetPrimary({id})");
    }

    private async Task DisableCompatibleAsync(string id)
    {
        var monitors = InvalidateAndQueryMonitors();
        var target = FindMonitor(monitors, id);

        if (!target.IsActive)
        {
            _logger.LogDebug("Monitor '{Id}' is already disabled, skipping (compatible)", id);
            return;
        }

        // If disabling the primary, move primary to another active monitor first
        if (target.IsPrimary)
        {
            var other = monitors.FirstOrDefault(m => m.IsActive && !MatchesId(m, id));
            if (other != null)
            {
                _logger.LogInformation("Shuffling primary to {Other} before disabling {Id} (compatible)", other.MonitorId, id);
                await SetPrimaryCompatibleAsync(other.MonitorId);
            }
        }

        _logger.LogInformation("Disabling monitor (compatible): {Name} ({Id})", target.MonitorName, target.MonitorId);

        var targetKey = ResolveTargetKey(target);
        await ApplyStepWithVerification(
            () => BuildDisableConfig(targetKey),
            () => !FindMonitor(QueryMonitors(), id).IsActive,
            $"Disable({id})");
    }

    private async Task ApplyStepWithVerification(
        Func<(DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes)> buildConfig,
        Func<bool> verify,
        string stepName,
        SetDisplayConfigFlags extraFlags = 0)
    {
        for (var attempt = 0; attempt < MaxVerifyAttempts; attempt++)
        {
            ApplyWithRetry(buildConfig, extraFlags);
            InvalidateCache();

            if (attempt > 0 && StepDelayMs > 0)
                await Task.Delay(StepDelayMs);

            if (verify())
            {
                _logger.LogDebug("Step {Step} verified successfully", stepName);
                return;
            }

            _logger.LogWarning("Step {Step} verification failed, retrying ({Attempt}/{Max})",
                stepName, attempt + 1, MaxVerifyAttempts);

            if (StepDelayMs > 0)
                await Task.Delay(StepDelayMs);
        }

        _logger.LogWarning("Step {Step} could not be verified after {Max} attempts", stepName, MaxVerifyAttempts);
    }

    private List<MonitorInfo> InvalidateAndQueryMonitors()
    {
        InvalidateCache();
        return QueryMonitors();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    internal void InvalidateCache()
    {
        _cachedMonitors = null;
    }

    internal int[] RetryDelaysMs = [500, 1000, 2000];
    internal int StepDelayMs = 500;

    /// <summary>
    /// Applies a display config change with retry logic for transient driver errors.
    /// Error 31 (GEN_FAILURE): transient driver timing — wait and retry with same config.
    /// Error 87 (INVALID_PARAMETER): stale adapter LUIDs — re-query and rebuild config via <paramref name="buildConfig"/>.
    /// </summary>
    private void ApplyWithRetry(
        Func<(DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes)> buildConfig,
        SetDisplayConfigFlags extraFlags = 0)
    {
        var flags =
            SetDisplayConfigFlags.SDC_APPLY
            | SetDisplayConfigFlags.SDC_USE_SUPPLIED_DISPLAY_CONFIG
            | SetDisplayConfigFlags.SDC_ALLOW_CHANGES
            | SetDisplayConfigFlags.SDC_SAVE_TO_DATABASE
            | extraFlags;

        var (paths, modes) = buildConfig();

        for (var attempt = 0;; attempt++)
        {
            try
            {
                _api.ApplyConfig(paths, modes, flags);
                return;
            }
            catch (Win32Exception ex) when (attempt < RetryDelaysMs.Length &&
                                            (ex.NativeErrorCode == ERROR_GEN_FAILURE ||
                                             ex.NativeErrorCode == ERROR_INVALID_PARAMETER))
            {
                var delay = RetryDelaysMs[attempt];
                _logger.LogWarning(
                    "SetDisplayConfig failed with error {Code} on attempt {Attempt}, retrying in {Delay}ms",
                    ex.NativeErrorCode, attempt + 1, delay);

                Thread.Sleep(delay);

                if (ex.NativeErrorCode == ERROR_INVALID_PARAMETER)
                {
                    _logger.LogDebug("Re-querying display config due to stale adapter IDs");
                    (paths, modes) = buildConfig();
                }
            }
        }
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
        MonitorMatchHelper.FindMonitor(monitors, id);

    internal static bool MatchesId(MonitorInfo m, string id) =>
        MonitorMatchHelper.MatchesId(m, id);

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

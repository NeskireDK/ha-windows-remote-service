using HaPcRemote.Service.Models;

namespace HaPcRemote.Service.Services;

public static class MonitorMatchHelper
{
    public static MonitorInfo FindMonitor(List<MonitorInfo> monitors, string id) =>
        monitors.Find(m => MatchesId(m, id))
        ?? throw new KeyNotFoundException($"Monitor '{id}' not found.");

    public static bool MatchesId(MonitorInfo m, string id) =>
        string.Equals(m.Name, id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(m.MonitorId, id, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrEmpty(m.SerialNumber)
            && string.Equals(m.SerialNumber, id, StringComparison.OrdinalIgnoreCase));
}

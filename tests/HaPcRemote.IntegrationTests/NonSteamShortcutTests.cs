using System.Net;
using HaPcRemote.IntegrationTests.Models;
using Shouldly;

namespace HaPcRemote.IntegrationTests;

/// <summary>
/// Diagnostic tests for non-Steam shortcut (Bloodborne) tracking and shutdown.
/// Run while the game is active to capture detection state.
/// </summary>
[Collection("Service")]
public class NonSteamShortcutTests : IntegrationTestBase
{
    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetGames_ContainsNonSteamShortcuts()
    {
        var response = await GetRawAsync("/api/steam/games");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await DeserializeAsync<ApiResponse<List<SteamGame>>>(response);
        result.ShouldNotBeNull();
        result.Data.ShouldNotBeNull();

        var shortcuts = result.Data.Where(g => g.IsShortcut).ToList();

        // Dump all shortcuts for diagnostics
        foreach (var s in shortcuts)
        {
            Console.WriteLine($"[Shortcut] AppId={s.AppId} Name={s.Name} ExePath={s.ExePath ?? "(null)"} LaunchOptions={s.LaunchOptions ?? "(null)"}");
        }

        shortcuts.ShouldNotBeEmpty("No non-Steam shortcuts found in game list");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetGames_BloodborneShortcutExists()
    {
        var response = await GetRawAsync("/api/steam/games");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await DeserializeAsync<ApiResponse<List<SteamGame>>>(response);
        result.ShouldNotBeNull();
        result.Data.ShouldNotBeNull();

        var bloodborne = result.Data
            .Where(g => g.IsShortcut)
            .FirstOrDefault(g => g.Name.Contains("Bloodborne", StringComparison.OrdinalIgnoreCase)
                              || g.Name.Contains("blood", StringComparison.OrdinalIgnoreCase));

        if (bloodborne == null)
        {
            // Dump all shortcuts so we can identify it manually
            var shortcuts = result.Data.Where(g => g.IsShortcut).ToList();
            Console.WriteLine($"Bloodborne not found. All shortcuts ({shortcuts.Count}):");
            foreach (var s in shortcuts)
                Console.WriteLine($"  [{s.AppId}] {s.Name} | ExePath={s.ExePath ?? "(null)"}");

            shortcuts.ShouldNotBeEmpty("No shortcuts at all — is Bloodborne added as a non-Steam game?");
            Assert.Fail("Bloodborne shortcut not found by name. Check console output for available shortcuts.");
        }

        Console.WriteLine($"[Bloodborne] AppId={bloodborne.AppId} Name={bloodborne.Name}");
        Console.WriteLine($"  ExePath={bloodborne.ExePath ?? "(null)"}");
        Console.WriteLine($"  LaunchOptions={bloodborne.LaunchOptions ?? "(null)"}");
        Console.WriteLine($"  IsShortcut={bloodborne.IsShortcut}");
        Console.WriteLine($"  AppId is negative (valid shortcut): {bloodborne.AppId < 0}");

        bloodborne.IsShortcut.ShouldBeTrue();
        bloodborne.AppId.ShouldBeLessThan(0, "Non-Steam shortcut AppId should be negative (high bit set)");
        bloodborne.ExePath.ShouldNotBeNullOrWhiteSpace("ExePath is required for process-based detection");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetRunning_DetectsNonSteamGame()
    {
        var response = await GetRawAsync("/api/steam/running");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await DeserializeAsync<ApiResponse<SteamRunningGame>>(response);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();

        Console.WriteLine($"Running game data is null: {result.Data == null}");

        if (result.Data == null)
        {
            Console.WriteLine("NO RUNNING GAME DETECTED — this is the bug.");
            Console.WriteLine("The service does not see Bloodborne as running.");
            Console.WriteLine("Possible causes:");
            Console.WriteLine("  1. ExePath in shortcuts.vdf doesn't match actual running process path");
            Console.WriteLine("  2. Steam reports appId=0 and process fallback fails to match");
            Console.WriteLine("  3. Game process name differs from shortcut exe");
            Assert.Fail("No running game detected while Bloodborne should be active");
        }

        Console.WriteLine($"[Running] AppId={result.Data.AppId} Name={result.Data.Name} ProcessId={result.Data.ProcessId?.ToString() ?? "(null)"}");
        Console.WriteLine($"  AppId is negative (shortcut): {result.Data.AppId < 0}");
        Console.WriteLine($"  Has ProcessId: {result.Data.ProcessId != null}");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetSystemState_ShowsSteamAndRunningState()
    {
        var response = await GetRawAsync("/api/system/state");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        Console.WriteLine("=== Full System State ===");
        Console.WriteLine(json);
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetDebugLogs_ShowsNonSteamDetection()
    {
        // First trigger a running game check to generate logs
        await GetRawAsync("/api/steam/running");

        // Then fetch logs filtered to Steam detection
        var response = await GetRawAsync("/api/debug/logs?lines=100&category=SteamService");

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            Console.WriteLine("Debug logs only accessible from localhost — skipping");
            return;
        }

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response.Content.ReadAsStringAsync();
            Console.WriteLine("=== SteamService Logs (last 100 lines) ===");
            Console.WriteLine(json);
        }
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetGames_ShortcutExePathsAreValid()
    {
        var response = await GetRawAsync("/api/steam/games");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await DeserializeAsync<ApiResponse<List<SteamGame>>>(response);
        result.ShouldNotBeNull();
        result.Data.ShouldNotBeNull();

        var shortcuts = result.Data.Where(g => g.IsShortcut).ToList();

        Console.WriteLine($"Shortcut ExePath analysis ({shortcuts.Count} shortcuts):");
        foreach (var s in shortcuts)
        {
            var hasExe = !string.IsNullOrWhiteSpace(s.ExePath);
            Console.WriteLine($"  [{s.AppId}] {s.Name}");
            Console.WriteLine($"    ExePath: {s.ExePath ?? "(null)"}");
            Console.WriteLine($"    HasExePath: {hasExe}");
            if (hasExe)
            {
                Console.WriteLine($"    ExeFileName: {Path.GetFileName(s.ExePath!)}");
            }
        }
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task Stop_WhenNonSteamGameRunning_StopsIt()
    {
        // First verify something is running
        var runningResponse = await GetRawAsync("/api/steam/running");
        var running = await DeserializeAsync<ApiResponse<SteamRunningGame>>(runningResponse);

        if (running?.Data == null)
        {
            Console.WriteLine("No game detected as running — cannot test stop");
            return;
        }

        Console.WriteLine($"Game running: [{running.Data.AppId}] {running.Data.Name} (pid={running.Data.ProcessId?.ToString() ?? "null"})");

        var stopResponse = await PostRawAsync("/api/steam/stop");
        stopResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var stopResult = await DeserializeAsync<ApiResponse>(stopResponse);
        stopResult.ShouldNotBeNull();
        Console.WriteLine($"Stop result: success={stopResult.Success} message={stopResult.Message}");

        // Verify it's no longer running
        await Task.Delay(2000);
        var afterStop = await GetRawAsync("/api/steam/running");
        var afterResult = await DeserializeAsync<ApiResponse<SteamRunningGame>>(afterStop);
        Console.WriteLine($"After stop — running game: {(afterResult?.Data == null ? "none" : afterResult.Data.Name)}");
    }
}

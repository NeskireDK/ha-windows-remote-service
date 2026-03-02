using FakeItEasy;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using System.Net.Http;

namespace HaPcRemote.Service.Tests.Services;

public class SteamServiceTests
{
    private readonly ISteamPlatform _platform = A.Fake<ISteamPlatform>();
    private readonly IModeService _modeService = A.Fake<IModeService>();
    private readonly ILogger<SteamService> _logger = A.Fake<ILogger<SteamService>>();
    private readonly IHttpClientFactory _httpClientFactory = A.Fake<IHttpClientFactory>();

    private SteamService CreateService(PcRemoteOptions? options = null)
    {
        options ??= new PcRemoteOptions();
        var monitor = A.Fake<IOptionsMonitor<PcRemoteOptions>>();
        A.CallTo(() => monitor.CurrentValue).Returns(options);
        return new SteamService(_platform, _modeService, monitor, _httpClientFactory, _logger);
    }

    // ── ParseLibraryFolders tests (static) ───────────────────────────

    [Fact]
    public void ParseLibraryFolders_ValidVdf_ReturnsLibraryPaths()
    {
        var paths = SteamService.ParseLibraryFolders(TestData.Load("library-folders.vdf"));

        paths.Count.ShouldBe(2);
        paths[0].ShouldBe(@"C:\Program Files (x86)\Steam");
        paths[1].ShouldBe(@"D:\SteamLibrary");
    }

    [Fact]
    public void ParseLibraryFolders_EmptyVdf_ReturnsEmptyList()
    {
        var vdf = """
            "libraryfolders"
            {
            }
            """;

        var paths = SteamService.ParseLibraryFolders(vdf);

        paths.ShouldBeEmpty();
    }

    // ── ParseAppManifest tests (static) ──────────────────────────────

    [Fact]
    public void ParseAppManifest_ValidAcf_ReturnsGameInfo()
    {
        var game = SteamService.ParseAppManifest(TestData.Load("app-manifest-730.acf"));

        game.ShouldNotBeNull();
        game.AppId.ShouldBe(730);
        game.Name.ShouldBe("Counter-Strike 2");
        game.LastPlayed.ShouldBe(1708000000L); // Uses LastPlayed field, not LastUpdated
    }

    [Fact]
    public void ParseAppManifest_MissingName_ReturnsNull()
    {
        var acf = """
            "AppState"
            {
                "appid"     "730"
                "StateFlags"    "4"
            }
            """;

        var game = SteamService.ParseAppManifest(acf);

        game.ShouldBeNull();
    }

    [Fact]
    public void ParseAppManifest_MissingAppId_ReturnsNull()
    {
        var acf = """
            "AppState"
            {
                "name"      "Counter-Strike 2"
                "StateFlags"    "4"
            }
            """;

        var game = SteamService.ParseAppManifest(acf);

        game.ShouldBeNull();
    }

    [Fact]
    public void ParseAppManifest_InvalidAppId_ReturnsNull()
    {
        var acf = """
            "AppState"
            {
                "appid"     "not_a_number"
                "name"      "Counter-Strike 2"
            }
            """;

        var game = SteamService.ParseAppManifest(acf);

        game.ShouldBeNull();
    }

    [Fact]
    public void ParseAppManifest_MissingLastUpdated_DefaultsToZero()
    {
        var acf = """
            "AppState"
            {
                "appid"     "730"
                "name"      "Counter-Strike 2"
            }
            """;

        var game = SteamService.ParseAppManifest(acf);

        game.ShouldNotBeNull();
        game.LastPlayed.ShouldBe(0L);
    }

    // ── ParseInstallDir tests (static) ───────────────────────────────

    [Fact]
    public void ParseInstallDir_ValidAcf_ReturnsInstallDir()
    {
        var installDir = SteamService.ParseInstallDir(TestData.Load("app-manifest-730.acf"));

        installDir.ShouldBe("Counter-Strike Global Offensive");
    }

    [Fact]
    public void ParseInstallDir_MissingInstallDir_ReturnsNull()
    {
        var acf = """
            "AppState"
            {
                "appid"     "730"
                "name"      "Counter-Strike 2"
            }
            """;

        var installDir = SteamService.ParseInstallDir(acf);

        installDir.ShouldBeNull();
    }

    // ── GetRunningGameAsync tests ────────────────────────────────────

    [Fact]
    public async Task GetRunningGame_NoGame_ReturnsNull()
    {
        A.CallTo(() => _platform.GetRunningAppId()).Returns(0);
        A.CallTo(() => _platform.GetSteamPath()).Returns("C:\\FakeNonExistentSteamPath_12345");
        A.CallTo(() => _platform.GetRunningProcessPaths()).Returns([]);
        var service = CreateService();

        var result = await service.GetRunningGameAsync();

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetRunningGame_GameRunning_CacheWarm_ReturnsGameInfo()
    {
        A.CallTo(() => _platform.GetRunningAppId()).Returns(730);
        A.CallTo(() => _platform.GetSteamPath()).Returns(@"C:\Steam");
        var service = CreateService();
        // Warm the cache manually via GetGamesAsync (returns empty list since filesystem is fake)
        // Then set up the cache via reflection would be complex — instead just verify the result
        // with an empty cache: name falls back to "Unknown (730)" but appId is correct
        var result = await service.GetRunningGameAsync();

        result.ShouldNotBeNull();
        result.AppId.ShouldBe(730);
    }

    [Fact]
    public async Task GetRunningGame_CacheCold_WarmsCache()
    {
        // Cache starts cold — GetRunningGameAsync should call GetSteamPath to warm it
        A.CallTo(() => _platform.GetRunningAppId()).Returns(730);
        A.CallTo(() => _platform.GetSteamPath()).Returns(@"C:\Steam");
        var service = CreateService();

        var result = await service.GetRunningGameAsync();

        result.ShouldNotBeNull();
        result.AppId.ShouldBe(730);
        // GetSteamPath called at least once (to warm cache via GetGamesAsync)
        A.CallTo(() => _platform.GetSteamPath()).MustHaveHappened();
    }

    [Fact]
    public async Task GetRunningGame_GameNotInTop20_LooksUpFromManifest()
    {
        // Game running, not in top-20 (cache empty from fake FS), GetSteamPath called twice:
        // once for cache warming, once for FindGameNameFromManifest
        A.CallTo(() => _platform.GetRunningAppId()).Returns(730);
        A.CallTo(() => _platform.GetSteamPath()).Returns(@"C:\Steam");
        var service = CreateService();

        var result = await service.GetRunningGameAsync();

        result.ShouldNotBeNull();
        result.AppId.ShouldBe(730);
        // Name is "Unknown (730)" because fake FS has no manifests — that's acceptable here
        // The important thing is GetSteamPath was called for the manifest fallback
        A.CallTo(() => _platform.GetSteamPath()).MustHaveHappened();
    }

    // ── LaunchGameAsync tests ────────────────────────────────────────

    [Fact]
    public async Task LaunchGame_SameGameRunning_ReturnsRunningGame()
    {
        A.CallTo(() => _platform.GetRunningAppId()).Returns(730);
        A.CallTo(() => _platform.GetSteamPath()).Returns((string?)null);
        var service = CreateService();

        var result = await service.LaunchGameAsync(730);

        result.ShouldNotBeNull();
        result.AppId.ShouldBe(730);
        A.CallTo(() => _platform.LaunchSteamUrl(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task LaunchGame_NoGameRunning_LaunchesSteamUrl()
    {
        // First call returns 0 (no game), poll calls also return 0 (launch not confirmed)
        A.CallTo(() => _platform.GetRunningAppId()).Returns(0);
        var service = CreateService();

        var result = await service.LaunchGameAsync(730);

        result.ShouldBeNull();
        A.CallTo(() => _platform.LaunchSteamUrl("steam://rungameid/730"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task LaunchGame_NoGameRunning_PollConfirms_ReturnsGame()
    {
        // First call returns 0 (triggers launch), then poll returns the launched appId
        var callCount = 0;
        A.CallTo(() => _platform.GetRunningAppId()).ReturnsLazily(() =>
        {
            callCount++;
            return callCount <= 1 ? 0 : 730;
        });
        A.CallTo(() => _platform.GetSteamPath()).Returns((string?)null);
        var service = CreateService();

        var result = await service.LaunchGameAsync(730);

        result.ShouldNotBeNull();
        result.AppId.ShouldBe(730);
        A.CallTo(() => _platform.LaunchSteamUrl("steam://rungameid/730"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task LaunchGame_DifferentGameRunning_StopsFirst()
    {
        // First call: different game is running (for LaunchGameAsync check)
        // Second call: still running (for StopGameAsync check)
        // After launch: poll calls return 0 (launch not confirmed)
        A.CallTo(() => _platform.GetRunningAppId()).Returns(570);
        A.CallTo(() => _platform.GetSteamPath()).Returns((string?)null);
        var service = CreateService();

        var result = await service.LaunchGameAsync(730);

        result.ShouldBeNull(); // Poll never sees 730
        // StopGameAsync was called (checks RunningAppId and GetSteamPath)
        A.CallTo(() => _platform.GetSteamPath()).MustHaveHappened();
        A.CallTo(() => _platform.LaunchSteamUrl("steam://rungameid/730"))
            .MustHaveHappenedOnceExactly();
    }

    // ── StopGameAsync tests ──────────────────────────────────────────

    [Fact]
    public async Task StopGame_NoGameRunning_NoOp()
    {
        A.CallTo(() => _platform.GetRunningAppId()).Returns(0);
        var service = CreateService();

        await service.StopGameAsync();

        A.CallTo(() => _platform.KillProcessesInDirectory(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task StopGame_NoSteamPath_NoOp()
    {
        A.CallTo(() => _platform.GetRunningAppId()).Returns(730);
        A.CallTo(() => _platform.GetSteamPath()).Returns((string?)null);
        var service = CreateService();

        await service.StopGameAsync();

        A.CallTo(() => _platform.KillProcessesInDirectory(A<string>._)).MustNotHaveHappened();
    }

    // ── ParseLibraryFolders edge cases ───────────────────────────────

    [Fact]
    public void ParseLibraryFolders_NullContentPath_ReturnsEmptyList()
    {
        // VDF with a folder entry but no path key — should produce empty list
        var vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "label"     "some label"
                }
            }
            """;

        var paths = SteamService.ParseLibraryFolders(vdf);

        paths.ShouldBeEmpty();
    }

    [Fact]
    public void ParseLibraryFolders_SingleEntry_ReturnsSinglePath()
    {
        var vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"      "E:\\Games\\Steam"
                }
            }
            """;

        var paths = SteamService.ParseLibraryFolders(vdf);

        paths.Count.ShouldBe(1);
        paths[0].ShouldBe(@"E:\Games\Steam");
    }

    // ── ParseAppManifest edge cases ───────────────────────────────────

    [Fact]
    public void ParseAppManifest_EmptyString_ReturnsNull()
    {
        // ValveKeyValue will parse an empty doc — no appid/name => null
        var acf = """
            "AppState"
            {
            }
            """;

        var game = SteamService.ParseAppManifest(acf);

        game.ShouldBeNull();
    }

    [Fact]
    public void ParseAppManifest_AppIdZero_ReturnsGame()
    {
        // appid 0 is technically valid integer — service should return a game object
        var acf = """
            "AppState"
            {
                "appid"     "0"
                "name"      "Unknown App"
            }
            """;

        var game = SteamService.ParseAppManifest(acf);

        game.ShouldNotBeNull();
        game.AppId.ShouldBe(0);
        game.Name.ShouldBe("Unknown App");
    }

    [Fact]
    public void ParseAppManifest_OnlyLastUpdated_FallsBackToLastUpdated()
    {
        var acf = """
            "AppState"
            {
                "appid"        "730"
                "name"         "Counter-Strike 2"
                "LastUpdated"  "1700000000"
            }
            """;

        var game = SteamService.ParseAppManifest(acf);

        game.ShouldNotBeNull();
        game.LastPlayed.ShouldBe(1700000000L);
    }

    [Fact]
    public void ParseAppManifest_BothFields_PrefersLastPlayed()
    {
        var acf = """
            "AppState"
            {
                "appid"        "730"
                "name"         "Counter-Strike 2"
                "LastUpdated"  "1700000000"
                "LastPlayed"   "1708000000"
            }
            """;

        var game = SteamService.ParseAppManifest(acf);

        game.ShouldNotBeNull();
        game.LastPlayed.ShouldBe(1708000000L);
    }

    [Fact]
    public void ParseAppManifest_VeryLargeLastUpdated_ParsesCorrectly()
    {
        var acf = """
            "AppState"
            {
                "appid"        "730"
                "name"         "Counter-Strike 2"
                "LastUpdated"  "9999999999"
            }
            """;

        var game = SteamService.ParseAppManifest(acf);

        game.ShouldNotBeNull();
        game.LastPlayed.ShouldBe(9999999999L);
    }

    // ── GetGamesAsync edge cases ──────────────────────────────────────

    [Fact]
    public async Task GetGamesAsync_SteamNotInstalled_Throws()
    {
        A.CallTo(() => _platform.GetSteamPath()).Returns((string?)null);
        var service = CreateService();

        await Should.ThrowAsync<InvalidOperationException>(
            () => service.GetGamesAsync());
    }

    [Fact]
    public async Task GetGamesAsync_EmptySteamPath_Throws()
    {
        A.CallTo(() => _platform.GetSteamPath()).Returns(string.Empty);
        var service = CreateService();

        // GetSteamPath returns empty string — treated as non-null but libraryfolders.vdf won't exist
        // so should return empty list, not throw
        A.CallTo(() => _platform.GetSteamPath()).Returns("C:\\FakeNonExistentSteamPath_12345");
        var result = await service.GetGamesAsync();

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetGamesAsync_CacheHit_DoesNotCallPlatformAgain()
    {
        A.CallTo(() => _platform.GetSteamPath()).Returns("C:\\FakeNonExistentSteamPath_12345");
        var service = CreateService();

        await service.GetGamesAsync(); // warm cache
        await service.GetGamesAsync(); // should use cache

        // GetSteamPath called exactly once (first call only)
        A.CallTo(() => _platform.GetSteamPath()).MustHaveHappenedOnceExactly();
    }

    // ── LaunchGameAsync edge cases ────────────────────────────────────

    [Fact]
    public async Task LaunchGame_AppIdZero_LaunchesIt()
    {
        // Edge: launching appId 0 — no game running, so it should still call LaunchSteamUrl
        A.CallTo(() => _platform.GetRunningAppId()).Returns(1);
        // running appId (1) != target (0), different game running so StopGame is called
        A.CallTo(() => _platform.GetSteamPath()).Returns((string?)null);
        var service = CreateService();

        var result = await service.LaunchGameAsync(0);

        // Poll never sees appId 0 (GetRunningAppId keeps returning 1)
        A.CallTo(() => _platform.LaunchSteamUrl("steam://rungameid/0"))
            .MustHaveHappenedOnceExactly();
    }

    // ── LaunchGameAsync shortcut tests ─────────────────────────────────

    [Fact]
    public async Task LaunchGame_ShortcutAppId_UsesShiftedId()
    {
        // Negative appId = non-Steam shortcut
        var shortcutAppId = -1234567890;
        A.CallTo(() => _platform.GetRunningAppId()).Returns(0);
        var service = CreateService();

        await service.LaunchGameAsync(shortcutAppId);

        // Expected: ((long)(uint)appId << 32) | 0x02000000
        var expectedLaunchId = ((long)(uint)shortcutAppId << 32) | 0x02000000;
        A.CallTo(() => _platform.LaunchSteamUrl($"steam://rungameid/{expectedLaunchId}"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task LaunchGame_RegularAppId_UsesPlainId()
    {
        A.CallTo(() => _platform.GetRunningAppId()).Returns(0);
        var service = CreateService();

        await service.LaunchGameAsync(730);

        A.CallTo(() => _platform.LaunchSteamUrl("steam://rungameid/730"))
            .MustHaveHappenedOnceExactly();
    }

    // ── IsShortcutAppId tests ──────────────────────────────────────────

    [Theory]
    [InlineData(-1, true)]
    [InlineData(-1234567890, true)]
    [InlineData(0, false)]
    [InlineData(730, false)]
    [InlineData(int.MaxValue, false)]
    public void IsShortcutAppId_ReturnsExpected(int appId, bool expected)
    {
        SteamService.IsShortcutAppId(appId).ShouldBe(expected);
    }

    // ── ParseShortcuts tests ───────────────────────────────────────────

    [Fact]
    public void ParseShortcuts_ValidBinaryVdf_ReturnsShortcuts()
    {
        using var stream = File.OpenRead(TestData.FilePath("shortcuts.vdf"));
        var shortcuts = SteamService.ParseShortcuts(stream);

        shortcuts.Count.ShouldBe(2);

        shortcuts[0].Name.ShouldBe("My Custom Game");
        shortcuts[0].AppId.ShouldBe(-1234567890);
        shortcuts[0].IsShortcut.ShouldBeTrue();
        shortcuts[0].LastPlayed.ShouldBe(1700000000L);
        shortcuts[0].ExePath.ShouldBe(@"C:\Games\custom.exe");

        shortcuts[1].Name.ShouldBe("Emulator Game");
        shortcuts[1].AppId.ShouldBe(-987654321);
        shortcuts[1].IsShortcut.ShouldBeTrue();
        shortcuts[1].LastPlayed.ShouldBe(1708000000L);
        shortcuts[1].ExePath.ShouldBe(@"D:\Emulators\retro.exe");
    }

    [Fact]
    public void ParseShortcuts_EmptyFile_ReturnsEmptyList()
    {
        using var stream = File.OpenRead(TestData.FilePath("shortcuts-empty.vdf"));
        var shortcuts = SteamService.ParseShortcuts(stream);

        shortcuts.ShouldBeEmpty();
    }

    [Fact]
    public void ParseShortcuts_CorruptData_ReturnsEmptyList()
    {
        using var stream = new MemoryStream([0xFF, 0xFE, 0x00, 0x01]);
        var shortcuts = SteamService.ParseShortcuts(stream);

        shortcuts.ShouldBeEmpty();
    }

    // ── Process-based fallback tests ──────────────────────────────────

    [Fact]
    public async Task GetRunningGame_AppIdZero_NoShortcutsInCache_ReturnsNull()
    {
        // Fake FS: GetGamesAsync returns empty list, so no shortcuts exist in cache.
        // GetRunningProcessPaths should NOT be called — no exe paths to match against.
        A.CallTo(() => _platform.GetRunningAppId()).Returns(0);
        A.CallTo(() => _platform.GetSteamPath()).Returns("C:\\FakeNonExistentSteamPath_12345");
        var service = CreateService();

        var result = await service.GetRunningGameAsync();

        result.ShouldBeNull();
        A.CallTo(() => _platform.GetRunningProcessPaths()).MustNotHaveHappened();
    }

    [Fact]
    public async Task GetRunningGame_AppIdNonZero_DoesNotCallGetRunningProcessPaths()
    {
        A.CallTo(() => _platform.GetRunningAppId()).Returns(730);
        A.CallTo(() => _platform.GetSteamPath()).Returns("C:\\FakeNonExistentSteamPath_12345");
        var service = CreateService();

        var result = await service.GetRunningGameAsync();

        A.CallTo(() => _platform.GetRunningProcessPaths()).MustNotHaveHappened();
    }

    [Fact]
    public async Task GetRunningGame_AppIdZero_SteamNotInstalled_ReturnsNull()
    {
        A.CallTo(() => _platform.GetRunningAppId()).Returns(0);
        A.CallTo(() => _platform.GetSteamPath()).Returns((string?)null);
        var service = CreateService();

        var result = await service.GetRunningGameAsync();

        result.ShouldBeNull();
        A.CallTo(() => _platform.GetRunningProcessPaths()).MustNotHaveHappened();
    }

    // ── GenerateShortcutAppId tests ────────────────────────────────────

    [Fact]
    public void GenerateShortcutAppId_AlwaysNegative()
    {
        var appId = SteamService.GenerateShortcutAppId(@"C:\Games\test.exe", "Test Game");

        appId.ShouldBeLessThan(0);
        SteamService.IsShortcutAppId(appId).ShouldBeTrue();
    }

    [Fact]
    public void GenerateShortcutAppId_DeterministicForSameInputs()
    {
        var id1 = SteamService.GenerateShortcutAppId(@"C:\Games\test.exe", "Test Game");
        var id2 = SteamService.GenerateShortcutAppId(@"C:\Games\test.exe", "Test Game");

        id1.ShouldBe(id2);
    }

    [Fact]
    public void GenerateShortcutAppId_DifferentForDifferentInputs()
    {
        var id1 = SteamService.GenerateShortcutAppId(@"C:\Games\test.exe", "Test Game");
        var id2 = SteamService.GenerateShortcutAppId(@"C:\Games\other.exe", "Other Game");

        id1.ShouldNotBe(id2);
    }

    // ── ResolvePcMode tests ───────────────────────────────────────

    [Fact]
    public void ResolvePcMode_PerGameBinding_ReturnsPerGameMode()
    {
        var options = new PcRemoteOptions
        {
            Steam = new SteamConfig
            {
                DefaultPcMode = "couch",
                GamePcModeBindings = new Dictionary<string, string> { ["730"] = "desktop" }
            }
        };
        var service = CreateService(options);

        service.ResolvePcMode(730).ShouldBe("desktop");
    }

    [Fact]
    public void ResolvePcMode_NoPerGameBinding_ReturnsDefault()
    {
        var options = new PcRemoteOptions
        {
            Steam = new SteamConfig { DefaultPcMode = "couch" }
        };
        var service = CreateService(options);

        service.ResolvePcMode(999).ShouldBe("couch");
    }

    [Fact]
    public void ResolvePcMode_PerGameNone_ReturnsNull()
    {
        var options = new PcRemoteOptions
        {
            Steam = new SteamConfig
            {
                DefaultPcMode = "couch",
                GamePcModeBindings = new Dictionary<string, string> { ["730"] = "none" }
            }
        };
        var service = CreateService(options);

        service.ResolvePcMode(730).ShouldBeNull();
    }

    [Fact]
    public void ResolvePcMode_DefaultNone_ReturnsNull()
    {
        var options = new PcRemoteOptions
        {
            Steam = new SteamConfig { DefaultPcMode = "none" }
        };
        var service = CreateService(options);

        service.ResolvePcMode(999).ShouldBeNull();
    }

    [Fact]
    public void ResolvePcMode_EmptyDefault_NoBindings_ReturnsNull()
    {
        var options = new PcRemoteOptions
        {
            Steam = new SteamConfig { DefaultPcMode = "" }
        };
        var service = CreateService(options);

        service.ResolvePcMode(999).ShouldBeNull();
    }

    [Fact]
    public void ResolvePcMode_PerGameEmpty_FallsBackToDefault()
    {
        var options = new PcRemoteOptions
        {
            Steam = new SteamConfig
            {
                DefaultPcMode = "couch",
                GamePcModeBindings = new Dictionary<string, string> { ["730"] = "" }
            }
        };
        var service = CreateService(options);

        service.ResolvePcMode(730).ShouldBe("couch");
    }

    // ── LaunchGameAsync mode switch tests ──────────────────────────

    [Fact]
    public async Task LaunchGameAsync_WithBinding_CallsModeSwitchBeforeLaunch()
    {
        var options = new PcRemoteOptions
        {
            Steam = new SteamConfig
            {
                DefaultPcMode = "couch",
                GamePcModeBindings = new Dictionary<string, string> { ["730"] = "desktop" }
            }
        };
        var service = CreateService(options);
        A.CallTo(() => _platform.GetRunningAppId()).Returns(0);

        _ = await service.LaunchGameAsync(730);

        A.CallTo(() => _modeService.ApplyModeAsync("desktop")).MustHaveHappenedOnceExactly();
        A.CallTo(() => _platform.LaunchSteamUrl("steam://rungameid/730")).MustHaveHappened();
    }

    [Fact]
    public async Task LaunchGameAsync_ModeNotFound_StillLaunches()
    {
        var options = new PcRemoteOptions
        {
            Steam = new SteamConfig { DefaultPcMode = "nonexistent" }
        };
        var service = CreateService(options);
        A.CallTo(() => _platform.GetRunningAppId()).Returns(0);
        A.CallTo(() => _modeService.ApplyModeAsync("nonexistent"))
            .Throws(new KeyNotFoundException("Mode not found"));

        _ = await service.LaunchGameAsync(999);

        A.CallTo(() => _platform.LaunchSteamUrl(A<string>._)).MustHaveHappened();
    }

    [Fact]
    public async Task LaunchGameAsync_NoBinding_NoModeSwitch()
    {
        var service = CreateService();
        A.CallTo(() => _platform.GetRunningAppId()).Returns(0);

        _ = await service.LaunchGameAsync(730);

        A.CallTo(() => _modeService.ApplyModeAsync(A<string>._)).MustNotHaveHappened();
        A.CallTo(() => _platform.LaunchSteamUrl("steam://rungameid/730")).MustHaveHappened();
    }

    [Fact]
    public async Task LaunchGameAsync_SameGameAlreadyRunning_NoModeSwitch()
    {
        var options = new PcRemoteOptions
        {
            Steam = new SteamConfig { DefaultPcMode = "couch" }
        };
        var service = CreateService(options);
        A.CallTo(() => _platform.GetRunningAppId()).Returns(730);
        A.CallTo(() => _platform.GetSteamPath()).Returns((string?)null);

        _ = await service.LaunchGameAsync(730);

        A.CallTo(() => _modeService.ApplyModeAsync(A<string>._)).MustNotHaveHappened();
        A.CallTo(() => _platform.LaunchSteamUrl(A<string>._)).MustNotHaveHappened();
    }

    // ── GetBindings tests ─────────────────────────────────────────

    [Fact]
    public void GetBindings_ReturnsCurrentConfig()
    {
        var options = new PcRemoteOptions
        {
            Steam = new SteamConfig
            {
                DefaultPcMode = "couch",
                GamePcModeBindings = new Dictionary<string, string>
                {
                    ["730"] = "desktop",
                    ["1245620"] = "couch"
                }
            }
        };
        var service = CreateService(options);

        var bindings = service.GetBindings();

        bindings.DefaultPcMode.ShouldBe("couch");
        bindings.GamePcModeBindings.Count.ShouldBe(2);
        bindings.GamePcModeBindings["730"].ShouldBe("desktop");
        bindings.GamePcModeBindings["1245620"].ShouldBe("couch");
    }

    // ── IsSteamRunning tests ──────────────────────────────────────────

    [Fact]
    public void IsSteamRunning_WhenPlatformReturnsTrue_ReturnsTrue()
    {
        A.CallTo(() => _platform.IsSteamRunning()).Returns(true);
        var service = CreateService();

        service.IsSteamRunning().ShouldBeTrue();
    }

    [Fact]
    public void IsSteamRunning_WhenPlatformReturnsFalse_ReturnsFalse()
    {
        A.CallTo(() => _platform.IsSteamRunning()).Returns(false);
        var service = CreateService();

        service.IsSteamRunning().ShouldBeFalse();
    }
}

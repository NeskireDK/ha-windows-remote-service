using System.Net;
using System.Text.RegularExpressions;
using HaPcRemote.IntegrationTests.Models;
using Shouldly;
using Xunit.Abstractions;

namespace HaPcRemote.IntegrationTests;

public class ArtworkDiagnosticTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public ArtworkDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private async Task<List<SteamGame>?> GetGamesOrNull()
    {
        var response = await GetRawAsync("/api/steam/games");
        if (response.StatusCode != HttpStatusCode.OK)
            return null;

        var result = await DeserializeAsync<ApiResponse<List<SteamGame>>>(response);
        return result?.Data;
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetGames_AllGamesHaveNonZeroAppId()
    {
        var games = await GetGamesOrNull();
        if (games == null)
        {
            _output.WriteLine("Steam not available — skipping");
            return;
        }

        games.ShouldNotBeEmpty();
        foreach (var game in games)
        {
            // Non-Steam shortcuts use negative AppIds (uint overflow to int) — that's valid
            game.AppId.ShouldNotBe(0, $"Game '{game.Name}' has zero AppId");
            if (game.AppId < 0)
                _output.WriteLine($"Non-Steam shortcut: {game.Name} ({game.AppId}) — IsShortcut: {game.IsShortcut}");
        }

        _output.WriteLine($"All {games.Count} games have non-zero AppId");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetGames_ReturnsTop20Games()
    {
        var games = await GetGamesOrNull();
        if (games == null)
        {
            _output.WriteLine("Steam not available — skipping");
            return;
        }

        games.ShouldNotBeEmpty();
        _output.WriteLine($"Returned {games.Count} games:");
        foreach (var game in games)
        {
            var lastPlayed = DateTimeOffset.FromUnixTimeSeconds(game.LastPlayed);
            _output.WriteLine($"  {game.Name} ({game.AppId}) — last played {lastPlayed:yyyy-MM-dd HH:mm} — shortcut: {game.IsShortcut}");
        }
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task ArtworkEndpoint_ForEachGame_ReturnsImageOr404()
    {
        var games = await GetGamesOrNull();
        if (games == null)
        {
            _output.WriteLine("Steam not available — skipping");
            return;
        }

        _output.WriteLine($"Checking artwork for {games.Count} games:\n");

        foreach (var game in games)
        {
            var response = await GetRawAsync($"/api/steam/artwork/{game.AppId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                var length = response.Content.Headers.ContentLength ?? (await response.Content.ReadAsByteArrayAsync()).Length;
                _output.WriteLine($"Game: {game.Name} ({game.AppId}) — Artwork: 200 OK ({contentType}, {length / 1024}KB)");
            }
            else
            {
                _output.WriteLine($"Game: {game.Name} ({game.AppId}) — Artwork: {(int)response.StatusCode} {response.StatusCode}");
            }

            new[] { HttpStatusCode.OK, HttpStatusCode.NotFound }
                .ShouldContain(response.StatusCode,
                    $"Unexpected status for {game.Name} ({game.AppId})");
        }
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task ArtworkEndpoint_ForGamesWithArtwork_ReturnsValidImage()
    {
        var games = await GetGamesOrNull();
        if (games == null)
        {
            _output.WriteLine("Steam not available — skipping");
            return;
        }

        var gamesWithArtwork = 0;

        foreach (var game in games)
        {
            var response = await GetRawAsync($"/api/steam/artwork/{game.AppId}");
            if (response.StatusCode != HttpStatusCode.OK)
                continue;

            gamesWithArtwork++;
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var body = await response.Content.ReadAsByteArrayAsync();

            contentType.ShouldStartWith("image/");
            body.Length.ShouldBeGreaterThan(0,
                $"Game {game.Name} ({game.AppId}) returned empty image body");

            _output.WriteLine($"Valid: {game.Name} ({game.AppId}) — {contentType}, {body.Length / 1024}KB");
        }

        _output.WriteLine($"\n{gamesWithArtwork} games returned valid artwork");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task ArtworkEndpoint_CountMissingVsFound()
    {
        var games = await GetGamesOrNull();
        if (games == null)
        {
            _output.WriteLine("Steam not available — skipping");
            return;
        }

        var found = new List<SteamGame>();
        var missing = new List<SteamGame>();

        foreach (var game in games)
        {
            var response = await GetRawAsync($"/api/steam/artwork/{game.AppId}");
            if (response.StatusCode == HttpStatusCode.OK)
                found.Add(game);
            else
                missing.Add(game);
        }

        var total = games.Count;
        _output.WriteLine($"Found: {found.Count}/{total}");
        _output.WriteLine($"Missing: {missing.Count}/{total}");

        if (missing.Count > 0)
        {
            _output.WriteLine($"\nMissing games: {string.Join(", ", missing.Select(g => $"{g.Name} ({g.AppId})"))}");
        }

        if (found.Count > 0)
        {
            _output.WriteLine($"\nFound games: {string.Join(", ", found.Select(g => $"{g.Name} ({g.AppId})"))}");
        }

        var missingPercent = (double)missing.Count / total * 100;
        missingPercent.ShouldBeLessThan(50.0,
            $"Artwork bug confirmed: {missing.Count}/{total} games ({missingPercent:F0}%) missing artwork.\n" +
            $"Missing: {string.Join(", ", missing.Select(g => $"{g.Name} ({g.AppId})"))}");
    }

    // --- v1.3.7 diagnostics endpoint tests ---

    private async Task<List<ArtworkDiagnostics>?> GetDiagnosticsOrSkip()
    {
        var response = await GetRawAsync("/api/steam/artwork/diagnostics");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null; // v1.3.7 not deployed yet

        response.EnsureSuccessStatusCode();
        var result = await DeserializeAsync<ApiResponse<List<ArtworkDiagnostics>>>(response);
        return result?.Data;
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Diagnostics_AllGames_ShowsPathResolution()
    {
        var diagnostics = await GetDiagnosticsOrSkip();
        if (diagnostics == null)
        {
            _output.WriteLine("v1.3.7 not deployed — skipping");
            return;
        }

        diagnostics.ShouldNotBeEmpty();

        foreach (var game in diagnostics)
        {
            _output.WriteLine($"--- {game.GameName} (appId={game.AppId}, fileId={game.FileId}, shortcut={game.IsShortcut}) ---");
            _output.WriteLine($"  ResolvedPath: {game.ResolvedPath ?? "(none)"}");
            _output.WriteLine($"  CdnUrl: {game.CdnUrl}");

            foreach (var path in game.PathsChecked)
            {
                var size = path.SizeBytes.HasValue ? $"{path.SizeBytes / 1024}KB" : "n/a";
                _output.WriteLine($"  [{path.Category}] exists={path.Exists} size={size} — {path.Path}");
            }

            _output.WriteLine("");
        }
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Diagnostics_AllGames_LocalFilesExistBeforeCdn()
    {
        var diagnostics = await GetDiagnosticsOrSkip();
        if (diagnostics == null)
        {
            _output.WriteLine("v1.3.7 not deployed — skipping");
            return;
        }

        var gamesWithLocalFiles = new List<string>();
        var gamesWithoutLocalFiles = new List<string>();

        foreach (var game in diagnostics)
        {
            var hasLocal = game.PathsChecked.Any(p => p.Exists);
            if (hasLocal)
                gamesWithLocalFiles.Add($"{game.GameName} ({game.AppId})");
            else
                gamesWithoutLocalFiles.Add($"{game.GameName} ({game.AppId})");
        }

        _output.WriteLine($"Games with local files: {gamesWithLocalFiles.Count}");
        foreach (var g in gamesWithLocalFiles)
            _output.WriteLine($"  [LOCAL] {g}");

        _output.WriteLine($"\nGames needing CDN: {gamesWithoutLocalFiles.Count}");
        foreach (var g in gamesWithoutLocalFiles)
            _output.WriteLine($"  [CDN] {g}");

        gamesWithoutLocalFiles.Count.ShouldBeLessThanOrEqualTo(5,
            $"Local resolution broken: {gamesWithoutLocalFiles.Count} games have zero local files. " +
            $"Games: {string.Join(", ", gamesWithoutLocalFiles)}");
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Diagnostics_CompareResolvedVsEndpoint()
    {
        var diagnostics = await GetDiagnosticsOrSkip();
        if (diagnostics == null)
        {
            _output.WriteLine("v1.3.7 not deployed — skipping");
            return;
        }

        _output.WriteLine($"{"Game",-35} {"Resolved?",-12} {"Endpoint",-12} {"Source"}");
        _output.WriteLine(new string('-', 75));

        foreach (var game in diagnostics)
        {
            var hasResolved = game.ResolvedPath != null;
            var artworkResponse = await GetRawAsync($"/api/steam/artwork/{game.AppId}");
            var endpointOk = artworkResponse.StatusCode == HttpStatusCode.OK;

            var source = (hasResolved, endpointOk) switch
            {
                (true, true) => "Local file",
                (false, true) => "CDN fallback",
                (true, false) => "BUG: resolved but endpoint 404",
                (false, false) => "No artwork"
            };

            _output.WriteLine($"{game.GameName,-35} {(hasResolved ? "YES" : "no"),-12} {(endpointOk ? "200" : "404"),-12} {source}");
        }
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Diagnostics_CheckSteamUserId()
    {
        var diagnostics = await GetDiagnosticsOrSkip();
        if (diagnostics == null)
        {
            _output.WriteLine("v1.3.7 not deployed — skipping");
            return;
        }

        diagnostics.ShouldNotBeEmpty();

        // Extract steamUserId from Custom Grid paths (pattern: userdata/{userId}/config/grid/)
        var userIdPattern = new Regex(@"userdata[/\\](\d+)[/\\]config[/\\]grid", RegexOptions.IgnoreCase);
        string? detectedUserId = null;

        foreach (var game in diagnostics)
        {
            foreach (var path in game.PathsChecked.Where(p => p.Category.Contains("Grid", StringComparison.OrdinalIgnoreCase)))
            {
                var match = userIdPattern.Match(path.Path);
                if (match.Success)
                {
                    detectedUserId = match.Groups[1].Value;
                    break;
                }
            }

            if (detectedUserId != null)
                break;
        }

        _output.WriteLine($"Steam User ID used for grid lookups: {detectedUserId ?? "(not found)"}");

        if (detectedUserId != null)
        {
            var gamesWithUserIdPaths = diagnostics.Count(g =>
                g.PathsChecked.Any(p => p.Path.Contains($"userdata{System.IO.Path.DirectorySeparatorChar}{detectedUserId}") ||
                                        p.Path.Contains($"userdata/{detectedUserId}")));

            _output.WriteLine($"Games referencing userdata/{detectedUserId}: {gamesWithUserIdPaths}/{diagnostics.Count}");
            gamesWithUserIdPaths.ShouldBeGreaterThan(0, "No paths reference the detected Steam user ID");
        }
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task Diagnostics_FileIdMatchesExpected()
    {
        var diagnostics = await GetDiagnosticsOrSkip();
        if (diagnostics == null)
        {
            _output.WriteLine("v1.3.7 not deployed — skipping");
            return;
        }

        var mismatches = new List<string>();

        foreach (var game in diagnostics)
        {
            var expectedFileId = game.IsShortcut || game.AppId < 0
                ? ((uint)game.AppId).ToString()
                : game.AppId.ToString();

            if (game.FileId != expectedFileId)
            {
                mismatches.Add($"{game.GameName} (appId={game.AppId}): expected FileId='{expectedFileId}', got '{game.FileId}'");
            }

            _output.WriteLine($"{game.GameName} (appId={game.AppId}) — FileId={game.FileId} expected={expectedFileId} — {(game.FileId == expectedFileId ? "OK" : "MISMATCH")}");
        }

        mismatches.ShouldBeEmpty(
            $"FileId mismatches found:\n{string.Join("\n", mismatches)}");
    }
}

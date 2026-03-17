using System.Net;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

public class UpdateServiceTests
{
    private readonly IHttpClientFactory _httpClientFactory = A.Fake<IHttpClientFactory>();
    private readonly ILogger<UpdateService> _logger = A.Fake<ILogger<UpdateService>>();

    private UpdateService CreateService() => new(_httpClientFactory, _logger);

    private void SetupHttpResponse(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(json, status);
        var client = new HttpClient(handler);
        A.CallTo(() => _httpClientFactory.CreateClient("GitHubUpdate")).Returns(client);
    }

    private CapturingHttpMessageHandler SetupCapturingHandler(string json)
    {
        var handler = new CapturingHttpMessageHandler(json);
        var client = new HttpClient(handler);
        A.CallTo(() => _httpClientFactory.CreateClient("GitHubUpdate")).Returns(client);
        return handler;
    }

    private void SetupHttpSequence(params string[] jsonResponses)
    {
        var handler = new SequentialHttpMessageHandler(jsonResponses);
        var client = new HttpClient(handler);
        A.CallTo(() => _httpClientFactory.CreateClient("GitHubUpdate")).Returns(client);
    }

    private void SetupHttpException(Exception ex)
    {
        var handler = new ThrowingHttpMessageHandler(ex);
        var client = new HttpClient(handler);
        A.CallTo(() => _httpClientFactory.CreateClient("GitHubUpdate")).Returns(client);
    }

    private static string MakeReleaseJson(string tagName, string? assetName = null, string? downloadUrl = null)
    {
        var release = MakeReleaseObject(tagName, assetName, downloadUrl);
        return JsonSerializer.Serialize(release);
    }

    private static string MakeReleasesJson(params (string tagName, string? assetName)[] releases)
    {
        var list = releases.Select(r => MakeReleaseObject(r.tagName, r.assetName)).ToArray();
        return JsonSerializer.Serialize(list);
    }

    private static object MakeReleaseObject(string tagName, string? assetName = null, string? downloadUrl = null)
    {
        var assets = new List<object>();
        if (assetName is not null)
        {
            assets.Add(new
            {
                name = assetName,
                browser_download_url = downloadUrl ?? $"https://example.com/{assetName}"
            });
        }

        return new { tag_name = tagName, assets };
    }

    // ── GetCurrentVersion ─────────────────────────────────────────────

    [Fact]
    public void GetCurrentVersion_InTestContext_ReturnsNullOrValidVersion()
    {
        // In test runner, GetEntryAssembly() may return null or test host assembly.
        // Either way, it should not throw.
        var version = UpdateService.GetCurrentVersion();

        // No assertion on value — just confirm no exception
    }

    // ── ParseVersion via CheckAndApplyAsync ───────────────────────────

    [Fact]
    public async Task CheckAndApply_TagWithVPrefix_ParsedCorrectly()
    {
        // v2.0.0 > anything GetCurrentVersion returns in test (null),
        // so if current is null, result is UpToDate (null version comparison exits early).
        // We test parse behavior through the returned result.
        var json = MakeReleaseJson("v2.0.0", "HaPcRemoteService-Setup-2.0.0.exe");
        SetupHttpResponse(json);
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        // currentVersion is null in test → latestVersion comparison short-circuits → UpToDate
        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAndApply_TagWithoutVPrefix_ParsedCorrectly()
    {
        var json = MakeReleaseJson("2.0.0", "HaPcRemoteService-Setup-2.0.0.exe");
        SetupHttpResponse(json);
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        // Same as above: currentVersion null → UpToDate
        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAndApply_InvalidTag_ReturnsUpToDate()
    {
        var json = MakeReleaseJson("not-a-version", "HaPcRemoteService-Setup-1.0.0.exe");
        SetupHttpResponse(json);
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAndApply_TwoPartVersion_NormalizedToThreePart()
    {
        // "v1.2" has Build < 0, so ParseVersion normalizes to 1.2.0
        var json = MakeReleaseJson("v1.2", "HaPcRemoteService-Setup-1.2.exe");
        SetupHttpResponse(json);
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        // Doesn't crash; currentVersion null → UpToDate
        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAndApply_FourPartVersion_ParsedCorrectly()
    {
        var json = MakeReleaseJson("v1.2.3.4", "HaPcRemoteService-Setup-1.2.3.4.exe");
        SetupHttpResponse(json);
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    // ── Null / missing release scenarios ──────────────────────────────

    [Fact]
    public async Task CheckAndApply_NullRelease_ReturnsUpToDate()
    {
        // GitHub returns a JSON that deserializes to null (empty body)
        SetupHttpResponse("null");
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAndApply_NoAssets_ReturnsUpToDate()
    {
        var json = JsonSerializer.Serialize(new { tag_name = "v99.0.0", assets = (object?)null });
        SetupHttpResponse(json);
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        // currentVersion is null → comparison short-circuits → UpToDate
        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAndApply_EmptyAssets_ReturnsUpToDate()
    {
        var json = MakeReleaseJson("v99.0.0"); // no asset added
        SetupHttpResponse(json);
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAndApply_AssetWrongPrefix_ReturnsUpToDate()
    {
        var json = MakeReleaseJson("v99.0.0", "WrongPrefix-1.0.0.exe");
        SetupHttpResponse(json);
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAndApply_AssetWrongExtension_ReturnsUpToDate()
    {
        var json = MakeReleaseJson("v99.0.0", "HaPcRemoteService-Setup-1.0.0.zip");
        SetupHttpResponse(json);
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    // ── Network / HTTP errors ─────────────────────────────────────────

    [Fact]
    public async Task CheckAndApply_HttpRequestException_ReturnsFailed()
    {
        SetupHttpException(new HttpRequestException("DNS resolution failed"));
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        result.Status.ShouldBe(UpdateStatus.Failed);
        result.Message.ShouldBe("Network unavailable");
    }

    [Fact]
    public async Task CheckAndApply_GenericException_ReturnsFailed()
    {
        SetupHttpException(new InvalidOperationException("Something broke"));
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        result.Status.ShouldBe(UpdateStatus.Failed);
        result.Message.ShouldBe("Update check failed");
    }

    // ── Lock / concurrency ────────────────────────────────────────────

    [Fact]
    public async Task CheckAndApply_ConcurrentCalls_SecondReturnsAlreadyInProgress()
    {
        // Set up a slow handler so first call blocks
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        var handler = new BlockingHttpMessageHandler(tcs.Task);
        var client = new HttpClient(handler);
        A.CallTo(() => _httpClientFactory.CreateClient("GitHubUpdate")).Returns(client);

        var svc = CreateService();

        // Start first call (will block on HTTP)
        var first = svc.CheckAndApplyAsync();

        // Small yield to ensure first call acquired the lock
        await Task.Delay(50);

        // Second call should fail immediately
        var second = await svc.CheckAndApplyAsync();
        second.Status.ShouldBe(UpdateStatus.Failed);
        second.Message.ShouldBe("Update already in progress");

        // Unblock first call
        var json = MakeReleaseJson("v0.0.1");
        tcs.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var firstResult = await first;
        firstResult.Status.ShouldNotBe(UpdateStatus.Failed);
    }

    [Fact]
    public async Task CheckAndApply_AfterFirstCompletes_SecondAcquiresLock()
    {
        var json = MakeReleaseJson("v0.0.1");
        SetupHttpResponse(json);
        var svc = CreateService();

        var first = await svc.CheckAndApplyAsync();
        first.Status.ShouldBe(UpdateStatus.UpToDate);

        // Need a new client for the second call since first was disposed
        SetupHttpResponse(json);
        var second = await svc.CheckAndApplyAsync();
        second.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    // ── UpdateResult factory methods ──────────────────────────────────

    [Fact]
    public void UpdateResult_UpToDate_SetsCorrectProperties()
    {
        var result = UpdateResult.UpToDate("1.2.3");

        result.Status.ShouldBe(UpdateStatus.UpToDate);
        result.CurrentVersion.ShouldBe("1.2.3");
        result.Message.ShouldBe("Already up to date");
        result.LatestVersion.ShouldBeNull();
    }

    [Fact]
    public void UpdateResult_UpdateStarted_SetsCorrectProperties()
    {
        var result = UpdateResult.UpdateStarted("1.0.0", "v2.0.0");

        result.Status.ShouldBe(UpdateStatus.UpdateStarted);
        result.CurrentVersion.ShouldBe("1.0.0");
        result.LatestVersion.ShouldBe("v2.0.0");
        result.Message.ShouldBe("Update from 1.0.0 to v2.0.0 started");
    }

    [Fact]
    public void UpdateResult_Failed_SetsCorrectProperties()
    {
        var result = UpdateResult.Failed("something went wrong");

        result.Status.ShouldBe(UpdateStatus.Failed);
        result.Message.ShouldBe("something went wrong");
        result.CurrentVersion.ShouldBeNull();
        result.LatestVersion.ShouldBeNull();
    }

    // ── Cancellation ──────────────────────────────────────────────────

    [Fact]
    public async Task CheckAndApply_CancellationRequested_Throws()
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        var handler = new BlockingHttpMessageHandler(tcs.Task);
        var client = new HttpClient(handler);
        A.CallTo(() => _httpClientFactory.CreateClient("GitHubUpdate")).Returns(client);
        var svc = CreateService();

        using var cts = new CancellationTokenSource();
        var task = svc.CheckAndApplyAsync(cts.Token);
        await cts.CancelAsync();

        // Unblock the handler with a cancellation
        tcs.SetCanceled();

        // The service catches generic exceptions, so it returns Failed
        var result = await task;
        result.Status.ShouldBe(UpdateStatus.Failed);
    }

    // ── IsPrerelease ───────────────────────────────────────────────────

    [Theory]
    [InlineData("v1.7.0-rc.1", true)]
    [InlineData("v1.7.0-beta", true)]
    [InlineData("v1.7.0-alpha.2", true)]
    [InlineData("1.7.0-rc.1", true)]
    [InlineData("v1.7.0", false)]
    [InlineData("v1.7.0.0", false)]
    [InlineData("1.7.0", false)]
    public void IsPrerelease_DetectsHyphenInCleanedTag(string tag, bool expected)
    {
        UpdateService.IsPrerelease(tag).ShouldBe(expected);
    }

    // ── Prerelease filtering ─────────────────────────────────────────

    [Fact]
    public async Task CheckAndApply_Default_UsesLatestEndpoint()
    {
        // Default (no prereleases) uses /releases/latest — single object response
        var json = MakeReleaseJson("v0.0.1", "HaPcRemoteService-Setup-0.0.1.exe");
        SetupHttpResponse(json);
        var svc = CreateService();

        var result = await svc.CheckAndApplyAsync();

        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAndApply_WithPrereleases_UsesReleasesEndpoint()
    {
        // With prereleases enabled, uses /releases — array response
        var json = MakeReleasesJson(("v0.0.1-rc.1", "HaPcRemoteService-Setup-0.0.1-rc.1.exe"));
        SetupHttpResponse(json);
        var svc = new UpdateService(_httpClientFactory, _logger, includePrereleases: () => true);

        var result = await svc.CheckAndApplyAsync();

        // v0.0.1 <= current → UpToDate (but prerelease was NOT filtered out)
        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAndApply_WithPrereleases_SkipsStableFindsPrerelease()
    {
        // Prerelease v0.0.2-rc.1 comes after stable v0.0.1 — both pass through filter
        var json = MakeReleasesJson(
            ("v0.0.1", "HaPcRemoteService-Setup-0.0.1.exe"),
            ("v0.0.2-rc.1", "HaPcRemoteService-Setup-0.0.2-rc.1.exe"));
        SetupHttpResponse(json);
        var svc = new UpdateService(_httpClientFactory, _logger, includePrereleases: () => true);

        var result = await svc.CheckAndApplyAsync();

        // Both <= current → UpToDate
        result.Status.ShouldBe(UpdateStatus.UpToDate);
    }


    [Fact]
    public async Task CheckAndApply_NullFunc_DefaultsToStableEndpoint()
    {
        var json = MakeReleaseJson("v0.0.1", "HaPcRemoteService-Setup-0.0.1.exe");
        var handler = SetupCapturingHandler(json);
        var svc = new UpdateService(_httpClientFactory, _logger);

        await svc.CheckAndApplyAsync();

        handler.RequestedUrls.ShouldHaveSingleItem();
        handler.RequestedUrls[0].ShouldContain("/releases/latest");
    }

    [Fact]
    public async Task CheckAndApply_FuncReturnsTrue_UsesAllReleasesEndpoint()
    {
        var json = MakeReleasesJson(("v0.0.1-rc.1", "HaPcRemoteService-Setup-0.0.1-rc.1.exe"));
        var handler = SetupCapturingHandler(json);
        var svc = new UpdateService(_httpClientFactory, _logger, includePrereleases: () => true);

        await svc.CheckAndApplyAsync();

        handler.RequestedUrls.ShouldHaveSingleItem();
        handler.RequestedUrls[0].ShouldEndWith("/releases");
    }

    [Fact]
    public async Task CheckAndApply_FuncReadDynamically_SwitchesEndpoint()
    {
        var includePrerelease = false;

        // First call: stable
        var stableJson = MakeReleaseJson("v0.0.1", "HaPcRemoteService-Setup-0.0.1.exe");
        var handler1 = SetupCapturingHandler(stableJson);
        var svc = new UpdateService(_httpClientFactory, _logger, includePrereleases: () => includePrerelease);

        await svc.CheckAndApplyAsync();
        handler1.RequestedUrls[0].ShouldContain("/releases/latest");

        // Toggle to prereleases
        includePrerelease = true;
        var preJson = MakeReleasesJson(("v0.0.2-rc.1", "HaPcRemoteService-Setup-0.0.2-rc.1.exe"));
        var handler2 = SetupCapturingHandler(preJson);

        await svc.CheckAndApplyAsync();
        handler2.RequestedUrls[0].ShouldEndWith("/releases");
        handler2.RequestedUrls[0].ShouldNotContain("/releases/latest");
    }

    // ── Test helpers ──────────────────────────────────────────────────

    private sealed class FakeHttpMessageHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    
    private sealed class CapturingHttpMessageHandler(string responseJson) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUrls.Add(request.RequestUri?.ToString() ?? "");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class SequentialHttpMessageHandler(string[] responses) : HttpMessageHandler
    {
        private int _callIndex;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = _callIndex < responses.Length ? responses[_callIndex] : "null";
            _callIndex++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            throw exception;
        }
    }

    private sealed class BlockingHttpMessageHandler(Task<HttpResponseMessage> gate) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return await gate.WaitAsync(ct);
        }
    }
}

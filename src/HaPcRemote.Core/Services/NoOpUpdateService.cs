namespace HaPcRemote.Service.Services;

/// <summary>
/// Fallback for platforms that don't support auto-update (e.g. Linux headless).
/// </summary>
public sealed class NoOpUpdateService : IUpdateService
{
    private static readonly UpdateResult NotAvailable = UpdateResult.Failed("Auto-update not available on this platform");

    public Task<UpdateResult> CheckAndApplyAsync(CancellationToken ct = default)
        => Task.FromResult(NotAvailable);

    public Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
        => Task.FromResult<ReleaseInfo?>(null);

    public Task<UpdateResult> ApplyAsync(ReleaseInfo release, CancellationToken ct = default)
        => Task.FromResult(NotAvailable);
}

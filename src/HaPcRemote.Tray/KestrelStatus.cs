namespace HaPcRemote.Tray;

internal static class KestrelStatus
{
    private static readonly object _sync = new();
    private static TaskCompletionSource _started = new();

    private static volatile bool _isRunning;
    private static volatile string? _error;

    public static bool IsRunning => _isRunning;
    public static string? Error => _error;
    public static Task Started => _started.Task;

    public static void SetRunning()
    {
        lock (_sync)
        {
            _isRunning = true;
        }
        _started.TrySetResult();
    }

    public static void SetFailed(string error)
    {
        lock (_sync)
        {
            _isRunning = false;
            _error = error;
        }
        _started.TrySetResult();
    }

    /// <summary>Reset status before an in-process restart so GeneralTab can await the new Started task.</summary>
    public static void Reset()
    {
        lock (_sync)
        {
            _isRunning = false;
            _error = null;
        }
        Interlocked.Exchange(ref _started, new TaskCompletionSource());
    }
}

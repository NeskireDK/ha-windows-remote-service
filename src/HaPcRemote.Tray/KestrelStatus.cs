namespace HaPcRemote.Tray;

internal static class KestrelStatus
{
    private static readonly object _sync = new();
    private static TaskCompletionSource _started = new();

    public static bool IsRunning { get; private set; }
    public static string? Error { get; private set; }
    public static Task Started => _started.Task;

    public static void SetRunning()
    {
        lock (_sync)
        {
            IsRunning = true;
        }
        _started.TrySetResult();
    }

    public static void SetFailed(string error)
    {
        lock (_sync)
        {
            IsRunning = false;
            Error = error;
        }
        _started.TrySetResult();
    }

    /// <summary>Reset status before an in-process restart so GeneralTab can await the new Started task.</summary>
    public static void Reset()
    {
        lock (_sync)
        {
            IsRunning = false;
            Error = null;
        }
        Interlocked.Exchange(ref _started, new TaskCompletionSource());
    }
}

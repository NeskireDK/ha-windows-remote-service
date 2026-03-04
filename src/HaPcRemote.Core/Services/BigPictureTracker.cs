namespace HaPcRemote.Service.Services;

/// <summary>
/// Thread-safe singleton that tracks Big Picture state locally.
/// Defaults to not running; set to running when launched via our API,
/// reset when killed via our API.
/// </summary>
public sealed class BigPictureTracker : IBigPictureTracker
{
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;
    public void MarkStarted() => _isRunning = true;
    public void MarkStopped() => _isRunning = false;
}

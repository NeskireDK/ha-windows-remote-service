namespace HaPcRemote.Service.Services;

/// <summary>
/// Tracks whether Steam Big Picture was started by the integration.
/// Process-name detection doesn't work for Big Picture because it runs
/// inside steam.exe, which is always running when Steam is open.
/// </summary>
public interface IBigPictureTracker
{
    bool IsRunning { get; }
    void MarkStarted();
    void MarkStopped();
}

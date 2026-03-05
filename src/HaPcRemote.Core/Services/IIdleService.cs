namespace HaPcRemote.Service.Services;

public interface IIdleService
{
    /// <summary>
    /// Returns the number of seconds since the last user input (keyboard/mouse/gamepad),
    /// or null if idle detection is unavailable.
    /// </summary>
    int? GetIdleSeconds();
}

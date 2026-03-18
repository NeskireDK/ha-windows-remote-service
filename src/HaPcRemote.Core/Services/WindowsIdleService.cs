using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HaPcRemote.Service.Services;

/// <summary>
/// Detects user inactivity from keyboard, mouse, and XInput gamepads.
/// Keyboard/mouse use GetLastInputInfo; gamepads are polled via XInputGetState.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsIdleService : IIdleService
{
    private const int MaxControllers = 4;
    private const int ErrorDeviceNotConnected = 1167;

    // --- Keyboard / mouse ---

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

    // --- XInput gamepad ---

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static partial int XInputGetState(int dwUserIndex, out XINPUT_STATE pState);

    /// <summary>Last known packet number per controller slot (0-3).</summary>
    private readonly uint[] _lastPacket = new uint[MaxControllers];

    /// <summary>Tick (Environment.TickCount64) when gamepad input was last detected.</summary>
    internal long LastGamepadInputTick;

    public WindowsIdleService()
    {
        LastGamepadInputTick = Environment.TickCount64;

        // Snapshot current packet numbers so we don't count initial state as input.
        for (var i = 0; i < MaxControllers; i++)
        {
            if (XInputGetState(i, out var state) == 0)
                _lastPacket[i] = state.dwPacketNumber;
        }
    }

    public int? GetIdleSeconds()
    {
        // Keyboard / mouse idle
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return null;

        var kbMouseIdleMs = (ulong)Environment.TickCount64 - info.dwTime;

        // Gamepad idle — poll all 4 XInput slots
        PollGamepads();
        var gamepadIdleMs = (ulong)(Environment.TickCount64 - LastGamepadInputTick);

        var minIdleMs = Math.Min(kbMouseIdleMs, gamepadIdleMs);
        return (int)(minIdleMs / 1000);
    }

    internal void PollGamepads()
    {
        for (var i = 0; i < MaxControllers; i++)
        {
            var result = XInputGetState(i, out var state);
            if (result == ErrorDeviceNotConnected)
                continue;
            if (result != 0)
                continue;

            if (state.dwPacketNumber != _lastPacket[i])
            {
                _lastPacket[i] = state.dwPacketNumber;
                LastGamepadInputTick = Environment.TickCount64;
            }
        }
    }
}

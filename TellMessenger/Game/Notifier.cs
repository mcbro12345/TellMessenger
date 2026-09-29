using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace TellMessenger.Game;

// Sound and taskbar alerts for new messages.
public static class Notifier
{
    // The game's <se.1> to <se.16> chat sounds.
    public const int SoundCount = 16;

    // Quiet interface sounds (menu clicks and the like), for sends.
    public const int UiSoundCount = 40;

    public static unsafe void PlayUiSound(int soundEffect)
    {
        if (soundEffect is < 1 or > UiSoundCount)
            return;
        UIGlobals.PlaySoundEffect((uint)soundEffect);
    }

    public static void PlaySound(int soundEffect)
    {
        if (soundEffect is < 1 or > SoundCount)
            return;
        UIGlobals.PlayChatSoundEffect((uint)soundEffect);
    }

    // Flashes the game's taskbar button until the window is focused. Does
    // nothing while the game is already in front.
    public static void FlashTaskbar()
    {
        var window = GameWindow();
        if (window == IntPtr.Zero || GetForegroundWindow() == window)
            return;

        var info = new FlashWInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWInfo>(),
            Window = window,
            Flags = FlashAll | FlashUntilForeground,
            Count = 0,
            Timeout = 0,
        };
        FlashWindowEx(ref info);
    }

    private static IntPtr gameWindow;

    private static IntPtr GameWindow()
    {
        if (gameWindow == IntPtr.Zero)
            gameWindow = Process.GetCurrentProcess().MainWindowHandle;
        return gameWindow;
    }

    private const uint FlashAll = 3;
    private const uint FlashUntilForeground = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashWInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

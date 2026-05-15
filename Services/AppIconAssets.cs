using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Shutdown_Scheduler.Services;

public static class AppIconAssets
{
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x00000010;

    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    public static bool HasIconFile => File.Exists(IconPath);

    public static nint LoadIconHandle(int size)
    {
        if (!HasIconFile)
        {
            return nint.Zero;
        }

        return LoadImage(nint.Zero, IconPath, ImageIcon, size, size, LoadFromFile);
    }

    public static void DestroyIconHandle(nint handle)
    {
        if (handle != nint.Zero)
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint instance, string name, uint type, int desiredWidth, int desiredHeight, uint loadFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}

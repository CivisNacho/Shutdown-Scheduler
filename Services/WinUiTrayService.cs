using System;
using System.Runtime.InteropServices;

namespace Shutdown_Scheduler.Services;

public sealed class WinUiTrayService : IDisposable
{
    public const int CommandOpen = 0x4001;
    public const int CommandToggleWidget = 0x4002;
    public const int CommandExit = 0x4004;
    public const uint TrayCallbackMessage = 0x0400 + 88;
    public const int WmRButtonUp = 0x0205;

    private const uint WmCommand = 0x0111;
    private const uint IconId = 1;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint MfString = 0x00000000;
    private const uint MfGrayed = 0x00000001;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const int IdiApplication = 32512;
    private static readonly nint HwndMessage = new(-3);

    private readonly LocalizationService _loc;
    private readonly Action _openMainWindow;
    private readonly Action _toggleWidget;
    private readonly Action _cancelSchedule;
    private readonly Action _exitApplication;
    private readonly NativeMethods.SubclassProc _subclassProc;
    private readonly nint _trayIconHandle;
    private nint _ownerHandle;
    private bool _isVisible;
    private bool _hasSchedule;
    private bool _widgetEnabled;
    private bool _isMainWindowOpen;

    public WinUiTrayService(
        LocalizationService loc,
        Action openMainWindow,
        Action toggleWidget,
        Action cancelSchedule,
        Action exitApplication)
    {
        _loc = loc;
        _openMainWindow = openMainWindow;
        _toggleWidget = toggleWidget;
        _cancelSchedule = cancelSchedule;
        _exitApplication = exitApplication;
        _subclassProc = WindowSubclassProc;
        _trayIconHandle = AppIconAssets.LoadIconHandle(24);
        CreateMessageWindow();
    }

    public void Update(bool hasSchedule, bool widgetEnabled, bool isMainWindowOpen)
    {
        _hasSchedule = hasSchedule;
        _widgetEnabled = widgetEnabled;
        _isMainWindowOpen = isMainWindowOpen;

        var shouldBeVisible = _hasSchedule || !_isMainWindowOpen;
        if (shouldBeVisible && !_isVisible)
        {
            ShellNotifyIcon(NimAdd, CreateIconData(NifMessage | NifIcon | NifTip));
            _isVisible = true;
        }
        else if (!shouldBeVisible && _isVisible)
        {
            ShellNotifyIcon(NimDelete, CreateIconData(0));
            _isVisible = false;
        }
        else if (shouldBeVisible)
        {
            ShellNotifyIcon(NimModify, CreateIconData(NifMessage | NifIcon | NifTip));
        }
    }

    public void ShowContextMenu()
    {
        if (_ownerHandle == 0)
        {
            return;
        }

        var menu = CreatePopupMenu();
        AppendMenu(menu, MenuFlags(!_isMainWindowOpen), CommandOpen, _loc.T("Open"));
        AppendMenu(menu, MenuFlags(_hasSchedule), CommandToggleWidget, _widgetEnabled ? _loc.T("DisableWidget") : _loc.T("EnableWidgetShort"));
        AppendMenu(menu, MfSeparator, 0, string.Empty);
        AppendMenu(menu, MfString, CommandExit, _hasSchedule ? _loc.T("CancelAndExit") : _loc.T("Exit"));

        GetCursorPos(out var point);
        SetForegroundWindow(_ownerHandle);
        TrackPopupMenu(menu, TpmRightButton, point.X, point.Y, 0, _ownerHandle, nint.Zero);
        DestroyMenu(menu);
    }

    public void ShowBalloonTip(string message)
    {
        if (!_isVisible)
        {
            return;
        }

        var data = CreateIconData(NifInfo);
        data.InfoTitle = _loc.T("AppTitle");
        data.Info = message;
        ShellNotifyIcon(NimModify, data);
    }

    public void Dispose()
    {
        if (_isVisible)
        {
            ShellNotifyIcon(NimDelete, CreateIconData(0));
            _isVisible = false;
        }

        if (_ownerHandle != 0)
        {
            NativeMethods.RemoveWindowSubclass(_ownerHandle, _subclassProc, 1);
            DestroyWindow(_ownerHandle);
            _ownerHandle = 0;
        }

        AppIconAssets.DestroyIconHandle(_trayIconHandle);
    }

    private void CreateMessageWindow()
    {
        _ownerHandle = CreateWindowEx(
            0,
            "STATIC",
            "ShutdownSchedulerTrayMessageWindow",
            0,
            0,
            0,
            0,
            0,
            HwndMessage,
            0,
            0,
            0);

        if (_ownerHandle != 0)
        {
            NativeMethods.SetWindowSubclass(_ownerHandle, _subclassProc, 1, 0);
        }
    }

    private nint WindowSubclassProc(nint hWnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        if (message == TrayCallbackMessage)
        {
            var trayMessage = lParam.ToInt32();
            if (trayMessage == WmRButtonUp)
            {
                ShowContextMenu();
                return 0;
            }
        }

        if (message == WmCommand)
        {
            switch ((int)wParam & 0xFFFF)
            {
                case CommandOpen:
                    _openMainWindow();
                    return 0;
                case CommandToggleWidget:
                    _toggleWidget();
                    return 0;
                case CommandExit:
                    _exitApplication();
                    return 0;
            }
        }

        return NativeMethods.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private NotifyIconData CreateIconData(uint flags)
        => new()
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _ownerHandle,
            Id = IconId,
            Flags = flags,
            CallbackMessage = TrayCallbackMessage,
            Icon = _trayIconHandle != nint.Zero ? _trayIconHandle : LoadIcon(nint.Zero, new nint(IdiApplication)),
            Tip = _loc.T("AppTitle"),
            TimeoutOrVersion = 10_000
        };

    private static bool ShellNotifyIcon(uint message, NotifyIconData data)
        => ShellNotifyIconNative(message, ref data);

    private static uint MenuFlags(bool enabled) => enabled ? MfString : MfString | MfGrayed;

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconNative(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, int itemId, string itemText);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hWnd);

    private static partial class NativeMethods
    {
        public delegate nint SubclassProc(nint hWnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowSubclass(nint hWnd, SubclassProc subclassProc, nuint subclassId, nuint referenceData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc subclassProc, nuint subclassId);

        [DllImport("comctl32.dll", SetLastError = true)]
        public static extern nint DefSubclassProc(nint hWnd, uint message, nuint wParam, nint lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public nint WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}

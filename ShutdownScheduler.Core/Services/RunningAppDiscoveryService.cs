using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Shutdown_Scheduler.Models;
using Shutdown_Scheduler.Persistence;

namespace Shutdown_Scheduler.Services;

public sealed class RunningAppDiscoveryService
{
    private readonly AppDetectionExclusionStore _exclusionStore;

    public RunningAppDiscoveryService(AppDetectionExclusionStore exclusionStore)
    {
        _exclusionStore = exclusionStore;
    }

    public IReadOnlyList<AppTarget> GetRunningApps(int excludedProcessId)
    {
        var windows = EnumerateCandidateWindows(excludedProcessId);
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var appTargetsByProcess = new Dictionary<string, AppTarget>(StringComparer.OrdinalIgnoreCase);
        var processesWithVisibleWindows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customExcludedExecutableNames = _exclusionStore.LoadExecutableNames();

        foreach (var target in windows
            .Select(window =>
            {
                using var process = Process.GetProcessById(window.ProcessId);
                if (process.SessionId != currentSessionId || IsExcludedProcessName(process.ProcessName))
                {
                    return null;
                }

                string executablePath;
                try
                {
                    executablePath = process.MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    executablePath = string.Empty;
                }

                var versionInfo = TryGetVersionInfo(executablePath);
                if (!IsSupportedExecutablePath(executablePath) ||
                    IsCustomExcludedApp(process.ProcessName, executablePath, versionInfo, customExcludedExecutableNames))
                {
                    return null;
                }

                return new AppTarget(
                    process.ProcessName,
                    $"{window.Title} ({process.ProcessName}.exe)",
                    executablePath);
            })
            .Where(target => target is not null)
            .Cast<AppTarget>())
        {
            appTargetsByProcess[target.ProcessName] = target;
            processesWithVisibleWindows.Add(target.ProcessName);
        }

        foreach (var target in EnumerateBackgroundAppCandidates(excludedProcessId, currentSessionId, customExcludedExecutableNames))
        {
            appTargetsByProcess.TryAdd(target.ProcessName, target);
        }

        return appTargetsByProcess.Values
            .OrderByDescending(target => processesWithVisibleWindows.Contains(target.ProcessName))
            .ThenBy(target => target.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<AppTarget> EnumerateBackgroundAppCandidates(
        int excludedProcessId,
        int currentSessionId,
        IReadOnlySet<string> customExcludedExecutableNames)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == excludedProcessId ||
                    process.SessionId != currentSessionId ||
                    string.IsNullOrWhiteSpace(process.ProcessName) ||
                    IsExcludedProcessName(process.ProcessName))
                {
                    continue;
                }

                string executablePath;
                try
                {
                    executablePath = process.MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    continue;
                }

                var versionInfo = TryGetVersionInfo(executablePath);
                if (!IsSupportedExecutablePath(executablePath) ||
                    IsCustomExcludedApp(process.ProcessName, executablePath, versionInfo, customExcludedExecutableNames))
                {
                    continue;
                }

                if (!LooksLikeDesktopApp(process, executablePath))
                {
                    continue;
                }

                var displayName = FirstNonEmpty(
                    versionInfo?.ProductName,
                    versionInfo?.FileDescription,
                    Path.GetFileNameWithoutExtension(executablePath),
                    process.ProcessName);

                yield return new AppTarget(
                    process.ProcessName,
                    $"{displayName} ({process.ProcessName}.exe)",
                    executablePath);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static IReadOnlyList<WindowCandidate> EnumerateCandidateWindows(int excludedProcessId)
    {
        var results = new Dictionary<int, WindowCandidate>();

        EnumWindows((windowHandle, _) =>
        {
            if (!IsCandidateWindow(windowHandle))
            {
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out var processId);
            if (processId == 0 || processId == excludedProcessId)
            {
                return true;
            }

            var title = GetWindowTitle(windowHandle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (!results.TryGetValue(processId, out var existing) || title.Length > existing.Title.Length)
            {
                results[processId] = new WindowCandidate(processId, title);
            }

            return true;
        }, IntPtr.Zero);

        return results.Values.ToList();
    }

    private static bool IsCandidateWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !IsWindowVisible(windowHandle))
        {
            return false;
        }

        if (GetWindow(windowHandle, GwOwner) != IntPtr.Zero)
        {
            return false;
        }

        var extendedStyle = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
        if ((extendedStyle & WsExToolWindow) != 0)
        {
            return false;
        }

        if (IsWindowCloaked(windowHandle))
        {
            return false;
        }

        var titleLength = GetWindowTextLength(windowHandle);
        if (titleLength <= 0)
        {
            return false;
        }

        var className = GetClassName(windowHandle);
        if (string.Equals(className, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeDesktopApp(Process process, string executablePath)
    {
        try
        {
            if (process.HasExited)
            {
                return false;
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return true;
            }

            if (!HasUiModuleLoaded(process))
            {
                return false;
            }

            var versionInfo = TryGetVersionInfo(executablePath);
            return !string.IsNullOrWhiteSpace(versionInfo?.ProductName) ||
                !string.IsNullOrWhiteSpace(versionInfo?.FileDescription);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasUiModuleLoaded(Process process)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                try
                {
                    var moduleName = module.ModuleName;
                    if (moduleName.Equals("user32.dll", StringComparison.OrdinalIgnoreCase) ||
                        moduleName.Equals("shell32.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                finally
                {
                    module.Dispose();
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static FileVersionInfo? TryGetVersionInfo(string executablePath)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(executablePath);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSupportedExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(executablePath);
        var windowsDirectory = EnsureTrailingSeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (normalizedPath.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsExcludedProcessName(string processName)
        => ExcludedProcessNames.Contains(processName);

    private static bool IsCustomExcludedApp(
        string processName,
        string executablePath,
        FileVersionInfo? versionInfo,
        IReadOnlySet<string> customExcludedExecutableNames)
    {
        var executableName = NormalizeExecutableName(executablePath);
        var normalizedProcessExecutableName = NormalizeExecutableName(processName);

        if (CustomExcludedProcessNames.Contains(processName) ||
            IsExecutableNameExcluded(customExcludedExecutableNames, executableName) ||
            IsExecutableNameExcluded(customExcludedExecutableNames, normalizedProcessExecutableName))
        {
            return true;
        }

        if (ContainsAny(executablePath, CustomExcludedPathKeywords))
        {
            return true;
        }

        return ContainsAny(versionInfo?.CompanyName, CustomExcludedCompanyKeywords) ||
            ContainsAny(versionInfo?.ProductName, CustomExcludedProductKeywords) ||
            ContainsAny(versionInfo?.FileDescription, CustomExcludedProductKeywords);
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool ContainsAny(string? value, IEnumerable<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExecutableNameExcluded(IReadOnlySet<string> customExcludedExecutableNames, string normalizedExecutableName)
        => !string.IsNullOrWhiteSpace(normalizedExecutableName) &&
            customExcludedExecutableNames.Contains(normalizedExecutableName);

    private static string NormalizeExecutableName(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var withoutInlineComment = trimmed.Split('#', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(withoutInlineComment))
        {
            return string.Empty;
        }

        var unquoted = withoutInlineComment.Trim('"');
        var fileName = Path.GetFileName(unquoted);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".exe";
        }

        return fileName.ToLowerInvariant();
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static bool IsWindowCloaked(IntPtr windowHandle)
    {
        try
        {
            return DwmGetWindowAttribute(
                windowHandle,
                DwmwaCloaked,
                out var cloaked,
                Marshal.SizeOf<int>()) == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetWindowTitle(IntPtr windowHandle)
    {
        var titleLength = GetWindowTextLength(windowHandle);
        if (titleLength <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(titleLength + 1);
        return GetWindowText(windowHandle, buffer, buffer.Capacity) > 0
            ? buffer.ToString().Trim()
            : string.Empty;
    }

    private static string GetClassName(IntPtr windowHandle)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(windowHandle, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    private readonly record struct WindowCandidate(int ProcessId, string Title);

    private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost",
        "explorer",
        "LockApp",
        "SearchHost",
        "SearchApp",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "SystemSettings",
        "TextInputHost",
        "Widgets",
        "WidgetBoard",
        "RuntimeBroker",
        "svchost",
        "dwm"
    };

    private static readonly HashSet<string> CustomExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RadeonSoftware",
        "AMDSoftware",
        "AMDRSServ",
        "atieclxx",
        "atiesrxx",
        "cncmd",
        "IntelGraphicsSoftware",
        "IntelCpHDCPSvc",
        "igfxCUIServiceN",
        "igfxEM",
        "igfxHK",
        "igfxTray",
        "ArcControl",
        "ArcControlAssist",
        "BraveUpdate",
        "GoogleUpdate"
    };

    private static readonly string[] CustomExcludedCompanyKeywords =
    {
        "Advanced Micro Devices",
        "AMD",
        "Intel Corporation",
        "Google LLC",
        "Brave Software, Inc."
    };

    private static readonly string[] CustomExcludedProductKeywords =
    {
        "Radeon Software",
        "AMD Software",
        "Adrenalin Edition",
        "Intel Graphics",
        "Intel HD Graphics",
        "Intel Arc Control",
        "Graphics Command Center",
        "Google Update",
        "Brave Update"
    };

    private static readonly string[] CustomExcludedPathKeywords =
    {
        "\\AMD\\CNext\\",
        "\\AMD\\Performance Profile Client\\",
        "\\Intel\\Intel(R) Graphics Command Center\\",
        "\\Intel\\GFXC\\",
        "\\Intel\\ArcControl\\",
        "\\Google\\Update\\",
        "\\BraveSoftware\\Update\\"
    };

    private const int GwOwner = 4;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const int DwmwaCloaked = 14;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
}

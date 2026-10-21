using System;
using System.ComponentModel;
using System.Linq;
using System.Diagnostics;
using System.Security.Principal;

namespace Shutdown_Scheduler.Services;

public static class ElevationService
{
    // Passed to the elevated instance so it restores the Forced/admin toggle.
    public const string ForcedAdminArg = "--forced-admin";

    public static bool StartedForForcedAdmin =>
        IsElevated && Environment.GetCommandLineArgs().Contains(ForcedAdminArg);

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Relaunches the current exe with admin privileges.
    /// Returns false if the user cancelled the UAC prompt.
    /// </summary>
    public static bool TryRestartAsAdmin()
    {
        var exePath = Process.GetCurrentProcess().MainModule!.FileName;
        try
        {
            Process.Start(new ProcessStartInfo(exePath, ForcedAdminArg)
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch (Win32Exception)
        {
            // User cancelled UAC
            return false;
        }
    }
}

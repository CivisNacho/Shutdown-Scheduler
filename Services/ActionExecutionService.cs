using System.Diagnostics;
using System.Runtime.InteropServices;
using Shutdown_Scheduler.Models;

namespace Shutdown_Scheduler.Services;

public sealed class ActionExecutionService
{
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    public Task ExecuteAsync(PowerAction action, ExecutionMode mode)
    {
        return Task.Run(() =>
        {
            switch (action)
            {
                case PowerAction.Shutdown:
                    StartShutdown("/s", mode == ExecutionMode.ForcedAdmin);
                    break;
                case PowerAction.Restart:
                    StartShutdown("/r", mode == ExecutionMode.ForcedAdmin);
                    break;
                case PowerAction.LogOff:
                    StartShutdown("/l", false);
                    break;
                case PowerAction.LockSession:
                    LockWorkStation();
                    break;
                case PowerAction.Suspend:
                    SetSuspendState(false, false, false);
                    break;
                case PowerAction.Hibernate:
                    if (!SetSuspendState(true, false, false))
                    {
                        throw new InvalidOperationException("Hibernate is not available on this system.");
                    }
                    break;
                default:
                    throw new NotSupportedException(action.ToString());
            }
        });
    }

    private static void StartShutdown(string actionFlag, bool forced)
    {
        var args = $"{actionFlag} /t 0";
        if (forced) args += " /f";

        var startInfo = new ProcessStartInfo("shutdown.exe", args)
        {
            UseShellExecute = true,
            Verb = forced ? "runas" : string.Empty
        };

        Process.Start(startInfo);
    }

    public static bool SupportsForcedMode(PowerAction action)
        => action is PowerAction.Shutdown or PowerAction.Restart;
}

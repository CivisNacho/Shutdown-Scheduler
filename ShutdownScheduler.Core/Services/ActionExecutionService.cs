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

    public Task ExecuteAsync(ScheduleRequest request)
    {
        return Task.Run(() =>
        {
            switch (request.Action)
            {
                case PowerAction.Shutdown:
                    StartShutdown("/s", request.ExecutionMode == ExecutionMode.ForcedAdmin);
                    break;
                case PowerAction.Restart:
                    StartShutdown("/r", request.ExecutionMode == ExecutionMode.ForcedAdmin);
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
                case PowerAction.CloseApp:
                    CloseApp(request.TargetProcessName);
                    break;
                default:
                    throw new NotSupportedException(request.Action.ToString());
            }
        });
    }

    private static void CloseApp(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            throw new InvalidOperationException("No app was selected.");
        }

        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            throw new InvalidOperationException("The selected app is no longer running.");
        }

        var closedAny = false;
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited && process.CloseMainWindow())
                {
                    closedAny = true;
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill(true);
                    }
                }
                else if (!process.HasExited)
                {
                    process.Kill(true);
                    closedAny = true;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        if (!closedAny)
        {
            throw new InvalidOperationException("The selected app could not be closed.");
        }
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

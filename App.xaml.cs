using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Shutdown_Scheduler.Services;

namespace Shutdown_Scheduler_WinUI
{
    public partial class App : Application
    {
        private AppSession? _session;

        public App()
        {
            InitializeComponent();
            RegisterExceptionLogging();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                _session = new AppSession(dispatcherQueue);
                _session.ShowMainWindow();
            }
            catch (Exception ex)
            {
                LogFatal("OnLaunched", ex);
                throw;
            }
        }

        private void RegisterExceptionLogging()
        {
            UnhandledException += (_, e) =>
            {
                try
                {
                    LogFatal("Application.UnhandledException", e.Exception);
                }
                catch
                {
                    // Best-effort only.
                }
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    if (e.ExceptionObject is Exception ex)
                    {
                        LogFatal("AppDomain.UnhandledException", ex);
                    }
                }
                catch
                {
                    // Best-effort only.
                }
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                try
                {
                    LogFatal("TaskScheduler.UnobservedTaskException", e.Exception);
                }
                catch
                {
                    // Best-effort only.
                }
            };
        }

        private static void LogFatal(string where, Exception ex)
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShutdownScheduler",
                "Logs");

            Directory.CreateDirectory(logDir);

            var path = Path.Combine(logDir, "startup-crash.log");
            var payload =
                $"[{DateTimeOffset.Now:O}] {where}{Environment.NewLine}{ex}{Environment.NewLine}---{Environment.NewLine}";

            File.AppendAllText(path, payload);
            Debug.WriteLine(payload);
        }
    }
}

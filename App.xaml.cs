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
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _session = new AppSession(dispatcherQueue);
            _session.ShowMainWindow();
        }
    }
}

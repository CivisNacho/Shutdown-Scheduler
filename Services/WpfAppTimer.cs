using System.Windows.Threading;

namespace Shutdown_Scheduler.Services;

public sealed class WpfAppTimerFactory : IAppTimerFactory
{
    public IAppTimer Create(TimeSpan interval) => new WpfAppTimer(interval);
}

public sealed class WpfAppTimer : IAppTimer
{
    private readonly DispatcherTimer _timer;

    public WpfAppTimer(TimeSpan interval)
    {
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, e) => Tick?.Invoke(this, e);
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public event EventHandler? Tick;

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}


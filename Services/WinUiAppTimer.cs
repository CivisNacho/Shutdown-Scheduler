using System;
using Microsoft.UI.Dispatching;

namespace Shutdown_Scheduler.Services;

public sealed class WinUiAppTimerFactory : IAppTimerFactory
{
    private readonly DispatcherQueue _dispatcherQueue;

    public WinUiAppTimerFactory(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public IAppTimer Create(TimeSpan interval)
        => new WinUiAppTimer(_dispatcherQueue, interval);
}

public sealed class WinUiAppTimer : IAppTimer
{
    private readonly DispatcherQueueTimer _timer;

    public WinUiAppTimer(DispatcherQueue dispatcherQueue, TimeSpan interval)
    {
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = interval;
        _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
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

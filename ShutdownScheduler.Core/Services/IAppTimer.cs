namespace Shutdown_Scheduler.Services;

public interface IAppTimer
{
    TimeSpan Interval { get; set; }
    event EventHandler? Tick;
    void Start();
    void Stop();
}

public interface IAppTimerFactory
{
    IAppTimer Create(TimeSpan interval);
}


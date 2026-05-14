using System.Diagnostics;
using System.Net.NetworkInformation;
using Shutdown_Scheduler.Models;

namespace Shutdown_Scheduler.Services;

public sealed class CpuMonitorService : IDisposable
{
    private readonly PerformanceCounter? _counter;
    private readonly Queue<(DateTime Time, double Value)> _samples = new();

    public CpuMonitorService()
    {
        try
        {
            _counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _counter.NextValue();
        }
        catch
        {
            _counter = null;
        }
    }

    public bool IsAvailable => _counter is not null;

    public double SampleAverage()
    {
        if (_counter is null) return 0;
        try
        {
            AddSample(Math.Clamp(_counter.NextValue(), 0, 100));
        }
        catch
        {
            return 0;
        }

        return _samples.Count == 0 ? 0 : _samples.Average(sample => sample.Value);
    }

    private void AddSample(double value)
    {
        var now = DateTime.Now;
        _samples.Enqueue((now, value));
        while (_samples.Count > 0 && now - _samples.Peek().Time > TimeSpan.FromSeconds(60))
        {
            _samples.Dequeue();
        }
    }

    public void Dispose() => _counter?.Dispose();
}

public sealed class NetworkMonitorService
{
    private readonly Queue<(DateTime Time, double BytesPerSecond)> _samples = new();
    private long _lastBytes;
    private DateTime _lastTime = DateTime.MinValue;

    public double SampleAverageBytesPerSecond()
    {
        var now = DateTime.Now;
        var bytes = GetTotalBytes();
        if (_lastTime != DateTime.MinValue)
        {
            var seconds = Math.Max((now - _lastTime).TotalSeconds, 0.1);
            var delta = Math.Max(0, bytes - _lastBytes);
            AddSample(delta / seconds);
        }

        _lastBytes = bytes;
        _lastTime = now;
        return _samples.Count == 0 ? 0 : _samples.Average(sample => sample.BytesPerSecond);
    }

    private static long GetTotalBytes()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .Sum(adapter =>
            {
                var stats = adapter.GetIPv4Statistics();
                return stats.BytesReceived + stats.BytesSent;
            });
    }

    private void AddSample(double bytesPerSecond)
    {
        var now = DateTime.Now;
        _samples.Enqueue((now, bytesPerSecond));
        while (_samples.Count > 0 && now - _samples.Peek().Time > TimeSpan.FromSeconds(60))
        {
            _samples.Dequeue();
        }
    }
}

public sealed class ConditionEvaluationService
{
    private readonly CpuMonitorService _cpu;
    private readonly NetworkMonitorService _network;
    private readonly LocalizationService _loc;

    public ConditionEvaluationService(CpuMonitorService cpu, NetworkMonitorService network, LocalizationService loc)
    {
        _cpu = cpu;
        _network = network;
        _loc = loc;
    }

    public ConditionEvaluationResult Evaluate(Models.ScheduleRequest request)
    {
        var cpuAverage = _cpu.SampleAverage();
        var networkAverage = _network.SampleAverageBytesPerSecond();

        if (request.NetworkConditionEnabled && networkAverage >= request.NetworkThresholdBytesPerSecond)
        {
            return new ConditionEvaluationResult
            {
                IsSatisfied = false,
                CpuAverage = cpuAverage,
                NetworkAverageBytesPerSecond = networkAverage,
                Message = $"{_loc.T("NetworkWaiting")} {_loc.T("CurrentAverage")}: {FormatBytes(networkAverage)}. {_loc.T("RequiredBelow")} {FormatBytes(request.NetworkThresholdBytesPerSecond)}."
            };
        }

        if (request.CpuConditionEnabled && cpuAverage >= request.CpuThresholdPercent)
        {
            return new ConditionEvaluationResult
            {
                IsSatisfied = false,
                CpuAverage = cpuAverage,
                NetworkAverageBytesPerSecond = networkAverage,
                Message = $"{_loc.T("CpuWaiting")} {_loc.T("CurrentAverage")}: {cpuAverage:0.0}%. {_loc.T("RequiredBelow")} {request.CpuThresholdPercent:0.0}%."
            };
        }

        return new ConditionEvaluationResult
        {
            IsSatisfied = true,
            CpuAverage = cpuAverage,
            NetworkAverageBytesPerSecond = networkAverage
        };
    }

    public static string FormatBytes(double bytesPerSecond)
        => bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / 1024 / 1024:0.00} MB/s"
            : $"{bytesPerSecond / 1024:0.0} KB/s";
}

using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Shutdown_Scheduler.Models;

namespace Shutdown_Scheduler.Services;

public sealed class CpuMonitorService : IDisposable
{
    private readonly PerformanceCounter? _counter;
    private readonly Queue<(DateTime Time, double Value)> _samples = new();
    private readonly Dictionary<string, ProcessCpuSampler> _processSamplers = new(StringComparer.OrdinalIgnoreCase);

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

    public double SampleAverageForProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return 0;
        }

        if (!_processSamplers.TryGetValue(processName, out var sampler))
        {
            sampler = new ProcessCpuSampler(processName);
            _processSamplers[processName] = sampler;
        }

        return sampler.SampleAverage();
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

    private sealed class ProcessCpuSampler
    {
        private readonly string _processName;
        private readonly Queue<(DateTime Time, double Value)> _samples = new();
        private readonly Dictionary<int, ProcessCpuSnapshot> _snapshots = new();

        public ProcessCpuSampler(string processName)
        {
            _processName = processName;
        }

        public double SampleAverage()
        {
            var now = DateTime.Now;
            double totalPercent = 0;
            var seenProcessIds = new HashSet<int>();

            foreach (var process in Process.GetProcessesByName(_processName))
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    seenProcessIds.Add(process.Id);
                    var totalProcessorTime = process.TotalProcessorTime;
                    if (_snapshots.TryGetValue(process.Id, out var previous))
                    {
                        var elapsed = Math.Max((now - previous.Time).TotalSeconds, 0.1);
                        var cpuDelta = Math.Max(0, (totalProcessorTime - previous.TotalProcessorTime).TotalSeconds);
                        totalPercent += cpuDelta / (elapsed * Environment.ProcessorCount) * 100;
                    }

                    _snapshots[process.Id] = new ProcessCpuSnapshot(now, totalProcessorTime);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            foreach (var processId in _snapshots.Keys.Except(seenProcessIds).ToList())
            {
                _snapshots.Remove(processId);
            }

            AddSample(Math.Clamp(totalPercent, 0, 100));
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
    }

    private readonly record struct ProcessCpuSnapshot(DateTime Time, TimeSpan TotalProcessorTime);
}

public sealed class NetworkMonitorService
{
    private readonly Queue<(DateTime Time, double BytesPerSecond)> _samples = new();
    private long _lastBytes;
    private DateTime _lastTime = DateTime.MinValue;
    private readonly Dictionary<string, ProcessNetworkSampler> _processSamplers = new(StringComparer.OrdinalIgnoreCase);

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

    public double SampleAverageBytesPerSecondForProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return 0;
        }

        if (!_processSamplers.TryGetValue(processName, out var sampler))
        {
            sampler = new ProcessNetworkSampler(processName);
            _processSamplers[processName] = sampler;
        }

        return sampler.SampleAverageBytesPerSecond();
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

    private sealed class ProcessNetworkSampler
    {
        private readonly string _processName;
        private readonly Queue<(DateTime Time, double BytesPerSecond)> _samples = new();
        private readonly Dictionary<int, long> _snapshots = new();
        private DateTime _lastTime = DateTime.MinValue;

        public ProcessNetworkSampler(string processName)
        {
            _processName = processName;
        }

        public double SampleAverageBytesPerSecond()
        {
            var now = DateTime.Now;
            long totalDelta = 0;
            var seenProcessIds = new HashSet<int>();

            foreach (var process in Process.GetProcessesByName(_processName))
            {
                try
                {
                    if (process.HasExited || !TryGetIoCounters(process.Handle, out var ioCounters))
                    {
                        continue;
                    }

                    seenProcessIds.Add(process.Id);
                    var totalBytes = checked((long)Math.Min(
                        long.MaxValue,
                        ioCounters.ReadTransferCount + ioCounters.WriteTransferCount + ioCounters.OtherTransferCount));

                    if (_snapshots.TryGetValue(process.Id, out var previousTotalBytes))
                    {
                        totalDelta += Math.Max(0, totalBytes - previousTotalBytes);
                    }

                    _snapshots[process.Id] = totalBytes;
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            foreach (var processId in _snapshots.Keys.Except(seenProcessIds).ToList())
            {
                _snapshots.Remove(processId);
            }

            if (_lastTime != DateTime.MinValue)
            {
                var seconds = Math.Max((now - _lastTime).TotalSeconds, 0.1);
                AddSample(totalDelta / seconds);
            }

            _lastTime = now;
            return _samples.Count == 0 ? 0 : _samples.Average(sample => sample.BytesPerSecond);
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IoCounters ioCounters);

    private static bool TryGetIoCounters(IntPtr processHandle, out IoCounters ioCounters)
    {
        try
        {
            return GetProcessIoCounters(processHandle, out ioCounters);
        }
        catch
        {
            ioCounters = default;
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }
}

public sealed class BatteryMonitorService
{
    public bool IsBatteryAvailable => TryGetBatteryPercentage(out _);

    public bool TryGetBatteryPercentage(out double percentage)
    {
        percentage = 0;

        try
        {
            if (!GetSystemPowerStatus(out var powerStatus))
            {
                return false;
            }

            if (powerStatus.BatteryFlag == BatteryFlagNoSystemBattery ||
                powerStatus.BatteryLifePercent is BatteryLifeUnknown or > 100)
            {
                return false;
            }

            percentage = Math.Clamp((double)powerStatus.BatteryLifePercent, 0, 100);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    private const byte BatteryFlagNoSystemBattery = 128;
    private const byte BatteryLifeUnknown = 255;
}

public sealed class ConditionEvaluationService
{
    private readonly CpuMonitorService _cpu;
    private readonly NetworkMonitorService _network;
    private readonly BatteryMonitorService _battery;
    private readonly LocalizationService _loc;

    public ConditionEvaluationService(
        CpuMonitorService cpu,
        NetworkMonitorService network,
        BatteryMonitorService battery,
        LocalizationService loc)
    {
        _cpu = cpu;
        _network = network;
        _battery = battery;
        _loc = loc;
    }

    public ConditionEvaluationResult Evaluate(Models.ScheduleRequest request)
    {
        var useAppScopedMetrics = request.Action == PowerAction.CloseApp &&
            !string.IsNullOrWhiteSpace(request.TargetProcessName);

        var cpuAverage = useAppScopedMetrics
            ? _cpu.SampleAverageForProcess(request.TargetProcessName)
            : _cpu.SampleAverage();
        var networkAverage = useAppScopedMetrics
            ? _network.SampleAverageBytesPerSecondForProcess(request.TargetProcessName)
            : _network.SampleAverageBytesPerSecond();
        var hasBatteryPercentage = _battery.TryGetBatteryPercentage(out var batteryPercent);

        if (request.NetworkConditionEnabled && networkAverage >= request.NetworkThresholdBytesPerSecond)
        {
            return new ConditionEvaluationResult
            {
                IsSatisfied = false,
                CpuAverage = cpuAverage,
                NetworkAverageBytesPerSecond = networkAverage,
                BatteryPercent = batteryPercent,
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
                BatteryPercent = batteryPercent,
                Message = $"{_loc.T("CpuWaiting")} {_loc.T("CurrentAverage")}: {cpuAverage:0.0}%. {_loc.T("RequiredBelow")} {request.CpuThresholdPercent:0.0}%."
            };
        }

        if (request.BatteryConditionEnabled)
        {
            if (!hasBatteryPercentage)
            {
                return new ConditionEvaluationResult
                {
                    IsSatisfied = false,
                    CpuAverage = cpuAverage,
                    NetworkAverageBytesPerSecond = networkAverage,
                    BatteryPercent = 0,
                    Message = _loc.T("BatteryUnavailable")
                };
            }

            if (batteryPercent >= request.BatteryThresholdPercent)
            {
                return new ConditionEvaluationResult
                {
                    IsSatisfied = false,
                    CpuAverage = cpuAverage,
                    NetworkAverageBytesPerSecond = networkAverage,
                    BatteryPercent = batteryPercent,
                    Message = $"{_loc.T("BatteryWaiting")} {_loc.T("CurrentAverage")}: {batteryPercent:0.0}%. {_loc.T("RequiredBelow")} {request.BatteryThresholdPercent:0.0}%."
                };
            }
        }

        return new ConditionEvaluationResult
        {
            IsSatisfied = true,
            CpuAverage = cpuAverage,
            NetworkAverageBytesPerSecond = networkAverage,
            BatteryPercent = batteryPercent
        };
    }

    public static string FormatBytes(double bytesPerSecond)
        => bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / 1024 / 1024:0.00} MB/s"
            : $"{bytesPerSecond / 1024:0.0} KB/s";
}
